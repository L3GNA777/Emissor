using Microsoft.Maui.Controls;
using Microsoft.Maui.Dispatching;
using MQTTnet.Client;
using System.Diagnostics;
using System.IO.Ports;
using System.Text;
using System.Threading.Tasks;
using System.Text.RegularExpressions;

namespace Emissor;

public partial class MainPage : ContentPage
{
    private bool _isSending = false;
    private IDispatcherTimer _dotsTimer;
    private int _dotCount = 0;
    private const int MaxDots = 3;
    private SerialPort _serialPort;
    private MQTTService _mqttService;
    private CancellationTokenSource _cts;

    private readonly List<string> _statusHistory = new List<string>();
    private readonly List<string> _errorHistory = new List<string>();
    private const int MaxHistoryLines = 150;

    private int BaudRate = 9600;
    private string _brokerAddress;
    public MainPage()
    {
        InitializeComponent();
        _cts = new CancellationTokenSource();
        SerialPortPicker.SelectedIndexChanged += OnSerialPortChanged;
        _brokerAddress = "broker.hivemq.com"; 
        _mqttService = new MQTTService(_brokerAddress);

        _dotsTimer = Dispatcher.CreateTimer();
        _dotsTimer.Interval = TimeSpan.FromMilliseconds(500);
        _dotsTimer.Tick += UpdateDotsAnimation;

        LoadAvailableSerialPorts();

        InitializeSystem();
    }

    private async void SaveBrokerAddress(object sender, EventArgs e)
    {
        try
        {
            if (EntryBrokerAdress == null)
            {
                await ShowError("Componente de endereço não encontrado");
                return;
            }

            var rawAddress = EntryBrokerAdress.Text;
            var newBrokerAddress = rawAddress?.Trim() ?? string.Empty;

            if (string.IsNullOrWhiteSpace(newBrokerAddress))
            {
                await ShowError("Por favor, informe um endereço para o broker MQTT");
                await MainThread.InvokeOnMainThreadAsync(() => EntryBrokerAdress.Focus());
                return;
            }

            if (string.Equals(newBrokerAddress, _brokerAddress, StringComparison.OrdinalIgnoreCase))
            {
                await UpdateStatus("O endereço informado já está em uso");
                return;
            }

            if (!IsValidBrokerAddress(newBrokerAddress))
            {
                await ShowError("Formato de endereço inválido. Use 'hostname' ou 'host:porta'");
                return;
            }

            await UpdateStatus($"Atualizando broker para: {newBrokerAddress}...");

            if (_mqttService != null && _mqttService.IsConnected)
            {
                await _mqttService.DisconnectAsync();
                await UpdateStatus("Desconectado do broker anterior");
                await Task.Delay(500);
            }

            _brokerAddress = newBrokerAddress;

            if (_mqttService == null)
            {
                _mqttService = new MQTTService(_brokerAddress);
                await UpdateStatus("Serviço MQTT inicializado");
            }
            else
            {
                _mqttService.UpdateBrokerAddress(_brokerAddress);
                await UpdateStatus("Endereço do broker atualizado");
            }

            await UpdateStatus("Conectando ao novo broker...");

            try
            {
                await _mqttService.ConnectAsync();
                await UpdateStatus($"Conexão estabelecida com: {_brokerAddress}");
                await DisplayAlert("Sucesso", "Broker atualizado com sucesso!", "OK");
            }
            catch (Exception connectEx)
            {
                await ShowError($"Falha na conexão: {connectEx.Message}");
            }
        }
        catch (OperationCanceledException)
        {
            await ShowError("Operação cancelada");
        }
        catch (MQTTnet.Exceptions.MqttCommunicationException mqttEx)
        {
            await ShowError($"Erro de comunicação MQTT: {mqttEx.Message}");
        }
        catch (Exception ex)
        {
            await ShowError($"Erro inesperado: {ex.Message}");
        }
    }

