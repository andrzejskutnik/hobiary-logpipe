# HobiaryLogPipe — Architektura Systemu

## 1. Widok ogólny — przepływ danych

HobiaryLogPipe ma dziś dwa równoległe warianty transportu:

- `RabbitMq` — klasyczny pipeline `InputPlugin -> RabbitMQ -> Adapter -> HobiaryLogDB`
- `Direct` — brokerless path `InputPlugin -> HobiaryLogDB`, bez RabbitMQ i bez Adaptera

```mermaid
flowchart TD
    subgraph Prod["Producenci logów"]
        P1["orders-service\n[InputPlugin]"]
        P2["api-gateway\n[InputPlugin]"]
        P3["inventory-svc\n[InputPlugin]"]
    end

    subgraph RMQ["RabbitMQ"]
        ANN["hobiary.announce\n(fanout exchange)"]
        DATA["hobiary.data\n(direct exchange)"]
        DLQ["hobiary.unknown\n(dead-letter queue)"]
    end

    subgraph ADP["Adapter — Worker Service"]
        AC["AnnounceConsumer"]
        PR["PluginRegistry"]
        DC["DataConsumer"]
    end

    subgraph HDB["HobiaryLogDB  {BaseLogDirectory}/"]
        L1["orders-service/\nhlog_*.hlog + hidx_*"]
        L2["api-gateway/\nhlog_*.hlog + hidx_*"]
        L3["inventory-service/\nhlog_*.hlog + hidx_*"]
    end

    P1 & P2 & P3 -- "AMQP\n(announce)" --> ANN
    P1 & P2 & P3 -- "AMQP\n(logi)" --> DATA
    ANN --> AC
    DATA --> DC
    AC <--> PR
    DC <--> PR
    DC --> L1 & L2 & L3
    AC -. "dead-letter" .-> DLQ
    DC -. "dead-letter" .-> DLQ
```

### 1a. Widok ogólny — wariant `Direct`

```mermaid
flowchart TD
    APP["App / ASP.NET Core / Worker"] --> IP["InputPlugin\nTransport = Direct"]
    IP --> DIR["{BaseLogDirectory}/{PluginId}/"]
    DIR --> HLOG["hlog_*.hlog + hidx_*"]
```

---

## 2. Sekwencja rejestracji pluginu

```mermaid
sequenceDiagram
    participant App as App + Plugin
    participant RMQ as RabbitMQ
    participant Adp as Adapter
    participant Reg as PluginRegistry

    App->>RMQ: PUBLISH hobiary.announce (AnnounceMessage)
    RMQ->>Adp: DELIVER

    Adp->>Reg: ValidatePluginId(pluginId)
    Reg-->>Adp: in allowlist?

    alt pluginId w allowliście
        Adp->>Reg: ValidateFieldMapping(mapping)
        Reg-->>Adp: OK
        Adp->>Reg: Register(pluginId, cfg, HobiaryLogClient)
        Adp->>RMQ: Bind queue hobiary.data, routing key = pluginId
        Adp->>RMQ: ACK
    else NOT IN ALLOWLIST
        Adp->>RMQ: NACK → dead-letter
    end
```

---

## 3. Sekwencja przetwarzania wiadomości danych

```mermaid
sequenceDiagram
    participant App as App + Plugin
    participant RMQ as RabbitMQ
    participant Adp as Adapter
    participant HLC as HobiaryLogClient

    App->>RMQ: PUBLISH hobiary.data, key: orders-svc
    RMQ->>Adp: DELIVER

    Adp->>Adp: LookupPlugin(routingKey → pluginId)
    Adp->>Adp: DecryptPayload (jeśli PayloadEncryption.Enabled)
    Adp->>Adp: DecompressGzip (jeśli x-hobiary-encoding: gzip)
    Adp->>Adp: CheckPayloadSize (truncate > 64 KB)

    alt Heartbeat {"type":"heartbeat"}
        Adp->>Adp: TouchHeartbeat()
        Adp->>RMQ: ACK
    else Log message
        Adp->>Adp: ParseJson (per fieldMapping)
        Adp->>Adp: SchemaValidation (PluginRequiredFields)
        Adp->>Adp: LevelFilter (PluginMinLevels)
        Adp->>Adp: RateLimit (PluginRateLimits)
        Adp->>HLC: Append(LogRecord)
        Adp->>SSE: Emit(logEvent)

        alt Append OK
            HLC-->>Adp: 0 (sukces)
            Adp->>RMQ: ACK
        else Append ERROR
            HLC-->>Adp: -1 (błąd)
            Adp->>RMQ: NACK requeue [retry ≤ MaxDlqRetries]
            Note over Adp,RMQ: po MaxDlqRetries próbach → dead-letter
        end
    end
```

