# HobiaryLogPipe — Przewodnik Użytkownika

## Co to jest HobiaryLogPipe?

HobiaryLogPipe to pipeline logów, który może działać w dwóch wariantach transportu:

- **RabbitMQ** — `InputPlugin` wysyła logi przez RabbitMQ, a `Adapter` zapisuje je do HobiaryLogDB
- **Direct** — `InputPlugin` zapisuje logi bezpośrednio do HobiaryLogDB, bez brokera i bez adaptera

```
Twoja aplikacja
  + InputPlugin  ──► RabbitMQ ──► Adapter ──► pliki .hlog
```

``` 
Twoja aplikacja
  + InputPlugin  ──► pliki .hlog
```

Każda aplikacja rejestruje się przy starcie, podając swój identyfikator i format wiadomości. Adapter dostosowuje się automatycznie.

---

## Wymagania

| Komponent | Wymaganie |
|---|---|
| .NET | 10.0+ |
| RabbitMQ | 3.12+ (AMQP 0.9.1), tylko dla wariantu `RabbitMq` |
| HobiaryLogDB | `hobiarylogdb.dll` / `libhobiarylogdb.so` |
| System operacyjny | Windows lub Linux |

Jeżeli budujesz HobiaryLogDB lokalnie, wynik znajdziesz w repo `hobiary-logdb` jako `build/hobiarylogdb.dll` (Windows) albo `build/libhobiarylogdb.so` (Linux).

---

## Konfiguracja adaptera

Skopiuj `config/appsettings.example.json` jako `appsettings.json` w katalogu adaptera i wypełnij wartości:

### `HobiaryLogPipe`

```json
"HobiaryLogPipe": {
  "BaseLogDirectory": "C:/logs/hobiary",
  "AllowedPlugins": ["orders-service", "api-gateway"],
  "UnknownPluginAction": "deadletter",
  "HttpApiKey": "",
  "MaxDlqRetries": 3,
  "HeartbeatTimeoutSeconds": 90,
  "PluginMinLevels": {
    "orders-service": "Warning"
  },
  "PluginRateLimits": {
    "orders-service": 100
  },
  "PluginRequiredFields": {
    "orders-service": ["level", "orderId"]
  },
  "PayloadEncryption": {
    "Enabled": false,
    "KeyBase64": ""
  }
}
```

| Pole | Opis |
|---|---|
| `BaseLogDirectory` | Katalog bazowy — adapter tworzy podkatalog per plugin: `{BaseLogDirectory}/{pluginId}/` |
| `AllowedPlugins` | Lista `pluginId` dopuszczonych do rejestracji; inne są odrzucane |
| `UnknownPluginAction` | Co zrobić z nieznanym pluginem: `deadletter` lub `drop` |
| `HttpApiKey` | Opcjonalny klucz do ochrony HTTP endpointów adaptera (`/dashboard`, `/api/*`, `/metrics`, `/health`) |
| `MaxDlqRetries` | Maks. liczba prób NACK przed dead-letter (domyślnie `3`) |
| `HeartbeatTimeoutSeconds` | Po ilu sekundach bez heartbeatu plugin uznawany za offline (`0` = wyłączone; domyślnie `90`) |
| `PluginMinLevels` | Minimalny poziom logu per plugin, np. `"orders-service": "Warning"` |
| `PluginRateLimits` | Maks. wiadomości/s per plugin; przekroczenie → NACK |
| `PluginRequiredFields` | Wymagane pola JSON per plugin; brak pola → NACK |
| `PayloadEncryption.Enabled` | Szyfrowanie AES-256-GCM; obie strony muszą mieć ten sam klucz |

### `RabbitMQ`

```json
"RabbitMQ": {
  "Host": "localhost",
  "Port": 5672,
  "VirtualHost": "/hobiary",
  "Username": "hobiary-adapter",
  "Password": "...",
  "AnnounceExchange": "hobiary.announce",
  "DataExchange": "hobiary.data",
  "DeadLetterQueue": "hobiary.unknown",
  "PrefetchCount": 50,
  "ReconnectDelaySeconds": 5
}
```

| Pole | Opis |
|---|---|
| `VirtualHost` | Dedykowany VHost dla HobiaryLogPipe — izolacja od innych kolejek |
| `Username` | User adaptera — powinien mieć tylko uprawnienia `consume` na swoich kolejkach |
| `PrefetchCount` | Ile wiadomości adapter pobiera naraz; 50 to dobry punkt startowy |
| `ReconnectDelaySeconds` | Czas oczekiwania między próbami ponownego połączenia |

---

## Konfiguracja RabbitMQ — krok po kroku

### 1. Utwórz dedykowany VHost

```bash
rabbitmqctl add_vhost /hobiary
```

### 2. Utwórz usera dla adaptera

```bash
rabbitmqctl add_user hobiary-adapter SILNE_HASŁO
rabbitmqctl set_permissions -p /hobiary hobiary-adapter "" "hobiary\.*" "hobiary\.*"
```

Adapter potrzebuje tylko `read` (consume) na exchange'ach danych.

### 3. Utwórz usera dla każdego pluginu

```bash
# Przykład dla orders-service
rabbitmqctl add_user plugin-orders INNE_SILNE_HASŁO
rabbitmqctl set_permissions -p /hobiary plugin-orders "" "hobiary\.(announce|data)" ""
```

Plugin potrzebuje tylko `write` (publish) — nie może czytać logów innych pluginów.

### 4. Dodaj pluginId do allowlisty

W `appsettings.json` adaptera dopisz `"orders-service"` do tablicy `AllowedPlugins`.

---

## Dodanie InputPlugin do aplikacji

### 1. Dodaj referencję do projektu

```xml
<ProjectReference Include="../../hobiary-logpipe/src/HobiaryLogPipe.InputPlugin/HobiaryLogPipe.InputPlugin.csproj" />
```

Lub (gdy będzie pakiet NuGet):

```xml
<PackageReference Include="HobiaryLogPipe.InputPlugin" Version="1.*" />
```

### 2. Zarejestruj w DI (ASP.NET Core / Generic Host)

```csharp
builder.Services.AddHobiaryLogPipe(options =>
{
    options.Transport   = LogPipeTransport.RabbitMq;
    options.PluginId    = "orders-service";
    options.Host        = "localhost";
    options.Port        = 5672;
    options.VirtualHost = "/hobiary";
    options.Username    = "plugin-orders";
    options.Password    = Environment.GetEnvironmentVariable("RABBITMQ_PASSWORD")!;
    options.FieldMapping = new()
    {
        Level   = "severity",   // nazwa pola "level" w Twoim JSON
        Message = "body",       // nazwa pola "message"
        Source  = "serviceName" // opcjonalne
    };
});
```

