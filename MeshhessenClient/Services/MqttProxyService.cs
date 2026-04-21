using System.Reflection;
using Meshtastic.Protobufs;
using MQTTnet;
using MQTTnet.Client;
using MQTTnet.Packets;

namespace MeshhessenClient.Services;

/// <summary>
/// Bidirectional MQTT proxy: forwards MqttClientProxyMessage packets between the
/// Meshtastic radio (serial/USB) and a real MQTT broker — exactly as the Android
/// app does when proxy_to_client_enabled = true.
/// </summary>
public sealed class MqttProxyService : IDisposable
{
    // ─── state ────────────────────────────────────────────────────────────────
    private readonly MeshtasticProtocolService _protocol;
    private IMqttClient?   _mqtt;
    private MQTTConfig?    _config;
    private string?        _rootTopic;
    private bool           _running;
    private bool           _disposed;

    // ─── events ───────────────────────────────────────────────────────────────
    /// <summary>Raised on the calling thread when connection state changes.</summary>
    public event EventHandler<string>? StatusChanged;

    // ─── version / node-id ────────────────────────────────────────────────────
    private static readonly string _appVersion =
        Assembly.GetExecutingAssembly()
                .GetCustomAttribute<AssemblyFileVersionAttribute>()
                ?.Version?.Split('.') is { } parts && parts.Length >= 3
                    ? $"{parts[0]}.{parts[1]}.{parts[2]}"
                    : "0.0.0";

    public MqttProxyService(MeshtasticProtocolService protocol)
    {
        _protocol = protocol;
    }

    // ─── public API ──────────────────────────────────────────────────────────

    public bool IsRunning => _running;

    /// <summary>
    /// Start the proxy. Idempotent — does nothing if already running with the
    /// same config.
    /// </summary>
    public async Task StartAsync(MQTTConfig mqttConfig, uint myNodeId)
    {
        if (_running) await StopAsync();          // restart on config change

        if (!mqttConfig.Enabled || !mqttConfig.ProxyToClientEnabled)
            return;

        _config = mqttConfig;
        _rootTopic = string.IsNullOrWhiteSpace(mqttConfig.Root) ? "msh" : mqttConfig.Root.TrimEnd('/');

        // ── parse broker address ──────────────────────────────────────────────
        var address = string.IsNullOrWhiteSpace(mqttConfig.Address)
            ? "mqtt.meshtastic.org"
            : mqttConfig.Address.Trim();

        string host;
        int    port;

        if (address.Contains(':'))
        {
            var idx = address.LastIndexOf(':');
            host = address[..idx];
            port = int.TryParse(address[(idx + 1)..], out var p) ? p
                   : (mqttConfig.TlsEnabled ? 8883 : 1883);
        }
        else
        {
            host = address;
            port = mqttConfig.TlsEnabled ? 8883 : 1883;
        }

        // ── build client options ──────────────────────────────────────────────
        var nodeIdHex  = $"{myNodeId:x8}";
        var clientId   = $"meshhessenclient-{_appVersion}-!{nodeIdHex}";

        var optBuilder = new MqttClientOptionsBuilder()
            .WithClientId(clientId)
            .WithTcpServer(host, port)
            .WithCleanSession(true)
            .WithKeepAlivePeriod(TimeSpan.FromSeconds(60));

        if (mqttConfig.TlsEnabled)
            optBuilder = optBuilder.WithTls();

        if (!string.IsNullOrEmpty(mqttConfig.Username))
            optBuilder = optBuilder.WithCredentials(mqttConfig.Username, mqttConfig.Password);

        // ── create + connect ──────────────────────────────────────────────────
        var factory = new MqttFactory();
        _mqtt = factory.CreateMqttClient();

        _mqtt.ApplicationMessageReceivedAsync += OnBrokerMessageAsync;
        _mqtt.DisconnectedAsync               += OnDisconnectedAsync;

        try
        {
            await _mqtt.ConnectAsync(optBuilder.Build(), CancellationToken.None);
        }
        catch (Exception ex)
        {
            RaiseStatus($"MQTT Proxy: Verbindungsfehler – {ex.Message}");
            _mqtt.Dispose();
            _mqtt = null;
            return;
        }

        // ── subscribe to root topic ───────────────────────────────────────────
        var subOpts = new MqttClientSubscribeOptionsBuilder()
            .WithTopicFilter(f => f.WithTopic($"{_rootTopic}/#"))
            .Build();

        await _mqtt.SubscribeAsync(subOpts);

        // ── hook radio → broker ───────────────────────────────────────────────
        _protocol.MqttProxyMessageReceived += OnRadioMqttMessage;

        _running = true;
        RaiseStatus($"MQTT Proxy aktiv — {host}:{port} als {clientId}");
    }

