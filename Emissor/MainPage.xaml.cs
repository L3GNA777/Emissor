using Microsoft.Maui.Controls;
using Microsoft.Maui.Dispatching;
using MQTTnet.Client;
using System.Diagnostics;
using System.IO.Ports;
using System.Text;
using System.Threading.Tasks;

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

    public MainPage()
    {
        InitializeComponent();
        _cts = new CancellationTokenSource();

        // Configuração do timer para animação
        _dotsTimer = Dispatcher.CreateTimer();
        _dotsTimer.Interval = TimeSpan.FromMilliseconds(500);
        _dotsTimer.Tick += UpdateDotsAnimation;

        // Inicializa componentes
        InitializeSystem();
    }

    private async void InitializeSystem()
    {
        try
        {
            await InitializeMqttAsync();
            InitializeSerialPort("COM3", 9600); // Ajuste para sua porta serial
            await UpdateStatus("Sistema inicializado");
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
            _mqttService = new MQTTService("broker.hivemq.com"); // Broker público para teste
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
                var valor = keyValue[1].Trim();

                var topic = tipo switch
                {
                    "tps" => "tps/data",
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
                        await _mqttService.PublishAsync(topic, valor);
                        await UpdateStatus($"Dados enviados: {tipo}={valor}");
                    }
                    catch (Exception ex)
                    {
                        await ShowError($"Falha ao publicar {tipo}: {ex.Message}");
                    }
                }
            }
        }
    }

    private async void Button_Clicked(object sender, EventArgs e)
    {
         // Feedback visual imediato
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
            InitializeSerialPort("COM3", 9600); // Ajuste a porta se necessário
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
                await UpdateStatus($"Comando enviado: {command}");
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
        await MainThread.InvokeOnMainThreadAsync(() =>
        {
            StatusLabel.Text = message;
            StatusLabel.TextColor = Colors.Black;
            Debug.WriteLine($"STATUS: {message}");
        });
    }

    private async Task ShowError(string errorMessage)
    {
        await MainThread.InvokeOnMainThreadAsync(() =>
        {
            ErrorsLabel.Text = errorMessage;
            ErrorsLabel.TextColor = Colors.Red;
        });
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