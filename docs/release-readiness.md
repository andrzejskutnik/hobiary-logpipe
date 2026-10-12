# Scenariusze testowe przed wydaniem

Dokument definiuje scenariusze, które muszą przejść przed uznaniem wersji HobiaryLogPipe za gotową do wydania.

## Kryteria wejścia

- Build wykonywany na .NET 10 SDK.
- Dostępna biblioteka HobiaryLogDB: `hobiarylogdb.dll` na Windows albo `libhobiarylogdb.so` na Linux.
- Dla scenariuszy RabbitMQ działa RabbitMQ 3.12+.
- Testy wykonuje się na świeżym katalogu logów.

## Gate automatyczny

```powershell
dotnet build HobiaryLogPipe.slnx
dotnet test HobiaryLogPipe.slnx --logger "console;verbosity=minimal"
```

Kryterium: wszystkie testy przechodzą, bez nowych ostrzeżeń i wyjątków procesu testowego.

## 1. Start i konfiguracja

| ID | Scenariusz | Oczekiwany rezultat |
|---|---|---|
| CFG-01 | Adapter startuje z poprawną konfiguracją RabbitMQ | Połączenie `Connected`, health check `Healthy` |
| CFG-02 | Niepoprawny host, port lub credentials | Start/reconnect kończy się kontrolowanym błędem; proces nie zawiesza się |
| CFG-03 | Niepoprawna konfiguracja TLS | Połączenie jest odrzucone z czytelnym błędem |
| CFG-04 | Hot reload `AllowedPlugins`, limitów i required fields | Nowe ustawienia obowiązują bez restartu adaptera |
| CFG-05 | Brak biblioteki HobiaryLogDB | Błąd wskazuje brakującą bibliotekę i katalog |

## 2. Rejestracja pluginu

| ID | Scenariusz | Oczekiwany rezultat |
|---|---|---|
| REG-01 | Poprawny announce | Plugin zostaje zarejestrowany, katalog logów i kolejka są utworzone |
| REG-02 | Plugin poza allowlistą | Announce trafia do DLQ |
| REG-03 | Niepoprawny `pluginId`, w tym `.`, `..`, spacje i długość > 64 | Announce trafia do DLQ; nie powstaje ścieżka poza katalogiem bazowym |
| REG-04 | Niepoprawny `fieldMapping` | Announce trafia do DLQ |
| REG-05 | Uszkodzony JSON announce | Announce trafia do DLQ bez awarii consumera |
| REG-06 | Powtórny announce tego samego pluginu | Stara rejestracja jest poprawnie zastąpiona i zwolniona |

## 3. Przetwarzanie logów

| ID | Scenariusz | Oczekiwany rezultat |
|---|---|---|
| LOG-01 | Poprawny JSON logu | Rekord trafia do HobiaryLogDB |
| LOG-02 | Niestandardowy `fieldMapping` | Pola są zapisane zgodnie z mapowaniem |
| LOG-03 | Nieznany plugin w routing key | Wiadomość zostaje odrzucona do DLQ |
| LOG-04 | Brak wymaganego pola | Wiadomość zostaje odrzucona; rośnie licznik schema-invalid |
| LOG-05 | Poziom poniżej `PluginMinLevels` | Rekord jest ACKowany bez zapisu |
| LOG-06 | Przekroczony `PluginRateLimits` | Rekord jest ACKowany bez zapisu; rośnie licznik rate-limited |
| LOG-07 | Heartbeat | Status pluginu jest aktualizowany, ale rekord nie trafia do bazy |
| LOG-08 | Deregister | Plugin jest oznaczony jako wyrejestrowany i emitowane jest zdarzenie SSE |
| LOG-09 | Niepoprawny payload JSON | Wiadomość jest odrzucona bez przerwania pracy consumera |

## 4. Szyfrowanie i kompresja

| ID | Scenariusz | Oczekiwany rezultat |
|---|---|---|
| SEC-01 | Poprawny AES-256-GCM i znany `KeyId` | Payload jest odszyfrowany i zapisany |
| SEC-02 | Nieznany `KeyId` | Wiadomość jest odrzucona bez requeue |
| SEC-03 | Payload zaszyfrowany przy wyłączonym szyfrowaniu | Wiadomość jest odrzucona |
| SEC-04 | Payload niezaszyfrowany przy wymaganym szyfrowaniu | Wiadomość jest odrzucona |
| SEC-05 | Poprawny gzip | Payload jest zdekompresowany i zapisany |
| SEC-06 | Uszkodzony gzip | Wiadomość jest odrzucona bez awarii consumera |
| SEC-07 | Gzip razem z szyfrowaniem | Dekompresja i deszyfrowanie zachodzą w poprawnej kolejności |

## 5. DLQ i retry

| ID | Scenariusz | Oczekiwany rezultat |
|---|---|---|
| DLQ-01 | `retryCount < MaxDlqRetries` | Wiadomość jest publikowana ponownie z podwyższonym licznikiem |
| DLQ-02 | `retryCount >= MaxDlqRetries` | Wiadomość jest parkowana |
| DLQ-03 | `MaxDlqRetries = 0` | Retry jest wyłączony; wiadomość jest parkowana |
| DLQ-04 | Brak nagłówka `x-death` | Używane są wartości domyślne bez wyjątku |
| DLQ-05 | Retry zachowuje routing key, payload i nagłówki | Ponowna wiadomość może zostać poprawnie obsłużona |
| DLQ-06 | Błąd publikacji retry | Consumer DLQ kończy obsługę kontrolowanym błędem |

