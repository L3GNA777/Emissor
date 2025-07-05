using MQTTnet;
using MQTTnet.Client;
using System;
using System.Threading.Tasks;

namespace Emissor
{
    public class MQTTService : IDisposable
    {
        private readonly IMqttClient _mqttClient;
        private readonly MqttClientOptions _options;

        public MQTTService(string brokerAddress)
        {
            var factory = new MqttFactory();
            _mqttClient = factory.CreateMqttClient();

            _options = new MqttClientOptionsBuilder()
                .WithTcpServer(brokerAddress)
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

        public async Task PublishAsync(string topic, string payload)
        {
            if (_mqttClient.IsConnected)
            {
                var message = new MqttApplicationMessageBuilder()
                    .WithTopic(topic)
                    .WithPayload(payload)
                    .Build();

                await _mqttClient.PublishAsync(message, CancellationToken.None);
            }
            else
            {
                throw new Exception("MQTT client is not connected");
            }
        }

        public void Dispose()
        {
            _mqttClient?.Dispose();
            GC.SuppressFinalize(this);
        }
    }
}