Wariant bez brokera:

```csharp
builder.Services.AddHobiaryLogPipe(options =>
{
    options.Transport = LogPipeTransport.Direct;
    options.PluginId = "orders-service";
    options.BaseLogDirectory = "C:/logs/hobiary-direct";
});
```

W trybie `Direct` finalna ścieżka zapisu to `{BaseLogDirectory}/{PluginId}/`.

### 2a. Brokerless Quick Start

Jeżeli chcesz od razu zobaczyć wariant bez RabbitMQ i bez adaptera, wykonaj ten skrócony przebieg:

1. Upewnij się, że `hobiarylogdb.dll` albo `libhobiarylogdb.so` jest dostępne w output projektu .NET.
2. Uruchom launcher demo:

```powershell
pwsh -File .\tools\start-brokerless-demos.ps1 -StopWebOnExit
```

3. Otwórz w przeglądarce widok HTML ostatnich logów:

```text
http://localhost:5088/logs/view?limit=20&minutes=30
```

4. Sprawdź, że po przebiegu powstały katalogi z logami:

```text
samples/
  BrokerlessDemo/
    demo-logs/
      brokerless-demo/
  BrokerlessWebDemo/
    demo-logs/
      brokerless-web-demo/
```

Launcher czyści przed startem tylko katalogi `demo-logs` dla użytych pluginów, wykonuje przykładowe requesty, pokazuje demo-focused readback z `.hlog` i automatycznie zamyka web demo, gdy używasz `-StopWebOnExit`.

Przykładowy oczekiwany output launchera:

```text
BrokerlessWebDemo is running under PID 40696.
Invoking sample HTTP requests...
GET /fail -> HTTP 500
Running console brokerless demo...
Read back 8 record(s) from '.\samples\BrokerlessDemo\demo-logs\brokerless-demo'. Displaying 4 demo-focused record(s):
- [Info] source=BrokerlessDemoWorker service=<null>
  message=Brokerless demo started for brokerless-demo
- [Warn] source=BrokerlessDemoWorker service=<null>
  message=This warning is written through Microsoft.Extensions.Logging provider.
- [Info] source=BrokerlessDemoWorker service=brokerless-demo
  message=Direct write through ILogPipeWriter
- [Error] source=BrokerlessDemoWorker service=brokerless-demo
  message=Simulated business error stored without broker
HTML view: http://localhost:5088/logs/view?limit=20&minutes=30
BrokerlessWebDemo stopped automatically.
```

Jeżeli chcesz gotowe demo tego wariantu, uruchom:

```powershell
$env:HOBIARY_PLUGIN_ID = 'brokerless-demo'
$env:HOBIARY_DIRECT_LOG_DIR = '.\samples\BrokerlessDemo\demo-logs'
dotnet run --project .\samples\BrokerlessDemo\BrokerlessDemo.csproj
```

Sample zapisuje logi bez brokera i po zakończeniu od razu odczytuje je z `.hlog`, żeby pokazać wynik na konsoli.

Jeżeli chcesz lokalnie obejrzeć albo wyeksportować rekordy bez uruchamiania web demo, użyj `HlogInspect`:

```powershell
dotnet run --project .\samples\HlogInspect\HlogInspect.csproj -- \
  --base-dir .\samples\BrokerlessDemo\demo-logs \
  --plugin-id brokerless-demo \
  --minutes 30 \
  --limit 20 \
  --format table
```

Filtrowanie po `source` albo `service`:

```powershell
dotnet run --project .\samples\HlogInspect\HlogInspect.csproj -- \
  --base-dir .\samples\BrokerlessDemo\demo-logs \
  --plugin-id brokerless-demo \
  --source OrdersService \
  --service api-gateway \
  --format json
```

Filtrowanie po `traceId`:

```powershell
dotnet run --project .\samples\HlogInspect\HlogInspect.csproj -- \
  --base-dir .\samples\BrokerlessDemo\demo-logs \
  --plugin-id brokerless-demo \
  --traceId 0123456789abcdef0123456789abcdef \
  --format json
```

Jeżeli analizujesz kilka śladów naraz, `--traceId` przyjmuje też listę rozdzieloną przecinkami:

```powershell
dotnet run --project .\samples\HlogInspect\HlogInspect.csproj -- \
  --base-dir .\samples\BrokerlessDemo\demo-logs \
  --plugin-id brokerless-demo \
  --traceId 0123456789abcdef0123456789abcdef,fedcba9876543210fedcba9876543210 \
  --format json
```

`--traceId` akceptuje wyłącznie tokeny w formacie 32-znakowego hex. Błędny token kończy się błędem parsera przed uruchomieniem zapytania.

Jeżeli wolisz bardziej shellowy zapis flag, możesz użyć aliasu `--trace-id`.

Filtrowanie po `correlationId`:

```powershell
dotnet run --project .\samples\HlogInspect\HlogInspect.csproj -- \
  --base-dir .\samples\BrokerlessDemo\demo-logs \
  --plugin-id brokerless-demo \
  --correlationId aabbccddeeff00112233445566778899 \
  --format json
```

Tak samo `--correlationId` może zawierać kilka identyfikatorów oddzielonych przecinkami, gdy chcesz złożyć widok dla wielu powiązanych przepływów:

```powershell
dotnet run --project .\samples\HlogInspect\HlogInspect.csproj -- \
  --base-dir .\samples\BrokerlessDemo\demo-logs \
  --plugin-id brokerless-demo \
  --correlationId aabbccddeeff00112233445566778899,99887766554433221100ffeeddccbbaa \
  --format json
```

`--correlationId` akceptuje 32-znakowy hex albo GUID. Jeżeli podasz GUID, CLI znormalizuje go do formatu bez myślników zanim zastosuje filtr.

CLI akceptuje też alias `--correlation-id`, równoważny `--correlationId`.

Filtrowanie po fragmencie `message` albo surowego `payload`:

```powershell
dotnet run --project .\samples\HlogInspect\HlogInspect.csproj -- \
  --base-dir .\samples\BrokerlessDemo\demo-logs \
  --plugin-id brokerless-demo \
  --contains timeout \
  --format json
```

