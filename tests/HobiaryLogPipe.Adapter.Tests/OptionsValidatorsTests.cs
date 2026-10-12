using HobiaryLogPipe.Adapter;
using Microsoft.Extensions.Options;

namespace HobiaryLogPipe.Adapter.Tests;

public sealed class OptionsValidatorsTests
{
    // ── AdapterOptionsValidator ───────────────────────────────────────────

    [Fact]
    public void AdapterOptions_Valid_ReturnsSuccess()
    {
        var opts = new AdapterOptions
        {
            BaseLogDirectory = "logs",
            AllowedPlugins = ["my-plugin"],
            UnknownPluginAction = "deadletter",
        };
        var result = new AdapterOptionsValidator().Validate(null, opts);
        Assert.True(result.Succeeded);
    }

    [Fact]
    public void AdapterOptions_EmptyBaseLogDirectory_Fails()
    {
        var opts = new AdapterOptions
        {
            BaseLogDirectory = "",
            AllowedPlugins = ["my-plugin"],
        };
        var result = new AdapterOptionsValidator().Validate(null, opts);
        Assert.True(result.Failed);
        Assert.Contains(result.Failures!, f => f.Contains("BaseLogDirectory"));
    }

    [Fact]
    public void AdapterOptions_EmptyAllowedPlugins_Fails()
    {
        var opts = new AdapterOptions
        {
            BaseLogDirectory = "logs",
            AllowedPlugins = [],
        };
        var result = new AdapterOptionsValidator().Validate(null, opts);
        Assert.True(result.Failed);
        Assert.Contains(result.Failures!, f => f.Contains("AllowedPlugins"));
    }

    [Fact]
    public void AdapterOptions_InvalidPluginId_Fails()
    {
        var opts = new AdapterOptions
        {
            BaseLogDirectory = "logs",
            AllowedPlugins = ["bad plugin!"],
        };
        var result = new AdapterOptionsValidator().Validate(null, opts);
        Assert.True(result.Failed);
        Assert.Contains(result.Failures!, f => f.Contains("bad plugin!"));
    }

    [Fact]
    public void AdapterOptions_InvalidUnknownPluginAction_Fails()
    {
        var opts = new AdapterOptions
        {
            BaseLogDirectory = "logs",
            AllowedPlugins = ["p"],
            UnknownPluginAction = "explode",
        };
        var result = new AdapterOptionsValidator().Validate(null, opts);
        Assert.True(result.Failed);
        Assert.Contains(result.Failures!, f => f.Contains("UnknownPluginAction"));
    }

    [Theory]
    [InlineData("deadletter")]
    [InlineData("ignore")]
    [InlineData("reject")]
    [InlineData("DEADLETTER")]
    public void AdapterOptions_KnownActions_Succeed(string action)
    {
        var opts = new AdapterOptions
        {
            BaseLogDirectory = "logs",
            AllowedPlugins = ["p"],
            UnknownPluginAction = action,
        };
        var result = new AdapterOptionsValidator().Validate(null, opts);
        Assert.True(result.Succeeded);
    }

    [Fact]
    public void AdapterOptions_HttpApiKey_TooShort_Fails()
    {
        var opts = new AdapterOptions
        {
            BaseLogDirectory = "logs",
            AllowedPlugins = ["p"],
            HttpApiKey = "short",
        };
        var result = new AdapterOptionsValidator().Validate(null, opts);
        Assert.True(result.Failed);
        Assert.Contains(result.Failures!, f => f.Contains("HttpApiKey"));
    }

    [Fact]
    public void AdapterOptions_HttpApiKey_LongEnough_Succeeds()
    {
        var opts = new AdapterOptions
        {
            BaseLogDirectory = "logs",
            AllowedPlugins = ["p"],
            HttpApiKey = "secret-key-123",
        };
        var result = new AdapterOptionsValidator().Validate(null, opts);
        Assert.True(result.Succeeded);
    }

    // ── RabbitMqOptionsValidator ──────────────────────────────────────────

    private static RabbitMqOptions ValidRabbitMq() => new RabbitMqOptions
    {
        Host = "localhost",
        Port = 5672,
        VirtualHost = "/",
        Username = "guest",
        Password = "guest",
        AnnounceExchange = "hobiary.announce",
        DataExchange = "hobiary.data",
        DeadLetterQueue = "hobiary.unknown",
        PrefetchCount = 50,
        ReconnectDelaySeconds = 5,
    };