---

## 3a. Sekwencja zapisu w wariancie `Direct`

```mermaid
sequenceDiagram
    participant App as App / ILogger / ILogPipeWriter
    participant LPW as LogPipeWriter
    participant DRT as DirectLogPipeTransport
    participant HLC as NativeDirectLogClient
    participant HDB as HobiaryLogDB

    App->>LPW: WriteAsync(LogMessage)
    LPW->>DRT: WriteAsync(LogMessage)
    DRT->>DRT: SerializeMessage(...)
    DRT->>DRT: ResolveTraceId / ResolveCorrelationId
    DRT->>HLC: Append(DirectHlogRecord)
    HLC->>HDB: hlog_append(...)
    HDB-->>HLC: 0 / error code
    HLC-->>DRT: success / exception
    DRT-->>LPW: Task.CompletedTask
```

---

## 4. Struktura wewnętrzna Adaptera

```mermaid
classDiagram
    class AdapterService {
        <<IHostedService>>
        +StartAsync(CancellationToken)
        +StopAsync(CancellationToken)
        -HandleAnnounceAsync()
        -HandleDataAsync()
    }
    class HeartbeatMonitor {
        <<BackgroundService>>
        -CheckIntervalMs = 10000
        +ExecuteAsync(CancellationToken)
        +RunCheckAsync(CancellationToken)
    }
    class MetricsLogger {
        <<BackgroundService>>
    }
    class AnnounceConsumer {
        +OnMessage(AnnounceMessage)
        -ValidatePluginId(string) bool
        -ValidateFieldMapping(FieldMappingConfig) bool
        -BindQueue(string pluginId)
    }
    class DataConsumer {
        +OnMessage(string routingKey, byte[] body)
        -MapFields(FieldMappingConfig, JsonElement) LogRecord
        -TruncatePayload(byte[], int maxSize) byte[]
    }
    class PluginRegistry {
        -ConcurrentDictionary~string,PluginRegistration~ _reg
        +Register(string, PluginRegistration)
        +TryGet(string, out PluginRegistration) bool
        +Contains(string pluginId) bool
        +All : IEnumerable~PluginRegistration~
    }
    class PluginRegistration {
        +PluginId : string
        +FieldMapping : FieldMappingConfig
        +MessageFormat : string
        +Client : HobiaryLogClient
        +RegisteredAt : DateTimeOffset
        +LastHeartbeatAt : DateTimeOffset?
        +TouchHeartbeat()
        +IsOnline(timeoutSeconds) bool
    }
    class AdapterOptions {
        +AllowedPlugins : string[]
        +BaseLogDirectory : string
        +UnknownPluginAction : string
        +MaxDlqRetries : int
        +HeartbeatTimeoutSeconds : int
        +PluginMinLevels : Dictionary~string,string~
        +PluginRateLimits : Dictionary~string,int~
        +PluginRequiredFields : Dictionary~string,string[]~
        +PayloadEncryption : PayloadEncryptionOptions
    }
    class SseHub {
        +Subscribe() ChannelReader~string~
        +Unsubscribe(ChannelReader)
        +Emit(pluginId, level, ts, payload)
        +EmitPluginStatus(pluginId, online)
    }

    AdapterService --> AnnounceConsumer
    AdapterService --> DataConsumer
    AdapterService --> SseHub
    HeartbeatMonitor --> PluginRegistry
    HeartbeatMonitor --> SseHub
    AnnounceConsumer --> PluginRegistry
    DataConsumer --> PluginRegistry
    PluginRegistry "1" *-- "many" PluginRegistration
    AdapterService ..> AdapterOptions
    HeartbeatMonitor ..> AdapterOptions
```