Domyślnie `--contains` sprawdza jednocześnie `message` i `payloadJson`. Jeżeli chcesz ograniczyć wyszukiwanie do jednego pola, dodaj `--contains-field any|message|payload`:

```powershell
dotnet run --project .\samples\HlogInspect\HlogInspect.csproj -- \
  --base-dir .\samples\BrokerlessDemo\demo-logs \
  --plugin-id brokerless-demo \
  --contains timeout \
  --contains-field payload \
  --format json
```

Jeżeli `--contains` ma przyjąć kilka tokenów rozdzielonych przecinkami, możesz sterować semantyką dopasowania przez `--contains-mode any|all`. `any` zwraca rekord, gdy pasuje dowolny token, a `all` wymaga dopasowania wszystkich tokenów w wybranym polu lub polach:

```powershell
dotnet run --project .\samples\HlogInspect\HlogInspect.csproj -- \
  --base-dir .\samples\BrokerlessDemo\demo-logs \
  --plugin-id brokerless-demo \
  --contains timeout,orders \
  --contains-field message \
  --contains-mode all \
  --format json
```

Jeżeli chcesz zawęzić wynik do kilku konkretnych producerów albo kanałów wykonania, `--source` i `--service` przyjmują listy rozdzielone przecinkami i dopasowują rekord, gdy wartość pola pasuje do któregokolwiek tokenu:

```powershell
dotnet run --project .\samples\HlogInspect\HlogInspect.csproj -- \
  --base-dir .\samples\BrokerlessDemo\demo-logs \
  --plugin-id brokerless-demo \
  --source OrdersService,BillingService \
  --service api-gateway,worker \
  --format json
```

`--level` nadal działa jako filtr minimalnego poziomu (`>=`), a gdy potrzebujesz dokładnego zestawu poziomów po stronie CLI, użyj `--level-in`:

```powershell
dotnet run --project .\samples\HlogInspect\HlogInspect.csproj -- \
  --base-dir .\samples\BrokerlessDemo\demo-logs \
  --plugin-id brokerless-demo \
  --level-in Info,Error \
  --format json
```

Sortowanie od najnowszych rekordów:

```powershell
dotnet run --project .\samples\HlogInspect\HlogInspect.csproj -- \
  --base-dir .\samples\BrokerlessDemo\demo-logs \
  --plugin-id brokerless-demo \
  --sort desc \
  --format table
```

Prosta paginacja przez `offset`:

```powershell
dotnet run --project .\samples\HlogInspect\HlogInspect.csproj -- \
  --base-dir .\samples\BrokerlessDemo\demo-logs \
  --plugin-id brokerless-demo \
  --sort desc \
  --offset 20 \
  --limit 20 \
  --format table
```

Nawigacja inkrementalna po czasie przez `after-timestamp`:

```powershell
dotnet run --project .\samples\HlogInspect\HlogInspect.csproj -- \
  --base-dir .\samples\BrokerlessDemo\demo-logs \
  --plugin-id brokerless-demo \
  --after-timestamp 2026-09-24T12:30:00Z \
  --sort asc \
  --format json
```

Zawężenie górnej granicy czasu przez `before-timestamp`:

```powershell
dotnet run --project .\samples\HlogInspect\HlogInspect.csproj -- \
  --base-dir .\samples\BrokerlessDemo\demo-logs \
  --plugin-id brokerless-demo \
  --before-timestamp 2026-09-24T12:45:00Z \
  --sort asc \
  --format json
```

Metadane strony dla skryptów przez `page-meta`:

```powershell
dotnet run --project .\samples\HlogInspect\HlogInspect.csproj -- \
  --base-dir .\samples\BrokerlessDemo\demo-logs \
  --plugin-id brokerless-demo \
  --sort asc \
  --offset 20 \
  --limit 20 \
  --page-meta \
  --format json
```

Lekki summary bez rekordów przez `counts-only`:

```powershell
dotnet run --project .\samples\HlogInspect\HlogInspect.csproj -- \
  --base-dir .\samples\BrokerlessDemo\demo-logs \
  --plugin-id brokerless-demo \
  --sort asc \
  --offset 20 \
  --limit 20 \
  --counts-only \
  --format json
```

`page-meta` działa tylko z `--format json`.

Projekcja wybranych pól przez `fields`:

```powershell
dotnet run --project .\samples\HlogInspect\HlogInspect.csproj -- \
  --base-dir .\samples\BrokerlessDemo\demo-logs \
  --plugin-id brokerless-demo \
  --fields timestamp,level,message \
  --format json
```

Preset pól `tracing`:

```powershell
dotnet run --project .\samples\HlogInspect\HlogInspect.csproj -- \
  --base-dir .\samples\BrokerlessDemo\demo-logs \
  --plugin-id brokerless-demo \
  --fields tracing \
  --format json
```

Jeżeli wolisz shellowy zapis nazw pól, `--fields` i `--summary-record-fields` akceptują też aliasy `trace-id`, `correlation-id` oraz `payload-json`.

Preset pól `errors`:

```powershell
dotnet run --project .\samples\HlogInspect\HlogInspect.csproj -- \
  --base-dir .\samples\BrokerlessDemo\demo-logs \
  --plugin-id brokerless-demo \
  --fields errors \
  --format json
```

Preset pól `security`:

```powershell
dotnet run --project .\samples\HlogInspect\HlogInspect.csproj -- \
  --base-dir .\samples\BrokerlessDemo\demo-logs \
  --plugin-id brokerless-demo \
  --fields security \
  --format json
```

Preset pól `audit`:

```powershell
dotnet run --project .\samples\HlogInspect\HlogInspect.csproj -- \
  --base-dir .\samples\BrokerlessDemo\demo-logs \
  --plugin-id brokerless-demo \
  --fields audit \
  --format csv
```

Preset pól `forensics`:

```powershell
dotnet run --project .\samples\HlogInspect\HlogInspect.csproj -- \
  --base-dir .\samples\BrokerlessDemo\demo-logs \
  --plugin-id brokerless-demo \
  --fields forensics \
  --format ndjson
```

Preset pól `triage`:

```powershell
dotnet run --project .\samples\HlogInspect\HlogInspect.csproj -- \
  --base-dir .\samples\BrokerlessDemo\demo-logs \
  --plugin-id brokerless-demo \
  --fields triage \
  --format json
```

Preset pól `ops`:

```powershell
dotnet run --project .\samples\HlogInspect\HlogInspect.csproj -- \
  --base-dir .\samples\BrokerlessDemo\demo-logs \
  --plugin-id brokerless-demo \
  --fields ops \
  --format csv
```

