using System.Net.Security;
using System.Security.Authentication;
using HobiaryLogPipe.Contracts;
using RabbitMQ.Client;

namespace HobiaryLogPipe.Adapter;

internal interface IRabbitMqConnectionFactory
{
    Task<IConnection> CreateConnectionAsync(CancellationToken cancellationToken);
}

internal sealed class RabbitMqConnectionFactory : IRabbitMqConnectionFactory
{
    private readonly RabbitMqOptions _options;

    public RabbitMqConnectionFactory(RabbitMqOptions options)
    {
        _options = options;
    }

    public async Task<IConnection> CreateConnectionAsync(CancellationToken cancellationToken)
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

        return await factory.CreateConnectionAsync(cancellationToken);
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
}