using MQTTnet;
using MQTTnet.Client;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace Emissor
{
    public class MQTTService : IDisposable
    {
        private readonly IMqttClient _mqttClient;
        private MqttClientOptions _options; // Removido o readonly
        private readonly int _reconnectDelayMs;

        public bool IsConnected => _mqttClient.IsConnected;

        public MQTTService(string brokerAddress, int port = 1883, int reconnectDelayMs = 2000)
        {
            var factory = new MqttFactory();
            _mqttClient = factory.CreateMqttClient();
            _reconnectDelayMs = reconnectDelayMs;

            _options = new MqttClientOptionsBuilder()
                .WithTcpServer(brokerAddress, port)
                .Build();
        }

        public async Task ConnectAsync()
        {
            if (!_mqttClient.IsConnected)
            {
                await _mqttClient.ConnectAsync(_options, CancellationToken.None);
            }
        }

        public async Task DisconnectAsync()
        {
            if (_mqttClient.IsConnected)
            {
                await _mqttClient.DisconnectAsync();
            }
        }

        public void UpdateBrokerAddress(string newBrokerAddress)
        {
            _options = new MqttClientOptionsBuilder()
                .WithTcpServer(newBrokerAddress)
                .Build();
        }

        public async Task PublishAsync(string topic, string payload)
        {
            while (true)
            {
                try
                {
                    if (!_mqttClient.IsConnected)
                    {
                        await TryReconnectAsync();
                    }

                    if (_mqttClient.IsConnected)
                    {
                        var message = new MqttApplicationMessageBuilder()
                            .WithTopic(topic)
                            .WithPayload(payload)
                            .Build();

                        await _mqttClient.PublishAsync(message, CancellationToken.None);
                        return;
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Publish failed: {ex.Message}. Will retry in {_reconnectDelayMs}ms...");
                    await Task.Delay(_reconnectDelayMs);
                }
            }
        }

        private async Task TryReconnectAsync()
        {
            try
            {
                await ConnectAsync();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Reconnect failed: {ex.Message}. Will retry in {_reconnectDelayMs}ms...");
                await Task.Delay(_reconnectDelayMs);
            }
        }

        public void Dispose()
        {
            _mqttClient?.Dispose();
            GC.SuppressFinalize(this);
        }
    }
}