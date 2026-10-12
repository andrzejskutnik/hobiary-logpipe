using Microsoft.Extensions.Options;

namespace HobiaryLogPipe.Adapter;

internal sealed class AdapterOptionsValidator : IValidateOptions<AdapterOptions>
{
    public ValidateOptionsResult Validate(string? name, AdapterOptions options)
    {
        var errors = new List<string>();

        if (string.IsNullOrWhiteSpace(options.BaseLogDirectory))
            errors.Add("HobiaryLogPipe.BaseLogDirectory jest wymagane.");

        if (options.AllowedPlugins.Length == 0)
            errors.Add("HobiaryLogPipe.AllowedPlugins nie może być pusta — dodaj co najmniej jeden pluginId.");

        foreach (var pluginId in options.AllowedPlugins)
        {
            if (!PluginIdValidator.IsValid(pluginId))
                errors.Add($"HobiaryLogPipe.AllowedPlugins: '{pluginId}' jest nieprawidłowym pluginId " +
                           "(dozwolone: [a-zA-Z0-9_.-]{1,64}).");
        }

        var allowed = new[] { "deadletter", "ignore", "reject" };
        if (!allowed.Contains(options.UnknownPluginAction, StringComparer.OrdinalIgnoreCase))
            errors.Add($"HobiaryLogPipe.UnknownPluginAction musi być jednym z: {string.Join(", ", allowed)}.");

        if (!string.IsNullOrEmpty(options.HttpApiKey) && options.HttpApiKey.Length < 8)
            errors.Add("HobiaryLogPipe.HttpApiKey musi mieć co najmniej 8 znaków albo być pusty (wyłączone uwierzytelnianie HTTP).");

        if (options.PayloadEncryption.Enabled)
        {
            var hasMultiKey = options.PayloadEncryption.Keys.Count > 0;

            if (hasMultiKey)
            {
                foreach (var (keyId, b64) in options.PayloadEncryption.Keys)
                {
                    if (string.IsNullOrWhiteSpace(keyId))
                    {
                        errors.Add("HobiaryLogPipe.PayloadEncryption.Keys: id klucza nie może być pusty.");
                        continue;
                    }
                    try
                    {
                        var k = Convert.FromBase64String(b64);
                        if (k.Length != 32)
                            errors.Add($"HobiaryLogPipe.PayloadEncryption.Keys['{keyId}'] musi kodować 32 bajty, podano {k.Length}.");
                    }
                    catch (FormatException)
                    {
                        errors.Add($"HobiaryLogPipe.PayloadEncryption.Keys['{keyId}'] nie jest poprawnym Base64.");
                    }
                }

                if (!options.PayloadEncryption.Keys.ContainsKey(options.PayloadEncryption.ActiveKeyId))
                    errors.Add($"HobiaryLogPipe.PayloadEncryption.ActiveKeyId='{options.PayloadEncryption.ActiveKeyId}' " +
                               "nie istnieje w Keys.");
            }
            else
            {
                if (string.IsNullOrWhiteSpace(options.PayloadEncryption.KeyBase64))
                {
                    errors.Add("HobiaryLogPipe.PayloadEncryption.KeyBase64 jest wymagane gdy Enabled=true i Keys jest pusty.");
                }
                else
                {
                    try
                    {
                        var key = Convert.FromBase64String(options.PayloadEncryption.KeyBase64);
                        if (key.Length != 32)
                            errors.Add($"HobiaryLogPipe.PayloadEncryption.KeyBase64 musi kodować 32 bajty (AES-256), podano {key.Length}.");
                    }
                    catch (FormatException)
                    {
                        errors.Add("HobiaryLogPipe.PayloadEncryption.KeyBase64 nie jest poprawnym Base64.");
                    }
                }
            }
        }

        return errors.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(errors);
    }
}

internal sealed class RabbitMqOptionsValidator : IValidateOptions<RabbitMqOptions>
{
    public ValidateOptionsResult Validate(string? name, RabbitMqOptions options)
    {
        var errors = new List<string>();

        if (string.IsNullOrWhiteSpace(options.Host))
            errors.Add("RabbitMQ.Host jest wymagane.");

        if (options.Port is <= 0 or > 65535)
            errors.Add($"RabbitMQ.Port={options.Port} jest nieprawidłowy (dozwolony zakres: 1–65535).");

        if (string.IsNullOrWhiteSpace(options.Username))
            errors.Add("RabbitMQ.Username jest wymagane.");

        if (string.IsNullOrWhiteSpace(options.Password))
            errors.Add("RabbitMQ.Password jest wymagane.");

        if (string.IsNullOrWhiteSpace(options.AnnounceExchange))
            errors.Add("RabbitMQ.AnnounceExchange jest wymagane.");

        if (string.IsNullOrWhiteSpace(options.DataExchange))
            errors.Add("RabbitMQ.DataExchange jest wymagane.");

        if (string.IsNullOrWhiteSpace(options.DeadLetterQueue))
            errors.Add("RabbitMQ.DeadLetterQueue jest wymagane.");

        if (options.PrefetchCount <= 0)
            errors.Add($"RabbitMQ.PrefetchCount={options.PrefetchCount} musi być > 0.");

        if (options.ReconnectDelaySeconds <= 0)
            errors.Add($"RabbitMQ.ReconnectDelaySeconds={options.ReconnectDelaySeconds} musi być > 0.");

        if (options.Tls.Enabled
            && !string.IsNullOrEmpty(options.Tls.CertPath)
            && !File.Exists(options.Tls.CertPath))
            errors.Add($"RabbitMQ.Tls.CertPath='{options.Tls.CertPath}' — plik nie istnieje.");

        return errors.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(errors);
    }
}