    private bool IsValidBrokerAddress(string address)
    {
        if (string.IsNullOrWhiteSpace(address))
            return false;

        if (address.Contains(":"))
        {
            var parts = address.Split(':');
            if (parts.Length != 2)
                return false;

            if (!int.TryParse(parts[1], out int port) || port < 1 || port > 65535)
                return false;
        }

        return Uri.CheckHostName(address.Split(':')[0]) != UriHostNameType.Unknown;
    }

    private void OnB_R_ButtonClicked(object sender, EventArgs e)
    {
        try
        {
            int newBaudRate = Convert.ToInt32(EntryBaudRate.Text);

            if (_serialPort != null && _serialPort.IsOpen)
            {
                string currentPort = _serialPort.PortName;
                _serialPort.Close();
                _serialPort.Dispose();

                BaudRate = newBaudRate;
                InitializeSerialPort(currentPort, BaudRate);

                UpdateStatus($"Baud Rate alterado para {BaudRate}").Wait();
            }
            else
            {
                BaudRate = newBaudRate;
                UpdateStatus($"Baud Rate configurado para {BaudRate} (porta não aberta)").Wait();
            }
        }
        catch (FormatException)
        {
            ShowError("Valor de Baud Rate inválido").Wait();
        }
        catch (Exception ex)
        {
            ShowError($"Erro ao alterar Baud Rate: {ex.Message}").Wait();
        }
    }

    protected override void OnSizeAllocated(double width, double height)
    {
        base.OnSizeAllocated(width, height);

        if (Height >= 1000)
        {
            SerialPortPicker.FontSize = 26;

            tittleErrors.FontSize = 72;
            tittleStatus.FontSize = 72;

            Send.FontSize = 52;

            ErrorsLabel.FontSize = 18;
            StatusLabel.FontSize = 18;
        }
        else
        {
            SerialPortPicker.FontSize = 16;

            tittleErrors.FontSize = 60;
            tittleStatus.FontSize = 60;

            Send.FontSize = 36;

            ErrorsLabel.FontSize = 12;
            StatusLabel.FontSize = 12;
        }
    }

    private void LoadAvailableSerialPorts()
    {
        SerialPortPicker.ItemsSource = SerialPort.GetPortNames().ToList();

        if (SerialPortPicker.ItemsSource.Count > 0)
        {
            SerialPortPicker.SelectedIndex = 0;
        }
    }

    private void OnRefreshPortsClicked(object sender, EventArgs e)
    {
        LoadAvailableSerialPorts();
    }

    private async void InitializeSystem()
    {
        try
        {
            await InitializeMqttAsync();

            if (SerialPortPicker.SelectedItem == null)
            {
                await ShowError("Selecione uma porta serial primeiro");
                return;
            }

            string selectedPort = SerialPortPicker.SelectedItem.ToString();
            InitializeSerialPort(selectedPort, BaudRate);
            await UpdateStatus($"Sistema inicializado na porta {selectedPort}");
        }
        catch (Exception ex)
        {
            await ShowError($"Falha na inicialização: {ex.Message}");
        }
    }

    private async Task InitializeMqttAsync()
    {
        try
        {
            _mqttService = new MQTTService("broker.hivemq.com"); 
            await _mqttService.ConnectAsync();
            await UpdateStatus("Conectado ao broker MQTT");
        }
        catch (Exception ex)
        {
            await ShowError($"Erro MQTT: {ex.Message}");
            throw;
        }
    }

    private void InitializeSerialPort(string portName, int baudRate)
    {
        try
        {
            _serialPort = new SerialPort(portName, baudRate);
            _serialPort.DataReceived += SerialDataReceived;
            _serialPort.Open();
            UpdateStatus($"Porta serial {portName} aberta");
        }
        catch (Exception ex)
        {
            ShowError($"Erro serial: {ex.Message}");
            throw;
        }
    }