    public async Task StopAsync()
    {
        _protocol.MqttProxyMessageReceived -= OnRadioMqttMessage;
        _running = false;

        if (_mqtt is { IsConnected: true })
        {
            try { await _mqtt.DisconnectAsync(); } catch { /* best-effort */ }
        }

        _mqtt?.Dispose();
        _mqtt = null;
        RaiseStatus("MQTT Proxy gestoppt.");
    }

    // ─── broker → radio ──────────────────────────────────────────────────────

    private async Task OnBrokerMessageAsync(MqttApplicationMessageReceivedEventArgs e)
    {
        if (!_running) return;

        var msg = new MqttClientProxyMessage
        {
            Topic    = e.ApplicationMessage.Topic,
            Retained = e.ApplicationMessage.Retain,
            Data     = Google.Protobuf.ByteString.CopyFrom(
                           e.ApplicationMessage.PayloadSegment.ToArray()),
        };

        try
        {
            await _protocol.SendMqttProxyMessageAsync(msg);
        }
        catch (Exception ex)
        {
            RaiseStatus($"MQTT Proxy: Weiterleitungsfehler Broker→Radio: {ex.Message}");
        }
    }

    // ─── radio → broker ──────────────────────────────────────────────────────

    private async void OnRadioMqttMessage(object? sender, MqttClientProxyMessage msg)
    {
        if (_mqtt is not { IsConnected: true }) return;

        try
        {
            byte[] payload = msg.PayloadVariantCase switch
            {
                MqttClientProxyMessage.PayloadVariantOneofCase.Data =>
                    msg.Data.ToByteArray(),
                MqttClientProxyMessage.PayloadVariantOneofCase.Text =>
                    System.Text.Encoding.UTF8.GetBytes(msg.Text),
                _ => Array.Empty<byte>(),
            };

            var appMsg = new MqttApplicationMessageBuilder()
                .WithTopic(msg.Topic)
                .WithPayload(payload)
                .WithRetainFlag(msg.Retained)
                .Build();

            await _mqtt.PublishAsync(appMsg, CancellationToken.None);
        }
        catch (Exception ex)
        {
            RaiseStatus($"MQTT Proxy: Weiterleitungsfehler Radio→Broker: {ex.Message}");
        }
    }

    // ─── reconnect on unexpected disconnect ──────────────────────────────────

    private async Task OnDisconnectedAsync(MqttClientDisconnectedEventArgs e)
    {
        if (!_running || _config == null) return;

        RaiseStatus("MQTT Proxy: Verbindung verloren, versuche Reconnect in 5 s …");
        await Task.Delay(5_000);

        if (!_running || _mqtt == null) return;

        try
        {
            var host = _mqtt.Options.ChannelOptions is MqttClientTcpOptions tcp
                       ? tcp.RemoteEndpoint?.ToString() ?? "?" : "?";
            await _mqtt.ReconnectAsync();
            RaiseStatus($"MQTT Proxy: Reconnect erfolgreich ({host})");
        }
        catch (Exception ex)
        {
            RaiseStatus($"MQTT Proxy: Reconnect fehlgeschlagen – {ex.Message}");
        }
    }

    // ─── helpers ─────────────────────────────────────────────────────────────

    private void RaiseStatus(string msg) =>
        StatusChanged?.Invoke(this, msg);

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _ = StopAsync();
    }
}
