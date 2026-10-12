namespace HobiaryLogPipe.Adapter;

/// <summary>
/// Parsuje nagłówki AMQP x-death z wiadomości trafiających do Dead-Letter Queue.
/// </summary>
internal static class DeadLetterParser
{
    /// <summary>
    /// Zwraca powód odrzucenia wiadomości z nagłówka x-death.
    /// Możliwe wartości RabbitMQ: "rejected", "expired", "maxlen", "delivery-limit".
    /// </summary>
    internal static string ParseReason(IDictionary<string, object?>? headers)
    {
        if (headers is null)
            return "unknown";

        if (!headers.TryGetValue("x-death", out var xDeath))
            return "unknown";

        if (xDeath is IList<object> list && list.Count > 0
            && list[0] is IDictionary<string, object?> entry
            && entry.TryGetValue("reason", out var reason)
            && reason is string reasonStr)
        {
            return reasonStr;
        }

        return "unknown";
    }

    /// <summary>
    /// Zwraca nazwę exchange źródłowego (skąd wiadomość trafiła do DLQ).
    /// </summary>
    internal static string ParseSourceExchange(IDictionary<string, object?>? headers)
    {
        if (headers is null)
            return "unknown";

        if (!headers.TryGetValue("x-death", out var xDeath))
            return "unknown";

        if (xDeath is IList<object> list && list.Count > 0
            && list[0] is IDictionary<string, object?> entry
            && entry.TryGetValue("exchange", out var exchange)
            && exchange is string exchangeStr)
        {
            return exchangeStr;
        }

        return "unknown";
    }

    /// <summary>
    /// Zwraca liczbę dotychczasowych prób przetworzenia wiadomości (z nagłówka x-hobiary-retry-count).
    /// </summary>
    internal static int ParseRetryCount(IDictionary<string, object?>? headers)
    {
        if (headers is null) return 0;
        if (!headers.TryGetValue("x-hobiary-retry-count", out var val)) return 0;
        return val switch
        {
            int i    => i,
            long l   => (int)l,
            byte[] b when b.Length >= 4 => BitConverter.ToInt32(b),
            string s when int.TryParse(s, out var parsed) => parsed,
            _ => 0,
        };
    }
}