    [Fact]
    public void RabbitMqOptions_Valid_ReturnsSuccess()
    {
        var result = new RabbitMqOptionsValidator().Validate(null, ValidRabbitMq());
        Assert.True(result.Succeeded);
    }

    [Fact]
    public void RabbitMqOptions_EmptyHost_Fails()
    {
        var opts = new RabbitMqOptions
        {
            Host = "",
            Port = 5672,
            Username = "guest",
            Password = "guest",
            AnnounceExchange = "e",
            DataExchange = "d",
            DeadLetterQueue = "q",
            PrefetchCount = 1,
            ReconnectDelaySeconds = 1,
        };
        var result = new RabbitMqOptionsValidator().Validate(null, opts);
        Assert.True(result.Failed);
        Assert.Contains(result.Failures!, f => f.Contains("Host"));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(65536)]
    public void RabbitMqOptions_InvalidPort_Fails(int port)
    {
        var opts = new RabbitMqOptions
        {
            Host = "localhost",
            Port = port,
            Username = "guest",
            Password = "guest",
            AnnounceExchange = "e",
            DataExchange = "d",
            DeadLetterQueue = "q",
            PrefetchCount = 1,
            ReconnectDelaySeconds = 1,
        };
        var result = new RabbitMqOptionsValidator().Validate(null, opts);
        Assert.True(result.Failed);
        Assert.Contains(result.Failures!, f => f.Contains("Port"));
    }

    [Fact]
    public void RabbitMqOptions_EmptyUsername_Fails()
    {
        var opts = new RabbitMqOptions
        {
            Host = "localhost",
            Port = 5672,
            Username = "",
            Password = "guest",
            AnnounceExchange = "e",
            DataExchange = "d",
            DeadLetterQueue = "q",
            PrefetchCount = 1,
            ReconnectDelaySeconds = 1,
        };
        var result = new RabbitMqOptionsValidator().Validate(null, opts);
        Assert.True(result.Failed);
        Assert.Contains(result.Failures!, f => f.Contains("Username"));
    }

    [Fact]
    public void RabbitMqOptions_ZeroPrefetchCount_Fails()
    {
        var opts = new RabbitMqOptions
        {
            Host = "localhost",
            Port = 5672,
            Username = "guest",
            Password = "guest",
            AnnounceExchange = "e",
            DataExchange = "d",
            DeadLetterQueue = "q",
            PrefetchCount = 0,
            ReconnectDelaySeconds = 1,
        };
        var result = new RabbitMqOptionsValidator().Validate(null, opts);
        Assert.True(result.Failed);
        Assert.Contains(result.Failures!, f => f.Contains("PrefetchCount"));
    }

    [Fact]
    public void RabbitMqOptions_ZeroReconnectDelay_Fails()
    {
        var opts = new RabbitMqOptions
        {
            Host = "localhost",
            Port = 5672,
            Username = "guest",
            Password = "guest",
            AnnounceExchange = "e",
            DataExchange = "d",
            DeadLetterQueue = "q",
            PrefetchCount = 1,
            ReconnectDelaySeconds = 0,
        };
        var result = new RabbitMqOptionsValidator().Validate(null, opts);
        Assert.True(result.Failed);
        Assert.Contains(result.Failures!, f => f.Contains("ReconnectDelaySeconds"));
    }

    // ── RabbitMqOptions — TLS ─────────────────────────────────────────────

    [Fact]
    public void RabbitMqOptions_TlsDisabled_Valid()
    {
        var result = new RabbitMqOptionsValidator().Validate(null, ValidRabbitMq());
        Assert.True(result.Succeeded);
    }

    [Fact]
    public void RabbitMqOptions_TlsEnabled_NoServerName_Valid()
    {
        var opts = new RabbitMqOptions
        {
            Host = "localhost",
            Port = 5672,
            VirtualHost = "/",
            Username = "guest",
            Password = "guest",
            AnnounceExchange = "hobiary.announce",
            DataExchange = "hobiary.data",
            DeadLetterQueue = "hobiary.unknown",
            PrefetchCount = 50,
            ReconnectDelaySeconds = 5,
            Tls = new HobiaryLogPipe.Contracts.AmqpTlsOptions { Enabled = true },
        };
        var result = new RabbitMqOptionsValidator().Validate(null, opts);
        Assert.True(result.Succeeded);
    }