    private async void OnSerialPortChanged(object sender, EventArgs e)
    {
        if (SerialPortPicker.SelectedItem == null) return;

        string newPort = SerialPortPicker.SelectedItem.ToString();

        // Não faz nada se a porta não mudou
        if (_serialPort != null && _serialPort.PortName.Equals(newPort, StringComparison.OrdinalIgnoreCase))
            return;

        try
        {
            bool wasSending = _isSending;

            // Para o envio se estiver ativo
            if (wasSending)
            {
                await TryStopSending();
            }

            // Fecha a porta atual
            if (_serialPort != null)
            {
                _serialPort.Close();
                _serialPort.Dispose();
                await UpdateStatus($"Porta {_serialPort.PortName} fechada");
            }

            // Abre a nova porta
            InitializeSerialPort(newPort, BaudRate);
            await UpdateStatus($"Conectado à porta {newPort}");

            // Reinicia o envio se estiver ativo
            if (wasSending)
            {
                await TryStartSending();
            }
        }
        catch (Exception ex)
        {
            await ShowError($"Erro ao mudar para porta {newPort}: {ex.Message}");
        }
    }

    private async void SerialDataReceived(object sender, SerialDataReceivedEventArgs e)
    {
        try
        {
            string data = _serialPort.ReadLine().Trim();
            await ProcessSerialDataAsync(data);
        }
        catch (Exception ex)
        {
            await ShowError($"Erro na leitura serial: {ex.Message}");
        }
    }

    private async Task ProcessSerialDataAsync(string data)
    {
        if (!_isSending) return;

        var parts = data.Split(',');
        foreach (var part in parts)
        {
            var keyValue = part.Split(':');
            if (keyValue.Length == 2)
            {
                var tipo = keyValue[0].ToLower().Trim();
                var valorBruto = keyValue[1].Trim();

                var valorNumerico = Regex.Replace(valorBruto, @"[^0-9\.\-]", "");

                if (double.TryParse(valorNumerico, out double valor))
                {
                    var topic = tipo switch
                    {   
                        "ig_point" => "ig_point/data",
                        "fuel_press" => "fuel_press/data",
                        "oil_press" => "oil_press/data",
                        "l_probe" => "l_probe/data",
                        "r_probe" => "r_probe/data",
                        "g_probe" => "g_probe/data",
                        "air_temp" => "air_temp/data",
                        "battery" => "battery/data",
                        "map" => "map/data",
                        "gear" => "gear/data",
                        "tps" => "tps/data",
                        "acc" => "acceleration/data",
                        "rpm" => "rpm/data",
                        "speed" => "speed/data",
                        "engine" => "engine/data",
                        "oil" => "oil/data",
                        "disk" => "disk/data",
                        "brake" => "brake/data",
                        _ => null
                    };

                    if (topic != null)
                    {
                        try
                        {
                            await _mqttService.PublishAsync(topic, valor.ToString());
                            await UpdateStatus($"Dados enviados: {tipo}={valor}");
                        }
                        catch (Exception ex)
                        {
                            await ShowError($"Falha ao publicar {tipo}: {ex.Message}");
                        }
                    }
                }
                else
                {
                    await ShowError($"Valor inválido para {tipo}: {valorBruto}");
                }
            }
        }
    }

    private async void Button_Clicked(object sender, EventArgs e)
    {
         _isSending = !_isSending;
         Button.BackgroundColor = _isSending ? Colors.Green : Colors.Red;
         Send.Text = _isSending ? "Sending" : "Send";

         if (_isSending)
         {   
             await TryStartSending();
         }
         else
         {
             await TryStopSending();
         }

    }

    private async Task TryStartSending()
    {
        if (_serialPort == null || !_serialPort.IsOpen)
        {
            if (SerialPortPicker.SelectedItem == null)
            {
                await ShowError("Selecione uma porta serial primeiro");
                _isSending = false;
                Button.BackgroundColor = Colors.Red;
                Send.Text = "Send";
                return;
            }

            string selectedPort = SerialPortPicker.SelectedItem.ToString();
            InitializeSerialPort(selectedPort, 9600);
        }

        if (_mqttService == null)
        {
            await InitializeMqttAsync();
        }

        _dotCount = 0;
        _dotsTimer.Start();
        await SendSerialCommandAsync("START");
    }