---

## 5. Struktura wewnętrzna InputPlugin

```mermaid
classDiagram
    class ILogPipeWriter {
        <<interface>>
        +WriteAsync(LogMessage) Task
    }
    class LogPipeWriter {
        <<IHostedService>>
        +StartAsync(CancellationToken)
        +StopAsync(CancellationToken)
        +WriteAsync(LogMessage) Task
        -_transport : ILogPipeTransport
    }
    class ILogPipeTransport {
        <<interface>>
        +StartAsync(CancellationToken) Task
        +StopAsync(CancellationToken) Task
        +WriteAsync(LogMessage, CancellationToken) Task
    }
    class RabbitMqLogPipeTransport {
        +ConnectAsync() Task
        +AnnounceAsync() Task
        +HeartbeatLoopAsync(CancellationToken) Task
        +Publish(exchange, routingKey, body)
    }
    class DirectLogPipeTransport {
        +StartAsync(CancellationToken) Task
        +WriteAsync(LogMessage, CancellationToken) Task
        -SerializeMessage(InputPluginOptions, LogMessage) byte[]
        -ResolveTraceId(LogMessage) string
        -ResolveCorrelationId(LogMessage) string
    }
    class NativeDirectLogClient {
        +Append(DirectHlogRecord)
    }
    class InputPluginOptions {
        +Transport : LogPipeTransport
        +PluginId : string
        +BaseLogDirectory : string
        +Host : string
        +Port : int
        +VirtualHost : string
        +Username : string
        +Password : string
        +FieldMapping : FieldMappingConfig
        +GzipEnabled : bool
        +HeartbeatIntervalSeconds : int
    }

    LogPipeWriter ..|> ILogPipeWriter
    LogPipeWriter --> ILogPipeTransport
    RabbitMqLogPipeTransport ..|> ILogPipeTransport
    DirectLogPipeTransport ..|> ILogPipeTransport
    LogPipeWriter --> InputPluginOptions
    DirectLogPipeTransport --> NativeDirectLogClient
```

---

## 6. Warstwy zależności projektów

```mermaid
flowchart TD
    Contracts["HobiaryLogPipe.Contracts\nAnnounceMessage · LogMessage\nFieldMappingConfig · LogPipeLevel"]
    HobiaryLog["HobiaryLogPipe.HobiaryLog\nHobiaryLogClient · QueryRange · CountByLevel"]

    InputPlugin["HobiaryLogPipe.InputPlugin\n+ RabbitMQ.Client"]
    Adapter["HobiaryLogPipe.Adapter\n+ RabbitMQ.Client\n+ Microsoft.Extensions.Hosting"]

    InputTests["InputPlugin.Tests\n(xUnit)"]
    AdapterTests["Adapter.Tests\n(xUnit)"]

    InputPlugin --> Contracts
    HobiaryLog --> Contracts
    Adapter --> Contracts
    Adapter --> HobiaryLog
    InputTests --> InputPlugin
    InputTests --> HobiaryLog
    AdapterTests --> Adapter
    AdapterTests --> HobiaryLog

    InputPlugin -. "❌ zakaz referencji" .-> Adapter
```

---

## 7. Model bezpieczeństwa

```mermaid
flowchart LR
    subgraph Untrusted["NIEZAUFANE — dane od pluginu"]
        PI["pluginId"]
        FM["fieldMapping"]
        LD["logDirectory\n(brak w protokole)"]
        PL["payload (JSON)"]
    end

    subgraph Boundary["GRANICA ZAUFANIA — Adapter"]
        VA["weryfikacja\nallowlisty"]
        VF["walidacja regex\n[a-zA-Z0-9_.-]{1,64}"]
        RJ["odrzucone\nAdapter sam decyduje o ścieżce"]
        SL["size limit 64 KB\n(truncate + flaga)"]
    end

    subgraph Trusted["ZAUFANE — konfiguracja"]
        AL["AllowedPlugins[ ]"]
        BD["BaseLogDirectory\n→ {BD}/{pluginId}/"]
        CR["AMQP credentials\nper-plugin VHost"]
    end

    PI --> VA --> AL
    FM --> VF
    LD --> RJ
    PL --> SL
    BD --> RJ
```