Eksport CSV:

```powershell
dotnet run --project .\samples\HlogInspect\HlogInspect.csproj -- \
  --base-dir .\samples\BrokerlessDemo\demo-logs \
  --plugin-id brokerless-demo \
  --fields timestamp,level,message \
  --format csv
```

`fields` działa tylko z `--format json`, `--format ndjson` albo `--format csv`.

Zapis wyniku do pliku przez `output`:

```powershell
dotnet run --project .\samples\HlogInspect\HlogInspect.csproj -- \
  --base-dir .\samples\BrokerlessDemo\demo-logs \
  --plugin-id brokerless-demo \
  --format csv \
  --fields core \
  --output .\exports\brokerless-demo.csv
```

Tryb dopisywania do istniejącego pliku przez `append-output`:

```powershell
dotnet run --project .\samples\HlogInspect\HlogInspect.csproj -- \
  --base-dir .\samples\BrokerlessDemo\demo-logs \
  --plugin-id brokerless-demo \
  --format csv \
  --fields ops \
  --output .\exports\brokerless-demo.csv \
  --append-output
```

Dzielenie eksportu na wiele plików przez `split-output-size`:

```powershell
dotnet run --project .\samples\HlogInspect\HlogInspect.csproj -- \
  --base-dir .\samples\BrokerlessDemo\demo-logs \
  --plugin-id brokerless-demo \
  --format csv \
  --fields audit \
  --output .\exports\brokerless-demo.csv \
  --split-output-size 1048576
```

`split-output-size` działa tylko z `--format csv` albo `--format ndjson` i nie łączy się z `append-output`.

Jeżeli chcesz dodatkowy indeks artefaktu eksportu, dodaj `--manifest-output`:

```powershell
dotnet run --project .\samples\HlogInspect\HlogInspect.csproj -- \
  --base-dir .\samples\BrokerlessDemo\demo-logs \
  --plugin-id brokerless-demo \
  --format ndjson \
  --fields triage \
  --output .\exports\brokerless-demo.ndjson \
  --split-output-size 1048576 \
  --manifest-output .\exports\brokerless-demo.manifest.json
```

`--manifest-output` działa też dla zwykłego, niesplitowanego `--output`, więc możesz zapisać osobny indeks także dla pojedynczego pliku:

```powershell
dotnet run --project .\samples\HlogInspect\HlogInspect.csproj -- \
  --base-dir .\samples\BrokerlessDemo\demo-logs \
  --plugin-id brokerless-demo \
  --format json \
  --output .\exports\brokerless-demo.json \
  --manifest-output .\exports\brokerless-demo.manifest.json
```

Jeżeli manifest ma zawierać też checksumę SHA-256 wygenerowanego pliku albo każdej części splitu, dodaj `--manifest-include-output-hash`. W manifeście JSON mapa hashy trafia do głównego pola `OutputHashesSha256`, a przy `--manifest-format ndjson` i `--manifest-include-totals` także do rekordu `totals`.

```powershell
dotnet run --project .\samples\HlogInspect\HlogInspect.csproj -- \
  --base-dir .\samples\BrokerlessDemo\demo-logs \
  --plugin-id brokerless-demo \
  --format ndjson \
  --fields triage \
  --output .\exports\brokerless-demo.ndjson \
  --manifest-output .\exports\brokerless-demo.manifest.json \
  --manifest-include-output-hash
```

Domyślnie manifest jest zapisywany jako jeden dokument JSON. Jeżeli wolisz jeden wpis na linię, użyj `--manifest-format ndjson`:

```powershell
dotnet run --project .\samples\HlogInspect\HlogInspect.csproj -- \
  --base-dir .\samples\BrokerlessDemo\demo-logs \
  --plugin-id brokerless-demo \
  --format ndjson \
  --output .\exports\brokerless-demo.ndjson \
  --split-output-size 1048576 \
  --manifest-output .\exports\brokerless-demo.manifest.ndjson \
  --manifest-format ndjson
```

Jeżeli chcesz dopiąć na końcu manifestu `ndjson` rekord zbiorczy, dodaj `--manifest-include-totals`:

```powershell
dotnet run --project .\samples\HlogInspect\HlogInspect.csproj -- \
  --base-dir .\samples\BrokerlessDemo\demo-logs \
  --plugin-id brokerless-demo \
  --format ndjson \
  --output .\exports\brokerless-demo.ndjson \
  --split-output-size 1048576 \
  --manifest-output .\exports\brokerless-demo.manifest.ndjson \
  --manifest-format ndjson \
  --manifest-include-totals
```

Jeżeli rekord `totals` ma zawierać też zakres eksportu i aktywne filtry, dodaj `--manifest-include-window`:

```powershell
dotnet run --project .\samples\HlogInspect\HlogInspect.csproj -- \
  --base-dir .\samples\BrokerlessDemo\demo-logs \
  --plugin-id brokerless-demo \
  --format ndjson \
  --source OrdersService \
  --after-timestamp 2026-09-24T12:30:00Z \
  --output .\exports\brokerless-demo.ndjson \
  --split-output-size 1048576 \
  --manifest-output .\exports\brokerless-demo.manifest.ndjson \
  --manifest-format ndjson \
  --manifest-include-totals \
  --manifest-include-window
```

Jeżeli chcesz dopiąć do `totals` również liczniki poziomów dla wyeksportowanej strony, dodaj `--manifest-include-level-counts`:

```powershell
dotnet run --project .\samples\HlogInspect\HlogInspect.csproj -- \
  --base-dir .\samples\BrokerlessDemo\demo-logs \
  --plugin-id brokerless-demo \
  --format ndjson \
  --output .\exports\brokerless-demo.ndjson \
  --split-output-size 1048576 \
  --manifest-output .\exports\brokerless-demo.manifest.ndjson \
  --manifest-format ndjson \
  --manifest-include-totals \
  --manifest-include-level-counts
```

Jeżeli chcesz osobno zobaczyć liczniki dla wszystkich rekordów dopasowanych do filtrów oraz dla finalnie zwróconej strony, dodaj `--manifest-include-match-counts`:

```powershell
dotnet run --project .\samples\HlogInspect\HlogInspect.csproj -- \
  --base-dir .\samples\BrokerlessDemo\demo-logs \
  --plugin-id brokerless-demo \
  --source OrdersService \
  --limit 20 \
  --format ndjson \
  --output .\exports\brokerless-demo.ndjson \
  --split-output-size 1048576 \
  --manifest-output .\exports\brokerless-demo.manifest.ndjson \
  --manifest-format ndjson \
  --manifest-include-totals \
  --manifest-include-match-counts
```

