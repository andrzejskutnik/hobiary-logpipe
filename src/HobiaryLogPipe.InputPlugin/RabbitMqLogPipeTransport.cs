using System.IO.Compression;
using System.Net.Security;
using System.Security.Authentication;
using System.Text;
using System.Text.Json;
using HobiaryLogPipe.Contracts;
using Microsoft.Extensions.Logging;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace HobiaryLogPipe.InputPlugin;

internal sealed class RabbitMqLogPipeTransport : ILogPipeTransport
{
    private readonly ILogger _logger;
    private readonly InputPluginOptions _options;

    private IConnection? _connection;
    private IChannel? _channel;
    private CancellationToken _stoppingToken;
    private int _reconnecting;
    private Dictionary<string, byte[]>? _keyMap;
    private string _activeKeyId = "default";

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private static readonly ReadOnlyMemory<byte> DeregisterBody =
        new(Encoding.UTF8.GetBytes("{\"type\":\"deregister\"}"));

    private static readonly ReadOnlyMemory<byte> HeartbeatBody =
        new(Encoding.UTF8.GetBytes("{\"type\":\"heartbeat\"}"));

    public RabbitMqLogPipeTransport(ILogger logger, InputPluginOptions options)
    {
        _logger = logger;
        _options = options;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        _stoppingToken = cancellationToken;
        if (_options.PayloadEncryption.Enabled)
        {
            _keyMap = PayloadEncryptor.BuildKeyMap(_options.PayloadEncryption);
            _activeKeyId = PayloadEncryptor.ActiveKeyId(_options.PayloadEncryption);
        }

        try
        {
            await ConnectAsync(cancellationToken);
        }
        catch (OperationCanceledException) { return; }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "RabbitMQ unavailable on startup. Retrying in background...");
            _ = Task.Run(() => ReconnectLoopAsync(cancellationToken), CancellationToken.None);
        }

        if (_options.HeartbeatIntervalSeconds > 0)
            _ = Task.Run(() => HeartbeatLoopAsync(cancellationToken), CancellationToken.None);
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        if (_channel is not null)
        {
            try
            {
                await _channel.BasicPublishAsync(
                    exchange: _options.DataExchange,
                    routingKey: _options.PluginId,
                    body: DeregisterBody,
                    cancellationToken: cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Nie udało się wysłać deregister — połączenie mogło już być zamknięte.");
            }
        }

        await DisposeConnectionAsync();
    }

    public async Task WriteAsync(LogMessage message, CancellationToken cancellationToken)
    {
        if (_channel is null)
            throw new InvalidOperationException("LogPipeWriter not started. Call StartAsync first or register via AddHobiaryLogPipe.");

        var body = new ReadOnlyMemory<byte>(DirectLogPipeTransport.SerializeMessage(_options, message));

        if (_options.GzipEnabled)
        {
            var compressed = GzipCompress(body);
            if (_keyMap is not null)
            {
                if (!_keyMap.TryGetValue(_activeKeyId, out var activeKey))
                    throw new InvalidOperationException(
                        $"Aktywny klucz szyfrowania '{_activeKeyId}' nie istnieje w Keys.");

                var encrypted = PayloadEncryptor.Encrypt(compressed.ToArray(), activeKey, _activeKeyId, out var encHeaders);
                encHeaders![LogPipeWriter.GzipHeaderName] = LogPipeWriter.GzipHeaderValue;
                var props = new BasicProperties { Headers = encHeaders };
                await _channel.BasicPublishAsync(
                    exchange: _options.DataExchange,
                    routingKey: _options.PluginId,
                    mandatory: false,
                    basicProperties: props,
                    body: new ReadOnlyMemory<byte>(encrypted),
                    cancellationToken: cancellationToken);
            }
            else
            {
                var gzipProps = new BasicProperties
                {
                    Headers = new Dictionary<string, object?> { [LogPipeWriter.GzipHeaderName] = LogPipeWriter.GzipHeaderValue }
                };
                await _channel.BasicPublishAsync(
                    exchange: _options.DataExchange,
                    routingKey: _options.PluginId,
                    mandatory: false,
                    basicProperties: gzipProps,
                    body: compressed,
                    cancellationToken: cancellationToken);
            }
            return;
        }

        if (_keyMap is not null)
        {
            if (!_keyMap.TryGetValue(_activeKeyId, out var activeKey))
                throw new InvalidOperationException(
                    $"Aktywny klucz szyfrowania '{_activeKeyId}' nie istnieje w Keys.");

            var encrypted = PayloadEncryptor.Encrypt(body.ToArray(), activeKey, _activeKeyId, out var headers);
            var props = new BasicProperties { Headers = headers! };
            await _channel.BasicPublishAsync(
                exchange: _options.DataExchange,
                routingKey: _options.PluginId,
                mandatory: false,
                basicProperties: props,
                body: new ReadOnlyMemory<byte>(encrypted),
                cancellationToken: cancellationToken);
        }
        else
        {
            await _channel.BasicPublishAsync(
                exchange: _options.DataExchange,
                routingKey: _options.PluginId,
                body: body,
                cancellationToken: cancellationToken);
        }
    }

    public async ValueTask DisposeAsync()
    {
        await DisposeConnectionAsync();
    }

    private async Task ConnectAsync(CancellationToken ct)
    {
        var factory = new ConnectionFactory
        {
            HostName = _options.Host,
            Port = _options.Port,
            VirtualHost = _options.VirtualHost,
            UserName = _options.Username,
            Password = _options.Password,
        };
        if (_options.Tls.Enabled)
            ApplyTls(factory, _options.Tls, _options.Host);

        _connection = await factory.CreateConnectionAsync(ct);
        _connection.ConnectionShutdownAsync += OnConnectionShutdownAsync;
        _channel = await _connection.CreateChannelAsync(cancellationToken: ct);

        await _channel.ExchangeDeclareAsync(
            exchange: _options.AnnounceExchange,
            type: ExchangeType.Fanout,
            durable: true,
            cancellationToken: ct);

        await _channel.ExchangeDeclareAsync(
            exchange: _options.DataExchange,
            type: ExchangeType.Direct,
            durable: true,
            cancellationToken: ct);

        await AnnounceAsync(ct);
    }

    private Task OnConnectionShutdownAsync(object sender, ShutdownEventArgs e)
    {
        if (_stoppingToken.IsCancellationRequested) return Task.CompletedTask;
        _logger.LogWarning("RabbitMQ connection lost ({Reason}). Scheduling reconnect...", e.ReplyText);
        _ = Task.Run(() => ReconnectLoopAsync(_stoppingToken), CancellationToken.None);
        return Task.CompletedTask;
    }

    private async Task ReconnectLoopAsync(CancellationToken ct)
    {
        if (Interlocked.CompareExchange(ref _reconnecting, 1, 0) != 0)
            return;

        try
        {
            while (!ct.IsCancellationRequested)
            {
                try
                {
                    await Task.Delay(TimeSpan.FromSeconds(_options.ReconnectDelaySeconds), ct);
                    await DisposeConnectionAsync();
                    await ConnectAsync(ct);
                    _logger.LogInformation("Reconnected to RabbitMQ successfully.");
                    return;
                }
                catch (OperationCanceledException) { return; }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Reconnect failed. Next attempt in {Delay}s.", _options.ReconnectDelaySeconds);
                }
            }
        }
        finally
        {
            Interlocked.Exchange(ref _reconnecting, 0);
        }
    }

    private async Task HeartbeatLoopAsync(CancellationToken ct)
    {
        var interval = TimeSpan.FromSeconds(_options.HeartbeatIntervalSeconds);
        while (!ct.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(interval, ct);
                if (_channel is not null)
                    await _channel.BasicPublishAsync(
                        exchange: _options.DataExchange,
                        routingKey: _options.PluginId,
                        body: HeartbeatBody,
                        cancellationToken: ct);
            }
            catch (OperationCanceledException) { return; }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Heartbeat send failed — reconnect in progress?");
            }
        }
    }

    private async Task AnnounceAsync(CancellationToken ct)
    {
        var announce = new AnnounceMessage
        {
            PluginId = _options.PluginId,
            Version = _options.Version,
            MessageFormat = "json",
            FieldMapping = _options.FieldMapping,
        };

        var body = new ReadOnlyMemory<byte>(JsonSerializer.SerializeToUtf8Bytes(announce, JsonOpts));

        await _channel!.BasicPublishAsync(
            exchange: _options.AnnounceExchange,
            routingKey: string.Empty,
            body: body,
            cancellationToken: ct);

        _logger.LogInformation("Plugin '{PluginId}' announced on exchange '{Exchange}'",
            _options.PluginId, _options.AnnounceExchange);
    }

    private static ReadOnlyMemory<byte> GzipCompress(ReadOnlyMemory<byte> input)
    {
        using var ms = new MemoryStream();
        using (var gz = new GZipStream(ms, CompressionLevel.Optimal, leaveOpen: true))
            gz.Write(input.Span);
        return ms.ToArray();
    }

    private static void ApplyTls(ConnectionFactory factory, AmqpTlsOptions tls, string defaultHost)
    {
        factory.Ssl = new SslOption
        {
            Enabled = true,
            ServerName = tls.ServerName ?? defaultHost,
            Version = SslProtocols.Tls12 | SslProtocols.Tls13,
            AcceptablePolicyErrors = tls.AcceptInvalidCertificates
                ? SslPolicyErrors.RemoteCertificateNameMismatch | SslPolicyErrors.RemoteCertificateChainErrors
                : SslPolicyErrors.None,
        };
        if (!string.IsNullOrEmpty(tls.CertPath))
        {
            factory.Ssl.CertPath = tls.CertPath;
            factory.Ssl.CertPassphrase = tls.CertPassphrase ?? string.Empty;
        }
    }

    private async Task DisposeConnectionAsync()
    {
        if (_channel is not null)
        {
            await _channel.DisposeAsync();
            _channel = null;
        }
        if (_connection is not null)
        {
            await _connection.DisposeAsync();
            _connection = null;
        }
    }
}