    [Fact]
    public void RabbitMqOptions_TlsEnabled_NonExistentCertPath_Fails()
    {
        var opts = new RabbitMqOptions
        {
            Host = "localhost",
            Port = 5672,
            VirtualHost = "/",
            Username = "guest",
            Password = "guest",
            AnnounceExchange = "hobiary.announce",
            DataExchange = "hobiary.data",
            DeadLetterQueue = "hobiary.unknown",
            PrefetchCount = 50,
            ReconnectDelaySeconds = 5,
            Tls = new HobiaryLogPipe.Contracts.AmqpTlsOptions
            {
                Enabled = true,
                CertPath = "C:/nonexistent/client.pfx",
            },
        };
        var result = new RabbitMqOptionsValidator().Validate(null, opts);
        Assert.True(result.Failed);
        Assert.Contains(result.Failures!, f => f.Contains("CertPath"));
    }

    // ── AdapterOptions — PayloadEncryption ───────────────────────────────

    [Fact]
    public void AdapterOptions_PayloadEncryptionDisabled_Valid()
    {
        var opts = new AdapterOptions
        {
            BaseLogDirectory = "logs",
            AllowedPlugins = ["p"],
            UnknownPluginAction = "deadletter",
        };
        var result = new AdapterOptionsValidator().Validate(null, opts);
        Assert.True(result.Succeeded);
    }

    [Fact]
    public void AdapterOptions_PayloadEncryptionEnabled_ValidKey_Succeeds()
    {
        var key32 = Convert.ToBase64String(new byte[32]);
        var opts = new AdapterOptions
        {
            BaseLogDirectory = "logs",
            AllowedPlugins = ["p"],
            UnknownPluginAction = "deadletter",
            PayloadEncryption = new HobiaryLogPipe.Contracts.PayloadEncryptionOptions
            {
                Enabled = true,
                KeyBase64 = key32,
            }
        };
        var result = new AdapterOptionsValidator().Validate(null, opts);
        Assert.True(result.Succeeded);
    }

    [Fact]
    public void AdapterOptions_PayloadEncryptionEnabled_EmptyKey_Fails()
    {
        var opts = new AdapterOptions
        {
            BaseLogDirectory = "logs",
            AllowedPlugins = ["p"],
            UnknownPluginAction = "deadletter",
            PayloadEncryption = new HobiaryLogPipe.Contracts.PayloadEncryptionOptions
            {
                Enabled = true,
                KeyBase64 = string.Empty,
            }
        };
        var result = new AdapterOptionsValidator().Validate(null, opts);
        Assert.True(result.Failed);
        Assert.Contains(result.Failures!, f => f.Contains("KeyBase64"));
    }

    [Fact]
    public void AdapterOptions_PayloadEncryptionEnabled_WrongKeyLength_Fails()
    {
        var key16 = Convert.ToBase64String(new byte[16]); // AES-128 — za krótki
        var opts = new AdapterOptions
        {
            BaseLogDirectory = "logs",
            AllowedPlugins = ["p"],
            UnknownPluginAction = "deadletter",
            PayloadEncryption = new HobiaryLogPipe.Contracts.PayloadEncryptionOptions
            {
                Enabled = true,
                KeyBase64 = key16,
            }
        };
        var result = new AdapterOptionsValidator().Validate(null, opts);
        Assert.True(result.Failed);
        Assert.Contains(result.Failures!, f => f.Contains("32 bajty"));
    }

    [Fact]
    public void AdapterOptions_PayloadEncryptionEnabled_InvalidBase64_Fails()
    {
        var opts = new AdapterOptions
        {
            BaseLogDirectory = "logs",
            AllowedPlugins = ["p"],
            UnknownPluginAction = "deadletter",
            PayloadEncryption = new HobiaryLogPipe.Contracts.PayloadEncryptionOptions
            {
                Enabled = true,
                KeyBase64 = "nie!base64",
            }
        };
        var result = new AdapterOptionsValidator().Validate(null, opts);
        Assert.True(result.Failed);
        Assert.Contains(result.Failures!, f => f.Contains("Base64"));
    }
}