Jeżeli manifest ma zachować pełny ślad uruchomienia CLI, możesz dołączyć same filtry przez `--manifest-include-filters` albo całe polecenie przez `--manifest-include-command`. W manifeście JSON oba pola trafiają do głównego dokumentu, a przy `--manifest-format ndjson` są dopinane do rekordu `totals` i dlatego wymagają też `--manifest-include-totals`:

```powershell
dotnet run --project .\samples\HlogInspect\HlogInspect.csproj -- \
  --base-dir .\samples\BrokerlessDemo\demo-logs \
  --plugin-id brokerless-demo \
  --format ndjson \
  --contains timeout,orders \
  --contains-field message \
  --contains-mode all \
  --output .\exports\brokerless-demo.ndjson \
  --manifest-output .\exports\brokerless-demo.manifest.ndjson \
  --manifest-format ndjson \
  --manifest-include-totals \
  --manifest-include-filters \
  --manifest-include-command
```

Jeżeli chcesz sterować nazwami części, użyj `--split-output-template`. Dostępne tokeny: `{name}`, `{part}`, `{ext}`.

```powershell
dotnet run --project .\samples\HlogInspect\HlogInspect.csproj -- \
  --base-dir .\samples\BrokerlessDemo\demo-logs \
  --plugin-id brokerless-demo \
  --format ndjson \
  --fields triage \
  --output .\exports\brokerless-demo.ndjson \
  --split-output-size 1048576 \
  --split-output-template chunks\{name}-chunk-{part}{ext}
```

Domyślnie token `{part}` używa numeracji z paddingiem do 4 cyfr (`0001`, `0002`, ...). Jeżeli chcesz prostą numerację (`1`, `2`, ...), dodaj `--template-number-style plain`:

```powershell
dotnet run --project .\samples\HlogInspect\HlogInspect.csproj -- \
  --base-dir .\samples\BrokerlessDemo\demo-logs \
  --plugin-id brokerless-demo \
  --format ndjson \
  --output .\exports\brokerless-demo.ndjson \
  --split-output-size 1048576 \
  --split-output-template chunks\{name}-chunk-{part}{ext} \
  --template-number-style plain
```

Jeżeli chcesz zacząć numerację części od innej wartości niż `1`, dodaj `--split-output-start-index`:

```powershell
dotnet run --project .\samples\HlogInspect\HlogInspect.csproj -- \
  --base-dir .\samples\BrokerlessDemo\demo-logs \
  --plugin-id brokerless-demo \
  --format ndjson \
  --output .\exports\brokerless-demo.ndjson \
  --split-output-size 1048576 \
  --split-output-start-index 10
```

Jeżeli chcesz zapisać osobny plik z metadanymi okna i licznikami poziomów, dodaj `--summary-output`:

```powershell
dotnet run --project .\samples\HlogInspect\HlogInspect.csproj -- \
  --base-dir .\samples\BrokerlessDemo\demo-logs \
  --plugin-id brokerless-demo \
  --format json \
  --summary-output .\exports\brokerless-demo.summary.json
```

Domyślnie plik summary jest zapisywany jako JSON. Jeżeli wolisz tekstowy skrót operacyjny, użyj `--summary-format table`:

```powershell
dotnet run --project .\samples\HlogInspect\HlogInspect.csproj -- \
  --base-dir .\samples\BrokerlessDemo\demo-logs \
  --plugin-id brokerless-demo \
  --summary-output .\exports\brokerless-demo.summary.txt \
  --summary-format table
```

Jeżeli chcesz od razu dostać prosty rozkład po poziomie, źródle, serwisie albo identyfikatorach śledzenia, dodaj `--summary-group-by level|source|service|traceId|correlationId`:

```powershell
dotnet run --project .\samples\HlogInspect\HlogInspect.csproj -- \
  --base-dir .\samples\BrokerlessDemo\demo-logs \
  --plugin-id brokerless-demo \
  --summary-output .\exports\brokerless-demo.summary.json \
  --summary-group-by service
```

To samo działa dla przepływów tracingowych, gdy chcesz szybko policzyć rekordy per `traceId` albo `correlationId`:

```powershell
dotnet run --project .\samples\HlogInspect\HlogInspect.csproj -- \
  --base-dir .\samples\BrokerlessDemo\demo-logs \
  --plugin-id brokerless-demo \
  --summary-output .\exports\brokerless-demo.summary.json \
  --summary-group-by traceId
```

Analogicznie możesz grupować po `correlationId`, gdy chcesz zebrać rekordy z kilku serwisów do jednego przepływu korelacyjnego:

```powershell
dotnet run --project .\samples\HlogInspect\HlogInspect.csproj -- \
  --base-dir .\samples\BrokerlessDemo\demo-logs \
  --plugin-id brokerless-demo \
  --summary-output .\exports\brokerless-demo.summary.json \
  --summary-group-by correlationId
```

Jeżeli wolisz zapis z myślnikami zgodny ze stylem shella, CLI akceptuje też aliasy `--summary-group-by trace-id` oraz `--summary-group-by correlation-id`.

Kolejność i wielkość tego zestawienia możesz zawęzić przez `--summary-group-order count|key` oraz `--summary-group-limit`. Dla `level` domyślny porządek nadal odpowiada sekwencji poziomów logowania, ale po wymuszeniu `count` albo `key` możesz dostać bardziej operacyjny albo alfabetyczny widok:

```powershell
dotnet run --project .\samples\HlogInspect\HlogInspect.csproj -- \
  --base-dir .\samples\BrokerlessDemo\demo-logs \
  --plugin-id brokerless-demo \
  --summary-output .\exports\brokerless-demo.summary.json \
  --summary-group-by source \
  --summary-group-order key \
  --summary-group-limit 10
```

Jeżeli chcesz do summary dołączyć próbkę pierwszego albo ostatniego zwróconego rekordu, użyj `--summary-include-records first|last`:

```powershell
dotnet run --project .\samples\HlogInspect\HlogInspect.csproj -- \
  --base-dir .\samples\BrokerlessDemo\demo-logs \
  --plugin-id brokerless-demo \
  --summary-output .\exports\brokerless-demo.summary.json \
  --summary-include-records last
```

Jeżeli chcesz ograniczyć pola tej próbki, użyj `--summary-record-fields`:

```powershell
dotnet run --project .\samples\HlogInspect\HlogInspect.csproj -- \
  --base-dir .\samples\BrokerlessDemo\demo-logs \
  --plugin-id brokerless-demo \
  --summary-output .\exports\brokerless-demo.summary.json \
  --summary-include-records last \
  --summary-record-fields timestamp,message
```

Tutaj również możesz użyć aliasów z myślnikami, na przykład `--summary-record-fields trace-id,correlation-id,payload-json`.

Jeżeli chcesz dołączyć więcej niż jeden rekord z początku albo końca wyniku, użyj `--summary-record-count`:

```powershell
dotnet run --project .\samples\HlogInspect\HlogInspect.csproj -- \
  --base-dir .\samples\BrokerlessDemo\demo-logs \
  --plugin-id brokerless-demo \
  --summary-output .\exports\brokerless-demo.summary.json \
  --summary-include-records last \
  --summary-record-count 3 \
  --summary-record-fields timestamp,message
```

Jeżeli próbka rekordów ma być renderowana niezależnie od głównego formatu summary, użyj `--summary-records-format json|table|ndjson|csv`:

```powershell
dotnet run --project .\samples\HlogInspect\HlogInspect.csproj -- \
  --base-dir .\samples\BrokerlessDemo\demo-logs \
  --plugin-id brokerless-demo \
  --summary-output .\exports\brokerless-demo.summary.txt \
  --summary-format table \
  --summary-include-records last \
  --summary-record-count 2 \
  --summary-record-fields timestamp,message \
  --summary-records-format json
```

Przy osobnym pliku próbki `ndjson` jest wygodne do dalszego przetwarzania potokowego:

```powershell
dotnet run --project .\samples\HlogInspect\HlogInspect.csproj -- \
  --base-dir .\samples\BrokerlessDemo\demo-logs \
  --plugin-id brokerless-demo \
  --summary-output .\exports\brokerless-demo.summary.json \
  --summary-include-records last \
  --summary-record-count 2 \
  --summary-record-fields timestamp,message \
  --summary-records-format ndjson \
  --summary-records-output .\exports\brokerless-demo.records.ndjson
```

Jeżeli chcesz od razu dostać próbkę w formacie tabelarycznym do Excela albo prostego importu, użyj `csv`:

```powershell
dotnet run --project .\samples\HlogInspect\HlogInspect.csproj -- \
  --base-dir .\samples\BrokerlessDemo\demo-logs \
  --plugin-id brokerless-demo \
  --summary-output .\exports\brokerless-demo.summary.txt \
  --summary-format table \
  --summary-include-records last \
  --summary-record-count 2 \
  --summary-record-fields timestamp,message \
  --summary-records-format csv
```

Jeżeli próbkę rekordów chcesz zapisać do osobnego pliku obok głównego summary, użyj `--summary-records-output`:

```powershell
dotnet run --project .\samples\HlogInspect\HlogInspect.csproj -- \
  --base-dir .\samples\BrokerlessDemo\demo-logs \
  --plugin-id brokerless-demo \
  --summary-output .\exports\brokerless-demo.summary.json \
  --summary-include-records last \
  --summary-record-fields timestamp,message \
  --summary-records-format json \
  --summary-records-output .\exports\brokerless-demo.records.json
```

Jeżeli nazwa pliku próbki ma się wyliczać automatycznie z nazwy summary, trybu i formatu, użyj `--summary-records-template`. Dostępne tokeny: `{name}`, `{mode}`, `{format}`, `{count}`, `{ext}`. Token `{count}` podstawia rzeczywistą liczbę rekordów próbki, a `--template-number-style plain` pozwala zapisać ją bez paddingu. Nieznane tokeny są odrzucane jako błąd CLI.

```powershell
dotnet run --project .\samples\HlogInspect\HlogInspect.csproj -- \
  --base-dir .\samples\BrokerlessDemo\demo-logs \
  --plugin-id brokerless-demo \
  --summary-output .\exports\brokerless-demo.summary.json \
  --summary-include-records last \
  --summary-record-fields timestamp,message \
  --summary-records-format ndjson \
  --summary-records-template {name}-{mode}-{count}{ext}
```

W praktyce `audit` nadaje się do raportów operacyjnych, `forensics` do zrzutów śledczych (payload + context śledzenia, bez pełnego komunikatu tekstowego), a `triage` do szybkiej analizy incydentów z minimalnym zestawem pól.

Eksport JSON albo NDJSON:

```powershell
dotnet run --project .\samples\HlogInspect\HlogInspect.csproj -- \
  --base-dir .\samples\BrokerlessDemo\demo-logs \
  --plugin-id brokerless-demo \
  --format json

dotnet run --project .\samples\HlogInspect\HlogInspect.csproj -- \
  --base-dir .\samples\BrokerlessDemo\demo-logs \
  --plugin-id brokerless-demo \
  --format ndjson
```

Możesz też użyć parametrów CLI:

```powershell
dotnet run --project .\samples\BrokerlessDemo\BrokerlessDemo.csproj -- \
  --plugin-id brokerless-demo \
  --log-dir .\samples\BrokerlessDemo\demo-logs \
  --hold-seconds 1
```

Jeżeli wolisz wariant webowy, dostępny jest też minimalny sample ASP.NET Core działający bez brokera:

```powershell
$env:HOBIARY_PLUGIN_ID = 'brokerless-web-demo'
$env:HOBIARY_DIRECT_LOG_DIR = '.\samples\BrokerlessWebDemo\demo-logs'
dotnet run --project .\samples\BrokerlessWebDemo\BrokerlessWebDemo.csproj
```

Po starcie możesz wywołać `GET /`, `GET /orders/42`, `GET /fail` i `GET /logs/recent?limit=20&minutes=30`, a middleware `UseHobiaryHttpLogging()` oraz endpointy dopiszą rekordy bezpośrednio do `.hlog`.

Jeżeli chcesz uruchomić oba demo jednym poleceniem, użyj:

```powershell
pwsh -File .\tools\start-brokerless-demos.ps1
```

Skrypt przed uruchomieniem czyści tylko katalogi `demo-logs` odpowiadające wskazanym pluginom, dzięki czemu odczyt po zakończeniu pokazuje świeży przebieg zamiast miksu wcześniejszych danych.

Jeżeli chcesz, żeby skrypt po zakończeniu automatycznie zamknął web demo, użyj przełącznika:

```powershell
pwsh -File .\tools\start-brokerless-demos.ps1 -StopWebOnExit
```

