namespace HobiaryLogPipe.Contracts;

/// <summary>
/// Opcje szyfrowania payload wiadomości (E2E, AES-256-GCM).
/// Odpowiada sekcji konfiguracji "PayloadEncryption".
///
/// Rotacja klucza:
///   Zdefiniuj wiele kluczy w <see cref="Keys"/> i wskaż aktywny przez <see cref="ActiveKeyId"/>.
///   Adapter deszyfruje wiadomości dowolnym kluczem z <see cref="Keys"/>.
///   InputPlugin szyfruje zawsze aktywnym kluczem (<see cref="ActiveKeyId"/>).
///
///   Migracja: dodaj nowy klucz do Keys, zmień ActiveKeyId → stare wiadomości
///   z poprzednim KeyId nadal będą poprawnie deszyfrowane.
/// </summary>
public sealed class PayloadEncryptionOptions
{
    /// <summary>Czy szyfrowanie payload jest włączone. Domyślnie false.</summary>
    public bool Enabled { get; init; } = false;

    /// <summary>
    /// (Uproszczona konfiguracja — jeden klucz)
    /// Klucz AES-256 zakodowany w Base64 (dokładnie 32 bajty → 44 znaki Base64).
    /// Używany gdy <see cref="Keys"/> jest pusty. Traktowany jak Keys["default"].
    /// </summary>
    public string KeyBase64 { get; init; } = string.Empty;

    /// <summary>
    /// Słownik kluczy: id → Base64(32B). Umożliwia rotację bez przestojów.
    /// Przykład: { "v1": "AAAA...", "v2": "BBBB..." }.
    /// </summary>
    public Dictionary<string, string> Keys { get; init; } = [];

    /// <summary>
    /// Id aktywnego klucza (z <see cref="Keys"/>) używanego do szyfrowania przez InputPlugin.
    /// Ignorowane gdy <see cref="Keys"/> jest pusty (używany jest <see cref="KeyBase64"/>).
    /// </summary>
    public string ActiveKeyId { get; init; } = "default";
}