## 6. Reconnect i zatrzymanie

| ID | Scenariusz | Oczekiwany rezultat |
|---|---|---|
| CON-01 | Utrata połączenia RabbitMQ | Startuje reconnect z backoffem |
| CON-02 | Kilka równoległych shutdown eventów | Wykonywana jest tylko jedna próba reconnectu |
| CON-03 | Reconnect kończy się sukcesem | Exchange, consumerzy, bindingi i kolejki pluginów są odtworzone |
| CON-04 | Kolejne reconnecty kończą się błędem | Backoff rośnie, ale nie przekracza maksimum |
| CON-05 | `StopAsync` podczas backoffu | Reconnect zostaje anulowany; nowe połączenie nie powstaje |
| CON-06 | Zamknięcie adaptera | Batch writerzy są opróżnieni, kanały i połączenie są zamknięte raz |
| CON-07 | Health check w trakcie reconnectu | Status jest `Degraded`, a po sukcesie wraca do `Healthy` |

## 7. Batch ACK

| ID | Scenariusz | Oczekiwany rezultat |
|---|---|---|
| ACK-01 | `BatchAckSize = 0` | ACK per wiadomość |
| ACK-02 | Osiągnięcie progu batcha | Wysyłany jest MultiAck |
| ACK-03 | Oczekujący batch bez kolejnej wiadomości | Po `BatchAckTimeoutMs` wysyłany jest MultiAck |
| ACK-04 | Osiągnięcie progu przed timeoutem | Timer jest anulowany; nie ma podwójnego ACK |
| ACK-05 | Reconnect lub stop z oczekującym batchem | Timer jest anulowany, stan jest wyzerowany, kanał nie jest używany po zamknięciu |
| ACK-06 | Równoległy callback wiadomości i timer | Nie występuje podwójny ACK ani wyścig na kanale |

## 8. Tryb brokerless i HlogInspect

| ID | Scenariusz | Oczekiwany rezultat |
|---|---|---|
| DIR-01 | Direct mode bez RabbitMQ | Logi są zapisywane lokalnie |
| DIR-02 | `PluginId = .` lub `..` | Start jest odrzucony |
| DIR-03 | Włączone gzip/szyfrowanie w Direct mode | Start kończy się jednoznacznym błędem, zamiast cichego ignorowania opcji |
| DIR-04 | Brokerless demo | Demo startuje, zapisuje logi i odczytuje rekordy |
| DIR-05 | Brokerless web demo | Endpointy logów recent/view działają |
| INS-01 | HlogInspect `table`, `json`, `ndjson`, `csv` | Eksporty mają poprawny format |
| INS-02 | Filtry source/service/trace/correlation/contains | Wynik zawiera tylko pasujące rekordy |
| INS-03 | Manifest, summary, split output | Pliki i metadane są spójne |
| INS-04 | Niepoprawne opcje CLI | Program zwraca kod 1 i podpowiada `--help` |

## 9. API i obserwowalność

| ID | Scenariusz | Oczekiwany rezultat |
|---|---|---|
| API-01 | Query API z filtrami source/service/trace/correlation | Zwracane są tylko pasujące rekordy |
| API-02 | Paging, cursor, offset i limit | Strony są stabilne i bez duplikatów |
| API-03 | Eksport CSV/NDJSON | Eksport nie gubi ani nie uszkadza rekordów |
| API-04 | SSE live feed | Nowe rekordy i zdarzenia offline/deregister są emitowane |
| API-05 | Metryki | Liczniki rejected, written, DLQ, reconnect, filtered i ACK są spójne |
| API-06 | Health endpoint | Nie ujawnia haseł, kluczy ani ścieżek sekretów |

## 10. Wydajność

Uruchom baseline:

```powershell
dotnet run --project tools/HobiaryLogPipe.Performance/HobiaryLogPipe.Performance.csproj -- `
  --iterations 100000 `
  --warmup 10000 `
  --batch-size 1000
```

Zapisz dla wersji:

- `LogMessageProcessor` ops/s,
- p50/p95 latency,
- `BatchAckTracker` ops/s,
- konfigurację sprzętu, systemu i trybu builda.

Harness jest pomiarem porównawczym, nie testem pass/fail. Porównuj wyniki na tej samej maszynie i z tym samym payloadem.

## Kryteria wydania

Wersję można oznaczyć jako gotową, gdy:

- `dotnet build HobiaryLogPipe.slnx` przechodzi,
- pełny `dotnet test HobiaryLogPipe.slnx` przechodzi,
- scenariusze RabbitMQ przechodzą przy działającym brokerze,
- scenariusze Direct/brokerless przechodzą bez RabbitMQ,
- reconnect, DLQ retry i batch ACK są sprawdzone,
- testy bezpieczeństwa nie wykazują obejścia allowlisty ani traversal ścieżek,
- baseline wydajności został zapisany,
- `git status --short` jest pusty.
