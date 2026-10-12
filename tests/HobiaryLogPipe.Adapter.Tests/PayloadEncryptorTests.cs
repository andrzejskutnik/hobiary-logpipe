using System.Security.Cryptography;
using HobiaryLogPipe.Contracts;

namespace HobiaryLogPipe.Adapter.Tests;

public sealed class PayloadEncryptorTests
{
    private static byte[] ValidKey() => RandomNumberGenerator.GetBytes(32);

    // ── KeyFromBase64 ──────────────────────────────────────────────────────

    [Fact]
    public void KeyFromBase64_Valid32Bytes_ReturnsKey()
    {
        var raw = RandomNumberGenerator.GetBytes(32);
        var key = PayloadEncryptor.KeyFromBase64(Convert.ToBase64String(raw));
        Assert.Equal(raw, key);
    }

    [Theory]
    [InlineData(16)]
    [InlineData(24)]
    [InlineData(48)]
    public void KeyFromBase64_WrongLength_Throws(int len)
    {
        var b64 = Convert.ToBase64String(RandomNumberGenerator.GetBytes(len));
        Assert.Throws<ArgumentException>(() => PayloadEncryptor.KeyFromBase64(b64));
    }

    [Fact]
    public void KeyFromBase64_InvalidBase64_Throws()
    {
        Assert.Throws<FormatException>(() => PayloadEncryptor.KeyFromBase64("nie-to-base64!!!"));
    }

    // ── Encrypt / Decrypt roundtrip ───────────────────────────────────────

    [Fact]
    public void EncryptDecrypt_SmallPayload_Roundtrip()
    {
        var key       = ValidKey();
        var plaintext = "{\"level\":\"Error\",\"message\":\"test\"}"u8.ToArray();

        var encrypted = PayloadEncryptor.Encrypt(plaintext, key);
        var decrypted = PayloadEncryptor.Decrypt(encrypted, key);

        Assert.Equal(plaintext, decrypted);
    }

    [Fact]
    public void EncryptDecrypt_EmptyPayload_Roundtrip()
    {
        var key       = ValidKey();
        var plaintext = Array.Empty<byte>();

        var encrypted = PayloadEncryptor.Encrypt(plaintext, key);
        var decrypted = PayloadEncryptor.Decrypt(encrypted, key);

        Assert.Equal(plaintext, decrypted);
    }

    [Fact]
    public void Encrypt_SameInput_ProducesDifferentCiphertexts()
    {
        var key       = ValidKey();
        var plaintext = "hello"u8.ToArray();

        var enc1 = PayloadEncryptor.Encrypt(plaintext, key);
        var enc2 = PayloadEncryptor.Encrypt(plaintext, key);

        Assert.NotEqual(enc1, enc2); // różne nonce
    }

    [Fact]
    public void Encrypt_OutputLength_IsNoncePlusCiphertextPlusTag()
    {
        var key       = ValidKey();
        var plaintext = new byte[100];
        var encrypted = PayloadEncryptor.Encrypt(plaintext, key);

        Assert.Equal(12 + 100 + 16, encrypted.Length);
    }

    // ── Decrypt — błędy ───────────────────────────────────────────────────

    [Fact]
    public void Decrypt_WrongKey_ThrowsCryptographicException()
    {
        var key1      = ValidKey();
        var key2      = ValidKey();
        var encrypted = PayloadEncryptor.Encrypt("payload"u8.ToArray(), key1);

        Assert.ThrowsAny<CryptographicException>(() => PayloadEncryptor.Decrypt(encrypted, key2));
    }

    [Fact]
    public void Decrypt_TamperedCiphertext_ThrowsCryptographicException()
    {
        var key       = ValidKey();
        var encrypted = PayloadEncryptor.Encrypt("payload"u8.ToArray(), key);
        encrypted[15] ^= 0xFF; // uszkadzamy ciphertext

        Assert.ThrowsAny<CryptographicException>(() => PayloadEncryptor.Decrypt(encrypted, key));
    }

    [Fact]
    public void Decrypt_TooShortData_ThrowsCryptographicException()
    {
        var key  = ValidKey();
        var data = new byte[10]; // < 12 + 16

        Assert.Throws<CryptographicException>(() => PayloadEncryptor.Decrypt(data, key));
    }

    // ── HeaderName ────────────────────────────────────────────────────────

    [Fact]
    public void HeaderName_HasExpectedValue()
    {
        Assert.Equal("x-hobiary-encrypted", PayloadEncryptor.HeaderName);
    }
}