Wariant webowy udostępnia też prosty widok HTML ostatnich rekordów:

```powershell
curl http://localhost:5088/logs/view?limit=20&minutes=30
```

### 3. Opcje zaawansowane InputPlugin

```csharp
    options.GzipEnabled              = true;  // kompresja payloadu (domyślnie false)
    options.HeartbeatIntervalSeconds = 30;    // heartbeat co 30 s (0 = wyłączone)
```

  Uwagi:

  - `GzipEnabled`, `HeartbeatIntervalSeconds`, `Host`, `Port`, `Username`, `Password`, `VirtualHost`, `AnnounceExchange` i `DataExchange` mają znaczenie tylko w trybie `RabbitMq`.
  - `BaseLogDirectory` ma znaczenie tylko w trybie `Direct`.

### 4. Używaj przez `ILogPipeWriter`

```csharp
public class OrderService(ILogPipeWriter logPipe)
{
    public async Task PlaceOrderAsync(Order order)
    {
    await logPipe.WriteAsync(new LogMessage
        {
            Level   = LogPipeLevel.Info,
            Message = $"Zamówienie {order.Id} złożone",
            Source  = "OrderService"
        });
    }
}
```

### 5. Przy starcie aplikacji

InputPlugin automatycznie wysyła komunikat rejestracyjny do `hobiary.announce`. Nie wymaga dodatkowej konfiguracji.

### 6. Mini troubleshooting `hobiarylogdb.dll` / `libhobiarylogdb.so`

Najczęstsze problemy w trybie `Direct` są związane z natywną biblioteką HobiaryLogDB albo z prawami do katalogu docelowego.

Checklist:

1. Biblioteka `hobiarylogdb.dll` albo `libhobiarylogdb.so` znajduje się w output projektu uruchamianej aplikacji.
2. Architektura biblioteki zgadza się z architekturą procesu .NET.
3. `BaseLogDirectory` wskazuje katalog, do którego proces może zapisywać.

Typowe objawy i interpretacja:

- `Nie można załadować biblioteki natywnej 'hobiarylogdb' ...`: biblioteka nie została skopiowana do output projektu, loader nie znajduje zależności natywnych albo architektura biblioteki nie zgadza się z procesem.
- `Nie można przygotować katalogu logu ...`: ścieżka `BaseLogDirectory` jest niepoprawna, za długa albo proces nie ma uprawnień do tworzenia katalogów.
- `Nie można otworzyć logu HobiaryLogDB ...`: `hlog_open` nie może utworzyć albo otworzyć katalogu i plików logu.
- `Nie można zapisać rekordu do HobiaryLogDB ... hlog_append zwróciło kod ...`: problem jest już po stronie natywnego zapisu; sprawdź katalog logu, dostęp do plików segmentów i zgodność środowiska uruchomieniowego.
- brak nowych plików `.hlog`: aplikacja nie pracuje w `Direct`, zapisuje do innego katalogu albo używa innego `PluginId` niż oczekiwany.

Jeżeli budujesz bibliotekę lokalnie, użyj w repo `hobiary-logdb`:

```powershell
cmake -S . -B build -G "Ninja" -DCMAKE_BUILD_TYPE=Debug
cmake --build build
```

Potem skopiuj `build/hobiarylogdb.dll` albo `build/libhobiarylogdb.so` do katalogu wyjściowego projektu .NET.

---

## Uruchomienie adaptera

```powershell
# Development
cd src/HobiaryLogPipe.Adapter
dotnet run

# Production (jako Windows Service)
dotnet publish -c Release -o ./publish
.\publish\HobiaryLogPipe.Adapter.exe --contentRoot .
```

Linux (systemd):

```ini
[Service]
ExecStart=/opt/hobiary-logpipe/HobiaryLogPipe.Adapter
WorkingDirectory=/opt/hobiary-logpipe
Restart=always
```

---

## Gdzie trafiają logi?

```
{BaseLogDirectory}/
  orders-service/
    hlog_000001.hlog
    hlog_000001.hidx_time
    hlog_000001.hidx_error
    hlog_000001.hidx_level
    hlog_000001.hidx_src
  api-gateway/
    hlog_000001.hlog
    ...
```

Każdy plugin ma **izolowany katalog**. Adapter nie pozwoli pluginowi zapisać danych poza jego katalogiem.

---

## Odczyt logów

### REST API adaptera

Adapter udostępnia wbudowane REST API. Nie wymaga dodatkowej konfiguracji — dostępne od razu po uruchomieniu.

Kontrakt opisany poniżej traktuj jako `v1`: nowe pola mogą dochodzić, ale istniejące pola i kody błędów nie powinny zmieniać znaczenia.

#### Dashboard HTML

```
GET /dashboard
```

Wbudowany panel operatorski dostępny bez osobnego frontendu. Pokazuje listę pluginów, szczegóły wybranego pluginu, statystyki poziomów, historię logów oraz live feed przez SSE.

Dashboard pozwala też:
- ustawić zakres czasu `from` / `to` dla statystyk i historii,
- wyeksportować bieżący filtr jako NDJSON,
- przechodzić między stronami historii logów,
- obserwować stan połączenia SSE i liczbę reconnectów.

Jeśli ustawisz `HttpApiKey`, otwieraj dashboard jako `GET /dashboard?apiKey=TWÓJ_KLUCZ` albo przekazuj nagłówek `X-Api-Key` w kliencie HTTP.

#### Lista pluginów

```
GET /api/plugins
```

Zwraca tablicę zarejestrowanych pluginów z liczą rekordów, formatem i stanem heartbeatu.

#### Szczegóły pluginu

```
GET /api/plugins/{pluginId}
```

Zwraca m.in. `lastHeartbeatAt` i `isOnline` (null gdy heartbeat wyłączony).

#### Statystyki logów

```
GET /api/plugins/{pluginId}/stats?from=2026-04-01T00:00:00Z&to=2026-04-01T23:59:59Z
```

Zwraca sumę logów oraz rozkład per poziom (`Trace`, `Debug`, `Info`, `Warn`, `Error`, `Fatal`) dla wybranego zakresu czasu.

#### Zapytanie o logi

```
GET /api/logs?pluginId=orders-service&from=2026-04-01T00:00:00Z&level=Warning&limit=200
```