### 7a. Known limitations wariantu `Direct`

Wariant `Direct` jest świadomie uproszczony i ma inny profil ryzyka niż pełny pipeline z Adapterem.

Najważniejsze ograniczenia:

- brak `announce`, więc nie istnieje centralna rejestracja pluginu ani dynamiczne bindowanie po `pluginId`,
- brak `heartbeat`, więc nie ma pojęcia `online/offline` ani przejść stanów znanych z `PluginRegistry`,
- brak `dead-letter`, retry i `MaxDlqRetries`, więc błąd zapisu kończy się lokalnie w procesie producenta,
- brak warstwy `allowlist`, `PluginRequiredFields`, `PluginMinLevels` i `PluginRateLimits`, bo nie ma Adaptera, który mógłby je egzekwować,
- brak centralnego REST API, dashboardu, SSE i live-obserwowalności,
- odpowiedzialność za `BaseLogDirectory` przechodzi na aplikację używającą `InputPlugin`,
- izolacja między pluginami wynika wyłącznie z tego, jaki katalog wybierze proces i jakim `PluginId` się posługuje,
- brokerless path nie amortyzuje chwilowych problemów I/O kolejką RabbitMQ; opóźnienie lub błąd dysku jest widoczny bezpośrednio w aplikacji.

W praktyce `Direct` jest dobry do lokalnych demo, środowisk single-host i prostych wdrożeń embedded, ale nie zastępuje pełnego modelu operatorskiego Adaptera.

### 7b. Różnice bezpieczeństwa i operacyjne: `RabbitMq` vs `Direct`

| Obszar | `RabbitMq` | `Direct` |
|---|---|---|
| Granica zaufania | Wymuszona przez Adapter | Wewnątrz procesu producenta |
| Kontrola `pluginId` | `allowlist` + walidacja regex w Adapterze | Walidacja lokalna w `InputPlugin`, bez centralnej polityki |
| Wybór katalogu logów | Adapter wyznacza `{BaseLogDirectory}/{pluginId}` | Proces sam wskazuje `BaseLogDirectory` |
| Retry / buforowanie | RabbitMQ + NACK + dead-letter | Brak kolejki pośredniej |
| Heartbeat / online status | Tak | Nie |
| Dashboard / API / SSE | Tak | Nie |
| Rate limiting / required fields | Tak, w Adapterze | Nie |
| Blast radius awarii | Błąd producenta może być zamortyzowany przez broker | Problem zapisu uderza bezpośrednio w producenta |

Konsekwencje projektowe:

- `RabbitMq` preferuj tam, gdzie potrzebne są polityki bezpieczeństwa, centralny nadzór, wieloźródłowy ingest i separacja obowiązków między producentem a storage,
- `Direct` preferuj tam, gdzie najważniejsze są prostota wdrożenia, brak brokera i lokalny zapis na tym samym hoście,
- przejście z `Direct` na `RabbitMq` nie zmienia formatu `.hlog`, ale zmienia odpowiedzialność operacyjną: z procesu aplikacji na Adapter + RabbitMQ.

---

## 8. Maszyna stanów PluginRegistration

```mermaid
stateDiagram-v2
    [*] --> UNKNOWN

    UNKNOWN --> VALIDATING : announce odebrane
    VALIDATING --> REJECTED : walidacja nie przeszła
    VALIDATING --> REGISTERED : walidacja OK + bind queue
    REGISTERED --> ONLINE : heartbeat odebrany
    ONLINE --> OFFLINE : brak heartbeatu przez HeartbeatTimeoutSeconds
    OFFLINE --> ONLINE : heartbeat odebrany
    REGISTERED --> RE_REG : nowe announce (ten sam pluginId)
    RE_REG --> REGISTERED : aktualizacja fieldMapping\n(stary klient .hlog pozostaje)
    REJECTED --> [*]

    note right of UNKNOWN
        dane pluginu → dead-letter / drop
    end note

    note right of REGISTERED
        aktywny: logi są persystowane\n(heartbeat jeszcze nie odebrany)
    end note

    note right of ONLINE
        SSE emituje plugin_offline/plugin_online\nprzy każdej zmianie stanu
    end note

    note right of RE_REG
        hot-reload konfiguracji
    end note
```

