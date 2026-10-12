using System.Text.Json;

namespace HobiaryLogPipe.Adapter;

/// <summary>
/// Waliduje obecność wymaganych pól JSON w wiadomości od pluginu.
/// Konfiguracja pochodzi z AdapterOptions.PluginRequiredFields.
/// </summary>
internal static class SchemaValidator
{
    /// <summary>
    /// Sprawdza czy <paramref name="root"/> zawiera wszystkie wymagane pola dla <paramref name="pluginId"/>.
    /// Zwraca <c>null</c> gdy walidacja przeszła, lub nazwę pierwszego brakującego pola.
    /// </summary>
    internal static string? FindMissingField(
        JsonElement root,
        string pluginId,
        Dictionary<string, string[]> requiredFields)
    {
        if (!requiredFields.TryGetValue(pluginId, out var fields) || fields.Length == 0)
            return null;

        foreach (var field in fields)
        {
            if (!root.TryGetProperty(field, out _))
                return field;
        }

        return null;
    }
}