| Parametr | Opis | Domyślnie |
|---|---|---|
| `pluginId` | Wymagane | — |
| `from` | Początek zakresu (ISO 8601) | początek plików |
| `to` | Koniec zakresu (ISO 8601) | teraz |
| `level` | Minimalny poziom (`Trace`–`Fatal`) | `Trace` |
| `limit` | Maks. liczba rekordów (1–1000) | `100` |
| `paged` | Gdy `true`, odpowiedź jest stronicowana | `false` |
| `cursor` | `timestampUs` ostatniego rekordu z poprzedniej strony | brak |

Odpowiedź zawiera tablicę obiektów `{ timestampUs, level, source, service, payload, traceId, correlationId }`.

Przy `paged=true` odpowiedź ma postać:

```json
{
  "items": [
    { "timestampUs": 1746057600000000, "level": "info", "payload": "..." }
  ],
  "cursor": null,
  "limit": 100,
  "nextCursor": 1746057600000000,
  "hasMore": true
}
```

Kolejną stronę pobierasz przez przekazanie `cursor` z poprzedniej odpowiedzi. Parametr `offset` pozostaje dostępny tylko dla kompatybilności wstecznej.

#### Format błędów

Dla odpowiedzi `400`, `401` i `404` adapter zwraca wspólny kształt:

```json
{
  "code": "invalid_plugin_id",
  "error": "Invalid pluginId.",
  "details": {
    "pluginId": "../../bad"
  }
}
```

Pole `details` jest opcjonalne. Typowe kody to `invalid_plugin_id`, `invalid_time_range`, `invalid_level`, `plugin_not_found` i `unauthorized`.

#### Eksport NDJSON

```
GET /api/plugins/{pluginId}/logs/export?from=2026-04-01T00:00:00Z&level=Warning
```

Zwraca strumień `application/x-ndjson`, jeden rekord JSON na linię. Format rekordu jest zgodny z `GET /api/logs`.

Opcjonalnie możesz ograniczyć eksport do fragmentu danych:

``` 
GET /api/plugins/{pluginId}/logs/export?cursor=1746057600000000&limit=100
```

To przydaje się przy dużych zakresach czasu albo gdy dashboard eksportuje tylko aktualnie oglądaną stronę historii. Oprócz NDJSON odpowiedź zwraca nagłówki `X-Export-Limit`, `X-Export-Count`, `X-Has-More` i opcjonalnie `X-Next-Cursor`.

#### SSE live streaming

```
GET /api/logs/stream
GET /api/logs/stream/{pluginId}
```

Połączenie typu `text/event-stream`. Każde zdarzenie to JSON:

```json
// zdarzenie logu
data: {"pluginId":"orders-service","level":"Error","timestamp":1746057600000000,"payload":"..."}

// zdarzenie zmiany stanu pluginu
data: {"pluginId":"orders-service","eventType":"plugin_offline","timestampUs":1746057700000000}
```

Dashboard `/dashboard` używa tego endpointu do aktualizacji live feedu bez odświeżania strony.

#### Metryki

```
GET /api/metrics
```

Liczniki globalne: `totalLogs`, `totalErrors`, `totalDropped`, `activePlugins`.

### Odczyt bezpośrednio przez HobiaryLogClient

Użyj `HobiaryLogClient` z projektu `hobiary-logdb`:

```csharp
using var client = new HobiaryLogClient("C:/logs/hobiary/orders-service");

// Ostatnia godzina
long from = (DateTimeOffset.UtcNow - TimeSpan.FromHours(1)).ToUnixTimeMilliseconds() * 1000L;
var records = client.ReadRange(fromTs: from);

foreach (var rec in records)
    Console.WriteLine($"[{rec.Level}] {Encoding.UTF8.GetString(rec.Payload ?? [])}");
```

---

## Heartbeat i monitoring

InputPlugin wysyła wiadomość heartbeat co `HeartbeatIntervalSeconds` sekund (domyślnie 30). Adapter rejestruje czas ostatniego heartbeatu i uznaje plugin za **offline** gdy minie `HeartbeatTimeoutSeconds` (domyślnie 90) bez odpowiedzi.

Zmiana stanu jest emitowana jako zdarzenie SSE (`plugin_online` / `plugin_offline`) i widoczna w `GET /api/plugins/{pluginId}` jako `isOnline`.

Aby **wyłączyć** heartbeat:
- InputPlugin: `options.HeartbeatIntervalSeconds = 0`
- Adapter: `HeartbeatTimeoutSeconds = 0`

---

## Zachowanie przy błędach

| Sytuacja | Zachowanie |
|---|---|
| Nieznany `pluginId` | Wiadomość trafia na `hobiary.unknown` (lub drop — per config) |
| Brak wymaganego pola (`PluginRequiredFields`) | NACK → dead-letter |
| Poziom logu poniżej minimum (`PluginMinLevels`) | NACK → dead-letter |
| Przekroczony rate limit (`PluginRateLimits`) | NACK bez requeueing |
| Nieprawidłowe szyfrowanie payloadu | NACK bez requeueing |
| Błąd zapisu do HobiaryLogDB | `Nack` → wiadomość wraca do kolejki; po `MaxDlqRetries` próbach → dead-letter |
| RabbitMQ niedostępny | Adapter czeka `ReconnectDelaySeconds`, próbuje ponownie w nieskończoność |
| Payload > 64 KB | Obcinany do 65 536 B, rekord zapisywany z flagą `HLOG_RFLAG_PAYLOAD_TRUNC` |
| `fieldMapping` z niedozwolonymi znakami | Rejestracja odrzucana, błąd w logach adaptera |

---

## Typowe pytania

**Czy mogę mieć wiele instancji adaptera?**
Nie — HobiaryLogDB jest silnikiem osadzanym, nie ma serwera. Jedna instancja adaptera per katalog logów.

**Czy plugin może działać bez RabbitMQ?**
Tak. Ustaw `Transport = LogPipeTransport.Direct` i `BaseLogDirectory`, a logi
będą zapisywane lokalnie przez HobiaryLogDB. Wymagana jest biblioteka natywna.
Tryb RabbitMq nadal wymaga brokera; Direct nie jest automatycznym fallbackiem.

**Jak zmienić format wiadomości już po rejestracji?**
Zrestartuj aplikację z nowym `fieldMapping` — plugin wyśle nowy komunikat rejestracyjny, adapter zaktualizuje registry.

**Czy stare logi są usuwane automatycznie?**
Nie automatycznie. Wywołaj `client.PurgeBefore(cutoff)` z `HobiaryLogClient` lub dodaj scheduler po stronie aplikacji odczytującej.