---

## 9. Układ plików na dysku (runtime)

```
{BaseLogDirectory}/
├── orders-service/              ← tworzone przy pierwszej rejestracji
│   ├── hlog_000001.hlog
│   ├── hlog_000001.hidx_time
│   ├── hlog_000001.hidx_error
│   ├── hlog_000001.hidx_level
│   ├── hlog_000001.hidx_src
│   └── hlog_000002.hlog         ← po rotacji (segment > 64 MB)
├── api-gateway/
│   └── hlog_000001.hlog
├── inventory-service/
│   └── hlog_000001.hlog
└── hobiary.unknown/             ← opcjonalny zapis dead-letter
```

---

## 10. Diagram komponentów NuGet / referencji

```mermaid
graph LR
    subgraph Ext["Zewnętrzne NuGet"]
        RMQ["RabbitMQ.Client\n(AMQP 0.9.1)"]
        MEH["Microsoft.Extensions\n.Hosting"]
        HLC["HobiaryLogClient\n(P/Invoke → hobiarylogdb.dll)"]
        STJ["System.Text.Json\n(wbudowany w .NET 9)"]
    end

    Contracts["HobiaryLogPipe.Contracts"] --> STJ
    HobiaryLog["HobiaryLogPipe.HobiaryLog"] --> HLC
    InputPlugin["HobiaryLogPipe.InputPlugin"] --> RMQ
    InputPlugin --> Contracts
    Adapter["HobiaryLogPipe.Adapter"] --> RMQ
    Adapter --> MEH
    Adapter --> HobiaryLog
    Adapter --> Contracts
```

---

## 11. REST API i SSE — endpointy

Adapter eksponuje wbudowany HTTP server (`Microsoft.AspNetCore.Builder.WebApplication`) na porcie `8080` (konfigurowalny przez `ASPNETCORE_URLS`).

### Endpointy

| Metoda | Ścieżka | Opis |
|--------|---------|------|
| `GET` | `/api/plugins` | Lista zarejestrowanych pluginów (pluginId, format, czasy) |
| `GET` | `/api/plugins/{pluginId}` | Szczegóły pluginu: pola, `lastHeartbeatAt`, `isOnline` |
| `GET` | `/api/plugins/{pluginId}/logs` | Odczyt logów z `.hlog` (query: `from`, `to`, `level`, `limit`) |
| `GET` | `/events` | SSE stream: zdarzenia w czasie rzeczywistym |

### SSE — typy zdarzeń

```json
{ "eventType": "log",           "pluginId": "orders-svc", "level": "Error", "timestampUs": 1700000000000000, "payload": "..." }
{ "eventType": "plugin_online", "pluginId": "orders-svc", "timestampUs": 1700000000000000 }
{ "eventType": "plugin_offline","pluginId": "orders-svc", "timestampUs": 1700000000000000 }
```

### Heartbeat flow

```mermaid
sequenceDiagram
    participant App as InputPlugin
    participant RMQ as RabbitMQ
    participant Adp as Adapter
    participant HBM as HeartbeatMonitor
    participant SSE as SseHub

    loop co HeartbeatIntervalSeconds (domyślnie 30s)
        App->>RMQ: PUBLISH hobiary.data, body: {"type":"heartbeat"}
        RMQ->>Adp: DELIVER
        Adp->>Adp: TouchHeartbeat()
        Adp->>RMQ: ACK
    end

    loop co 10s
        HBM->>HBM: sprawdź wszystkie PluginRegistration
        alt plugin offline (brak hb > HeartbeatTimeoutSeconds)
            HBM->>SSE: EmitPluginStatus(pluginId, online=false)
        else plugin wrócił do online
            HBM->>SSE: EmitPluginStatus(pluginId, online=true)
        end
    end
```