    private async Task TryStopSending()
    {
        _dotsTimer.Stop();
        await SendSerialCommandAsync("STOP");
    }

    private async Task SendSerialCommandAsync(string command)
    {
        try
        {
            if (_serialPort?.IsOpen == true)
            {
                _serialPort.WriteLine(command);
                await _mqttService.PublishAsync("control/command", command);
                if(command == "START")
                {
                    await UpdateStatus("Enviando dados");
                }
            }
            else
            {
                throw new Exception("Porta serial não está aberta");
            }
        }
        catch (Exception ex)
        {
            await ShowError($"Falha no comando {command}: {ex.Message}");
            throw;
        }
    }

    private void UpdateDotsAnimation(object sender, EventArgs e)
    {
        MainThread.BeginInvokeOnMainThread(() =>
        {
            _dotCount = (_dotCount + 1) % (MaxDots + 1);
            Send.Text = "Sending" + new string('.', _dotCount);
        });
    }

    private async Task UpdateStatus(string message)
    {
        var timestampedMessage = $"[{DateTime.Now:HH:mm:ss}] {message}";

        lock (_statusHistory)
        {
            _statusHistory.Add(timestampedMessage);
            if (_statusHistory.Count > MaxHistoryLines)
            {
                _statusHistory.RemoveAt(0);
            }
        }

        await MainThread.InvokeOnMainThreadAsync(() =>
        {
            StatusLabel.Text = string.Join(Environment.NewLine, _statusHistory);

            _ = StatusScrollView.ScrollToAsync(0, StatusLabel.Height, true);
        });
    }

    private async void OnScrollUpClicked(object sender, EventArgs e)
    {
        await StatusScrollView.ScrollToAsync(0, StatusScrollView.ScrollY - 100, true);
    }

    private async void OnScrollDownClicked(object sender, EventArgs e)
    {
        await StatusScrollView.ScrollToAsync(0, StatusScrollView.ScrollY + 100, true);
    }

    private async Task ShowError(string errorMessage)
    {
        var timestampedMessage = $"[{DateTime.Now:HH:mm:ss}] ERRO: {errorMessage}";

        lock (_errorHistory)
        {
            _errorHistory.Add(timestampedMessage);
            if (_errorHistory.Count > MaxHistoryLines)
            {
                _errorHistory.RemoveAt(0);
            }
        }

        await MainThread.InvokeOnMainThreadAsync(() =>
        {
            ErrorsLabel.Text = string.Join(Environment.NewLine, _errorHistory);
            _ = ErrorsScrollView.ScrollToAsync(0, ErrorsLabel.Height, true);
        });
    }

    private async void OnScrollErrorsUpClicked(object sender, EventArgs e)
    {
        await ErrorsScrollView.ScrollToAsync(0, ErrorsScrollView.ScrollY - 100, true);
    }

    private async void OnScrollErrorsDownClicked(object sender, EventArgs e)
    {
        await ErrorsScrollView.ScrollToAsync(0, ErrorsScrollView.ScrollY + 100, true);
    }

    protected override async void OnDisappearing()
    {
        base.OnDisappearing();

        try
        {
            _dotsTimer?.Stop();
            _cts?.Cancel();

            if (_isSending)
            {
                await TryStopSending();
            }

            _serialPort?.Close();
            _serialPort?.Dispose();

            if (_mqttService != null)
            {
                await _mqttService.DisconnectAsync();
                _mqttService.Dispose();
            }

            await UpdateStatus("Aplicação finalizada");
        }
        catch (Exception ex)
        {
            await ShowError($"Erro ao finalizar: {ex.Message}");
        }
    }
}