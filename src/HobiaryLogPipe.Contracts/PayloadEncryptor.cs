using System.Security.Cryptography;

namespace HobiaryLogPipe.Contracts;

/// <summary>
/// Szyfrowanie / deszyfrowanie payload wiadomości przy użyciu AES-256-GCM.
///
/// Format wire (bajty):
///   [nonce 12B] [ciphertext NB] [tag 16B]
///
/// Wiadomości zaszyfrowane oznaczane są nagłówkiem AMQP <see cref="HeaderName"/>.
/// </summary>
public static class PayloadEncryptor
{
    /// <summary>Nazwa nagłówka AMQP oznaczającego zaszyfrowany payload.</summary>
    public const string HeaderName = "x-hobiary-encrypted";

    private const int NonceLen = 12;
    private const int TagLen   = 16;

    /// <summary>
    /// Szyfruje <paramref name="plaintext"/> kluczem AES-256-GCM.
    /// Zwraca tablicę: nonce(12) + ciphertext(N) + tag(16).
    /// </summary>
    public static byte[] Encrypt(byte[] plaintext, byte[] key)
    {
        ArgumentNullException.ThrowIfNull(plaintext);
        ArgumentNullException.ThrowIfNull(key);

        using var aes = new AesGcm(key, TagLen);

        var nonce      = new byte[NonceLen];
        var ciphertext = new byte[plaintext.Length];
        var tag        = new byte[TagLen];

        RandomNumberGenerator.Fill(nonce);
        aes.Encrypt(nonce, plaintext, ciphertext, tag);

        var result = new byte[NonceLen + ciphertext.Length + TagLen];
        Buffer.BlockCopy(nonce,      0, result, 0,                              NonceLen);
        Buffer.BlockCopy(ciphertext, 0, result, NonceLen,                       ciphertext.Length);
        Buffer.BlockCopy(tag,        0, result, NonceLen + ciphertext.Length,   TagLen);
        return result;
    }

    /// <summary>
    /// Deszyfruje dane w formacie nonce(12) + ciphertext(N) + tag(16).
    /// Rzuca <see cref="CryptographicException"/> przy nieprawidłowym tagu lub zbyt krótkich danych.
    /// </summary>
    public static byte[] Decrypt(byte[] data, byte[] key)
    {
        ArgumentNullException.ThrowIfNull(data);
        ArgumentNullException.ThrowIfNull(key);

        if (data.Length < NonceLen + TagLen)
            throw new CryptographicException(
                $"Zaszyfrowany payload jest zbyt krótki ({data.Length} B); minimum {NonceLen + TagLen} B.");

        var nonce      = data[..NonceLen];
        var tag        = data[(data.Length - TagLen)..];
        var ciphertext = data[NonceLen..(data.Length - TagLen)];

        using var aes   = new AesGcm(key, TagLen);
        var plaintext   = new byte[ciphertext.Length];
        aes.Decrypt(nonce, ciphertext, tag, plaintext);
        return plaintext;
    }

    /// <summary>
    /// Dekoduje klucz AES-256 z Base64.
    /// Rzuca <see cref="ArgumentException"/> gdy długość != 32 bajty.
    /// </summary>
    public static byte[] KeyFromBase64(string base64)
    {
        var key = Convert.FromBase64String(base64);
        if (key.Length != 32)
            throw new ArgumentException(
                $"Klucz AES-256 musi mieć 32 bajty, podano {key.Length}.", nameof(base64));
        return key;
    }

    // ── Multi-key API ─────────────────────────────────────────────────────

    /// <summary>Nazwa nagłówka AMQP przechowującego id użytego klucza (rotacja).</summary>
    public const string KeyIdHeaderName = "x-hobiary-key-id";

    /// <summary>
    /// Szyfruje payload kluczem <paramref name="key"/> i oznacza wiadomość nagłówkami
    /// <see cref="HeaderName"/> oraz <see cref="KeyIdHeaderName"/> = <paramref name="keyId"/>.
    /// </summary>
    public static byte[] Encrypt(byte[] plaintext, byte[] key, string keyId,
        out Dictionary<string, object?> headers)
    {
        var ciphertext = Encrypt(plaintext, key);
        headers = new Dictionary<string, object?>
        {
            [HeaderName]       = 1,
            [KeyIdHeaderName]  = keyId,
        };
        return ciphertext;
    }

    /// <summary>
    /// Buduje słownik id→klucz z <see cref="PayloadEncryptionOptions"/>.
    /// Gdy <see cref="PayloadEncryptionOptions.Keys"/> jest pusty, tworzy wpis "default"
    /// z <see cref="PayloadEncryptionOptions.KeyBase64"/>.
    /// </summary>
    public static Dictionary<string, byte[]> BuildKeyMap(PayloadEncryptionOptions opts)
    {
        if (opts.Keys.Count > 0)
        {
            return opts.Keys.ToDictionary(
                kv => kv.Key,
                kv => KeyFromBase64(kv.Value),
                StringComparer.Ordinal);
        }

        return new Dictionary<string, byte[]>(StringComparer.Ordinal)
        {
            ["default"] = KeyFromBase64(opts.KeyBase64)
        };
    }

    /// <summary>
    /// Zwraca id aktywnego klucza na podstawie konfiguracji.
    /// Gdy <see cref="PayloadEncryptionOptions.Keys"/> jest pusty → "default".
    /// </summary>
    public static string ActiveKeyId(PayloadEncryptionOptions opts)
        => opts.Keys.Count > 0 ? opts.ActiveKeyId : "default";

    /// <summary>
    /// Odczytuje KeyId z nagłówków AMQP. Zwraca "default" gdy nagłówka brak.
    /// </summary>
    public static string ReadKeyId(IDictionary<string, object?>? headers)
    {
        if (headers is null) return "default";
        if (!headers.TryGetValue(KeyIdHeaderName, out var raw)) return "default";
        return raw switch
        {
            string s  => s,
            byte[] b  => System.Text.Encoding.UTF8.GetString(b),
            _         => "default",
        };
    }
}
