# REST API

Ta strona zbiera kontrakt HTTP adaptera w jednym miejscu, tak żeby nie szukać go po `README` i testach integracyjnych.

## Założenia kontraktu

- API jest wystawiane przez Adapter na porcie skonfigurowanym w `appsettings.json`.
- Opisany kontrakt traktujemy jako `v1`.
- Rozszerzenia powinny być kompatybilne wstecz.
- Gdy ustawisz `HobiaryLogPipe.HttpApiKey`, endpointy chronione wymagają `X-Api-Key`, `Authorization: ApiKey <key>`, `Authorization: Bearer <key>` albo `?apiKey=`.

## Endpointy

| Endpoint | Opis |
|---|---|
| `GET /health` | Health check zależności Adaptera |
| `GET /dashboard` | Wbudowany dashboard HTML |
| `GET /api/plugins` | Lista pluginów widocznych dla Adaptera |
| `GET /api/plugins/{pluginId}` | Szczegóły rejestracji pluginu |
| `GET /api/plugins/{pluginId}/stats` | Statystyki logów per poziom |
| `GET /api/metrics` | Liczniki techniczne i operacyjne |
| `GET /api/logs` | Odczyt logów z filtrami |
| `GET /api/plugins/{pluginId}/logs/export` | Eksport NDJSON |
| `GET /api/logs/stream` | SSE dla wszystkich pluginów |
| `GET /api/logs/stream/{pluginId}` | SSE dla pojedynczego pluginu |

## `GET /api/logs`

### Parametry

| Parametr | Wymagany | Opis |
|---|---|---|
| `pluginId` | tak | Identyfikator pluginu |
| `from` | nie | Początek zakresu czasu, ISO 8601 |
| `to` | nie | Koniec zakresu czasu, ISO 8601 |
| `level` | nie | Poziom logu: `Trace` do `Fatal` |
| `source` | nie | Filtruj po polu `source` (case-insensitive) |
| `service` | nie | Filtruj po polu `service` (case-insensitive) |
| `traceId` | nie | Filtruj po identyfikatorze śledzenia (case-insensitive, hex UUID) |
| `limit` | nie | `1..1000`, domyślnie `100` |
| `paged` | nie | Włącza odpowiedź kopertową z paginacją |
| `cursor` | nie | Kursor oparty o `timestampUs` |
| `offset` | nie | Parametr legacy, utrzymany dla kompatybilności |

### Przykład

```http
GET /api/logs?pluginId=orders-service&paged=true&cursor=1714555200000000&limit=100
```

### Odpowiedź paginowana

```json
{
  "items": [
    {
      "timestampUs": 1714555200000000,
      "level": "Information",
      "message": "Order accepted",
      "source": "OrdersController",
      "service": "orders-service",
      "traceId": "4c6f5a1d-8f1e-45d9-8c8f-7f5bc7e58dd4",
      "correlationId": "f6b8e3d7-4a07-4635-917f-7c08f3c2d88a"
    }
  ],
  "cursor": 1714555200000000,
  "limit": 100,
  "nextCursor": 1714555200999999,
  "hasMore": true
}
```

`cursor` oznacza ostatni `timestampUs` z poprzedniej strony. Nowe integracje powinny używać właśnie tego mechanizmu.

## `GET /api/plugins/{pluginId}/logs/export`

Eksport zwraca NDJSON i może być wykonywany porcjami.

### Parametry

| Parametr | Wymagany | Opis |
|---|---|---|
| `cursor` | nie | Kursor oparty o `timestampUs` |
| `limit` | nie | `1..10000`, domyślnie `1000` |
| `source` | nie | Filtruj po polu `source` (case-insensitive) |
| `service` | nie | Filtruj po polu `service` (case-insensitive) |

### Przykład

```http
GET /api/plugins/orders-service/logs/export?cursor=1714555200000000&limit=100&source=OrdersController
```

### Nagłówki odpowiedzi

| Nagłówek | Opis |
|---|---|
| `X-Export-Limit` | żądany limit |
| `X-Export-Count` | liczba rekordów w odpowiedzi |
| `X-Has-More` | czy są dalsze rekordy |
| `X-Next-Cursor` | następny kursor, gdy istnieje |
| `X-Cursor-Applied` | potwierdzenie użytego kursora |
| `X-Offset-Applied` | parametr legacy dla starszych klientów |

## `GET /api/plugins/{pluginId}/health`

Zwraca aktualny stan zdrowia pluginu: status online, czas ostatniego heartbeatu i liczbę rekordów.

### Odpowiedź (200 OK)

```json
{
  "pluginId": "orders-service",
  "isOnline": true,
  "isDeregistered": false,
  "lastHeartbeatAt": "2024-05-01T12:00:00Z",
  "heartbeatAgeSec": 3.5,
  "heartbeatTimeoutSec": 30,
  "recordCount": 12345,
  "checkedAt": "2024-05-01T12:00:03.5Z"
}
```

`isOnline` jest `null`, gdy `HeartbeatTimeoutSeconds = 0` (monitorowanie wyłączone).
`heartbeatTimeoutSec` jest `null`, gdy timeout nie jest skonfigurowany.

Gdy plugin nie istnieje, zwraca `404` z kodem błędu `plugin_not_found`.

## Format błędów

Odpowiedzi `400`, `401` i `404` mają wspólny kontrakt:

```json
{
  "code": "invalid_plugin_id",
  "error": "Plugin ID is invalid.",
  "details": {
    "pluginId": "orders/service"
  }
}
```

Pole `details` jest opcjonalne i służy do szybszej diagnozy błędnych wejść.

## Uwierzytelnianie HTTP API

Jeśli `HobiaryLogPipe.HttpApiKey` jest ustawione, możesz autoryzować żądania na cztery sposoby:

- nagłówkiem `X-Api-Key: <key>`,
- nagłówkiem `Authorization: ApiKey <key>`,
- nagłówkiem `Authorization: Bearer <key>` (dla standardowych bibliotek HTTP),
- parametrem query `?apiKey=<key>`.

W nowych integracjach serwer-serwer preferuj nagłówek `Authorization: Bearer`.

## Dashboard i SSE

`GET /dashboard` nie ma osobnego backendu BFF. Korzysta bezpośrednio z:

- `/api/plugins`
- `/api/plugins/{pluginId}/stats`
- `/api/logs`
- `/api/plugins/{pluginId}/logs/export`
- `/api/logs/stream`

To upraszcza diagnostykę: każde zachowanie dashboardu można odtworzyć ręcznie zwykłym klientem HTTP.

## Testowanie kontraktu

Najlepszym punktem odniesienia dla zachowania endpointów są testy integracyjne `TestServer` w projekcie `tests/HobiaryLogPipe.AspNetCore.Tests`.