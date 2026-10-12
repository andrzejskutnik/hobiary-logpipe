# Changelog

Wszystkie zmiany projektu są dokumentowane w tym pliku.
Format: [Keep a Changelog](https://keepachangelog.com/en/1.1.0/), wersjonowanie: [SemVer](https://semver.org/).

## [Unreleased]

### Dodano

- **#20 HTTP access control**
  - opcjonalny `HobiaryLogPipe.HttpApiKey` chroni `/dashboard`, `/api/*`, `/metrics` i `/health`
  - uwierzytelnienie przez nagłówek `X-Api-Key` lub query string `?apiKey=...`
  - dodane testy HTTP na `TestServer` weryfikujące ścieżki `401` / `200`

- **#19 Query pagination**
  - `GET /api/logs` obsługuje `paged=true` oraz `cursor` dla odpowiedzi stronicowanej
  - dashboard używa paginacji do przechodzenia między stronami historii logów
  - `GET /api/plugins/{pluginId}/logs/export` obsługuje `cursor` i `limit` do chunkowanego eksportu NDJSON
  - odpowiedzi chunkowanego eksportu zwracają nagłówki `X-Export-Limit`, `X-Export-Count`, `X-Has-More` i `X-Next-Cursor`

- **#18 Dashboard hardening**
  - dashboard obsługuje zakres czasu `from` / `to` dla statystyk i zapytań historycznych
  - przycisk eksportu NDJSON bezpośrednio z UI (`/api/plugins/{pluginId}/logs/export`)
  - widoczny licznik reconnectów SSE oraz czytelniejsze stany połączenia live feedu

- **#17 Embedded dashboard HTML**
  - `GET /dashboard` — wbudowany dashboard HTML bez osobnego frontendu
  - widoki: lista pluginów, szczegóły pluginu, statystyki poziomów, historia logów, live feed SSE
  - dashboard korzysta bezpośrednio z istniejącego API adaptera

- **#16 TraceId / CorrelationId propagation**
  - `LogMessage.TraceId` i `LogMessage.CorrelationId` jako pola `string` w formacie 32-znakowego hex
  - `InputPlugin` automatycznie uzupełnia `TraceId` z `Activity.Current` oraz `CorrelationId` z baggage
  - `Adapter` mapuje oba pola do `HlogRecord`, zapisuje je do HobiaryLogDB i zwraca w `GET /api/logs` oraz eksporcie NDJSON

- **#15 NDJSON export**
  - `GET /api/plugins/{pluginId}/logs/export` — eksport logów jako `application/x-ndjson`
  - każdy rekord zachowuje pola `level`, `timestampUs`, `source`, `service`, `payload`, `traceId`, `correlationId`

- **#14 Log statistics endpoint**
  - `GET /api/plugins/{pluginId}/stats` — sumaryczna liczba logów i rozkład per `HlogLevel`
  - zakres czasu przez `from` / `to` w ISO 8601

- **#13 Plugin deregistration**
  - `PluginRegistration.IsDeregistered` oraz obsługa wyrejestrowania pluginu po stronie Adaptera
  - API pluginów zwraca stan deregistration zamiast ukrywać wpis z registry

- **#12 Health check HobiaryLogDB**
  - `GET /health` rozszerzony o sprawdzenie dostępności klientów HobiaryLogDB
  - status `Degraded` dla częściowo niedostępnych rejestracji

- **#11 Heartbeat metrics endpoint**
  - `/metrics` eksportuje heartbeat counters i bieżącą liczbę pluginów online w formacie Prometheus

- **#10 Heartbeat / dead-plugin detection**
  - `InputPlugin`: background loop wysyłający `{"type":"heartbeat"}` co `HeartbeatIntervalSeconds` sekund (domyślnie 30)
  - `Adapter`: wykrywanie wiadomości heartbeat przed przetwarzaniem — `TouchHeartbeat()` + ACK bez zapisu do hlog
  - `HeartbeatMonitor` (`BackgroundService`) — co 10 s sprawdza wszystkie pluginy; przy zmianie stanu emituje SSE `plugin_online`/`plugin_offline`
  - `AdapterOptions.HeartbeatTimeoutSeconds` (domyślnie 90 s; `0` = wyłączone)
  - `InputPluginOptions.HeartbeatIntervalSeconds` (domyślnie 30 s)
  - Endpoint `GET /api/plugins/{pluginId}` rozszerzony o pola `lastHeartbeatAt` i `isOnline`

- **#9 Log Query REST API**
  - `GET /api/plugins` — lista zarejestrowanych pluginów z licznikami rekordów
  - `GET /api/plugins/{pluginId}` — szczegóły pluginu (format, mapping, statystyki, heartbeat)
  - `GET /api/metrics` — globalne liczniki: łączne logi, błędy, odrzucone, bieżące pluginy
  - `GET /api/logs` — odczyt logów per plugin: filtrowanie po przedziale czasu (`from`/`to` ISO 8601), poziomie (`level`) i limicie (1–1000); P/Invoke `hlog_visit_range` / `hlog_visit_level`

- **#8 SSE live streaming**
  - `GET /api/logs/stream` — strumień `text/event-stream` wszystkich zdarzeń logów w czasie rzeczywistym
  - `GET /api/logs/stream/{pluginId}` — strumień filtrowany per plugin
  - `SseHub` — wewnętrzny hub z `BoundedChannel(256, DropOldest)` per subskrybent

- **#7 Gzip compression**
  - `InputPluginOptions.GzipEnabled` — kompresja payloadu po stronie InputPlugin (nagłówek `x-hobiary-encoding: gzip`)
  - Adapter transparentnie dekompresuje przed przetwarzaniem

- **#6 Schema validation**
  - `AdapterOptions.PluginRequiredFields` — słownik wymaganych pól JSON per plugin
  - Brak wymaganego pola → NACK (wiadomość trafia do dead-letter)

- **#5 TLS i szyfrowanie E2E**
  - `RabbitMQ.Tls` — opcjonalne TLS/mTLS dla połączenia AMQP (`.pfx`; `AcceptInvalidCertificates` dla dev)
  - `HobiaryLogPipe.PayloadEncryption` — szyfrowanie AES-256-GCM payloadu; klucz 32B Base64; obie strony muszą mieć tę samą konfigurację; mismatch → NACK bez requeueing

- **#4 Rate limiting per plugin**
  - `AdapterOptions.PluginRateLimits` — maks. wiadomości/s per plugin (token bucket)
  - Przekroczenie → NACK bez requeueing (logi wracają do kolejki, ale nie są akceptowane)

- **#3 Min level per plugin**
  - `AdapterOptions.PluginMinLevels` — minimalny poziom logu per plugin (`Trace`–`Fatal`)
  - Logi poniżej progu → NACK (wiadomość trafia do dead-letter)

- **#2 DLQ retries**
  - `AdapterOptions.MaxDlqRetries` (domyślnie 3) — po przekroczeniu maksymalnej liczby prób NACK wiadomość trafia do dead-letter zamiast wracać do kolejki

## [0.1.0] - 2026-04-27

### Dodano

- `HobiaryLogPipe.Contracts` — typy współdzielone: `AnnounceMessage`, `LogMessage`, `FieldMappingConfig`, `LogPipeLevel`
- `HobiaryLogPipe.Adapter` — Worker Service konsumujący RabbitMQ i zapisujący logi do HobiaryLogDB przez P/Invoke
  - `AdapterService` — dwukanałowy konsument: `hobiary.announce` (fanout) + `hobiary.data` (direct)
  - `PluginRegistry` — rejestr zarejestrowanych pluginów (`ConcurrentDictionary`)
  - `PluginIdValidator` — walidacja `pluginId` i kluczy `fieldMapping` regexem `[a-zA-Z0-9_.-]{1,64}`
  - `FieldMapper` — mapowanie JSON na `HlogRecord` z obsługą 10 aliasów poziomów (case-insensitive)
  - `HobiaryLogClient` / `NativeHLog` — zarządzany wrapper P/Invoke nad `hobiarylogdb.dll`
  - Reconnect loop z `Interlocked` — automatyczne wznawianie połączenia po utracie z RabbitMQ
  - Model bezpieczeństwa: allowlist pluginId, adapter decyduje o ścieżce logów, limit payload 64 KB
- `HobiaryLogPipe.InputPlugin` — biblioteka do osadzenia w aplikacji klienckiej
  - `LogPipeWriter` — `ILogPipeWriter + IHostedService`: ogłasza się na `hobiary.announce`, publikuje logi na `hobiary.data`
  - Reconnect loop — automatyczne wznawianie połączenia; konfigurowane przez `ReconnectDelaySeconds`
  - `ServiceCollectionExtensions.AddHobiaryLogPipe(opts)` — rejestracja w DI jedną linią
- `HobiaryLogPipe.Serilog` — sink dla Serilog
  - `HobiaryLogPipeSink` — `ILogEventSink`: mapuje `LogEventLevel` → `LogPipeLevel`, wyciąga `SourceContext`
  - `WriteTo.HobiaryLogPipe(opts)` — extension method dla fluent API Serilog
- `HobiaryLogPipe.MicrosoftLogging` — `ILogger` provider wysyłający logi przez InputPlugin
- `HobiaryLogPipe.OpenTelemetry` — OpenTelemetry `LogExporter` przez InputPlugin
- Testy xUnit — 72 testy (Adapter: 53, Serilog: 13, InputPlugin: 6), wszystkie przechodzą
