# Plan rozwoju — HobiaryLogPipe

> Status: propozycja kolejnej fazy po uruchomieniu brokerless path `Direct`.
> Horyzont: vNext / najbliższe 4 etapy.
> Główny kierunek: najpierw domknięcie brokerless usability i diagnostyki, potem rozszerzanie ecosystemu.

---

## 1. Cel planu

HobiaryLogPipe ma już dwa działające modele transportu:

- `RabbitMq` — pełny pipeline z brokerem, Adapterem, dashboardem i politykami bezpieczeństwa,
- `Direct` — brokerless path do lokalnego zapisu `.hlog`.

Kolejna faza nie powinna polegać na dokładaniu przypadkowych feature'ów. Priorytetem jest domknięcie czterech obszarów, które już wynikają z obecnej architektury i ostatnich prac nad brokerless flow:

1. niezależny od Adaptera odczyt `.hlog` dla wariantu `Direct`,
2. lepsza diagnostyka i observability błędów natywnych,
3. testy współbieżności i recovery dla brokerless,
4. wygodniejszy lokalny tooling do pracy z `.hlog`.

Najważniejszy wniosek: **najpierw wzmacniamy ergonomię i niezawodność `Direct`, dopiero potem rozbudowujemy kolejne integracje i UI**.

---

## 2. Kierunek strategiczny

### 2.1 Model produktu na najbliższą fazę

Rekomendowany kierunek to **dual-path, ale brokerless-first dla vNext**:

- `RabbitMq` pozostaje wariantem operatorskim i wieloźródłowym,
- `Direct` dojrzewa do roli pełnoprawnego wariantu single-host i embedded,
- warstwa odczytu `.hlog` przestaje być ukryta w kodzie Adaptera,
- sample i dokumentacja mają działać jako realne reference implementations, a nie tylko demonstracje API.

### 2.2 Dlaczego taka kolejność

Ostatnie prace pokazały, że największe ryzyka nie są już w samym "czy zapis działa", tylko w:

- diagnozie problemów natywnych (`hlog_append`, DLL/SO, katalog wyjściowy),
- współbieżności lokalnych zapisów,
- wygodnym odczycie danych bez zależności od Adaptera,
- jakości codziennego UX dla dewelopera.

Jeżeli ten etap zostanie pominięty, każde kolejne rozszerzenie `Direct` będzie mnożyć ukryty koszt utrzymania.

---

## 3. Roadmap vNext

### Etap 1 — brokerless query/read package

Cel: wydzielić jawny, lekki komponent odczytu `.hlog`, którego można używać bez referencji do całego Adaptera.

Zakres:

- wydzielenie query/read API z `HobiaryLogPipe.Adapter.HobiaryLog` do osobnego projektu lub biblioteki współdzielonej,
- zachowanie prostego kontraktu `TryCreate`, `QueryRange`, `CountByLevel`,
- aktualizacja sample brokerless, żeby nie referencjonowały Adaptera tylko po to, by czytać `.hlog`,
- aktualizacja dokumentacji i cookbooków o nową ścieżkę odczytu.

Definition of Done:

- `BrokerlessDemo` i `BrokerlessWebDemo` czytają `.hlog` bez referencji do projektu Adaptera,
- publiczna nazwa i zakres API są spójne z użyciem brokerless,
- dokumentacja pokazuje nową ścieżkę odczytu jako domyślną dla `Direct`.

Ryzyka:

- duplikacja kodu P/Invoke lub rozmycie odpowiedzialności między projektem odczytu a Adapterem,
- zbyt szerokie API ujawnione publicznie bez stabilnego kontraktu.

### Etap 2 — diagnostyka native write errors

Cel: zamienić nieczytelne błędy niskiego poziomu na komunikaty używalne operacyjnie.

Zakres:

- mapowanie kodów błędów `hlog_append` / `hlog_open` na czytelne wyjątki i komunikaty,
- dodanie self-checków dla `Direct`: resolved output directory, obecność DLL/SO, możliwość utworzenia katalogu,
- poprawa logów startupowych dla `InputPlugin` w trybie `Direct`,
- dopisanie troubleshooting opartych o realne symptomy z demo i testów.

Definition of Done:

- `hlog_append failed with code -1` nie jest już końcowym komunikatem dla użytkownika,
- sample brokerless pokazują zrozumiały błąd bez zaglądania do natywnego kodu,
- dokumentacja zawiera mapę symptom -> diagnoza -> pierwszy krok naprawczy.

Ryzyka:

- część kodów błędów może wymagać zmian po stronie `hobiary-logdb`,
- zbyt agresywne opakowanie wyjątków może ukryć potrzebny szczegół diagnostyczny.

### Etap 3 — testy współbieżności i recovery dla `Direct`

Cel: formalnie zabezpieczyć ścieżkę brokerless przed regresjami, które już raz wyszły przy równoległych zapisach.

Zakres:

- test współbieżnych zapisów `ILogger` + `ILogPipeWriter`,
- test restart/reopen po poprawnym zamknięciu procesu,
- test crash/recovery dla nieczystego zakończenia podczas zapisu,
- testy regresyjne dla odczytu demo-focused records i readback po launcherze.

Definition of Done:

- istnieje co najmniej jeden test, który wcześniej łapałby race z `DirectLogPipeTransport`,
- brokerless sample mają powtarzalny readback po wielu uruchomieniach,
- ścieżka `Direct` ma wyraźne pokrycie testami integracyjnymi, a nie tylko unit testami.

Ryzyka:

- testy recovery mogą zależeć od zachowania natywnej biblioteki i środowiska testowego,
- część scenariuszy może wymagać wydzielenia helperów procesowych lub launcherów testowych.

### Etap 4 — lokalny tooling dla `.hlog`

Cel: dać użytkownikowi prosty viewer/inspector bez konieczności uruchamiania pełnego Adaptera.

Zakres:

- CLI `inspect` albo mały local viewer webowy dla katalogu `.hlog`,
- filtrowanie po czasie, poziomie, pluginie i limicie,
- eksport JSON/NDJSON dla lokalnych analiz,
- tryb read-only do użycia w support/debug workflow.

Definition of Done:

- użytkownik może wskazać katalog `.hlog` i odczytać rekordy bez pisania własnego kodu,
- dokumentacja ma gotowy lokalny workflow „open logs -> filter -> export”,
- tooling działa zarówno dla demo brokerless, jak i dla katalogów produkowanych przez Adapter.

Ryzyka:

- zbyt ambitny UI może odciągnąć fokus od właściwego API odczytu,
- brak jasnego rozdziału między tool-em developerskim a przyszłym produktem operatorskim.

---

## 4. Zależności między etapami

1. Etap 1 powinien wejść przed Etapem 4, bo viewer/CLI powinny używać docelowego query/read API.
2. Etap 2 można prowadzić równolegle z Etapem 1, ale jego pełna wartość rośnie po ustabilizowaniu publicznego API odczytu.
3. Etap 3 powinien zacząć się najpóźniej razem z końcówką Etapu 2, żeby nie utrwalać nieprzetestowanych zmian diagnostycznych i transportowych.
4. Etap 4 powinien wejść po ustaleniu stabilnego zachowania odczytu i błędów.

---

## 5. Proponowany podział na sprinty

### Sprint 1

- projekt i implementacja wydzielenia query/read package,
- migracja `BrokerlessDemo` i `BrokerlessWebDemo` na nową bibliotekę,
- minimalna aktualizacja docs.

### Sprint 2

- poprawa diagnostyki błędów natywnych,
- self-checki startupowe i komunikaty operatorskie,
- troubleshooting oparty o rzeczywiste przypadki.

### Sprint 3

- testy współbieżności `Direct`,
- testy restart/recovery,
- twarde testy regresyjne launchera brokerless.

### Sprint 4

- CLI / local viewer dla `.hlog`,
- eksport JSON/NDJSON,
- cookbook i przewodnik użytkownika dla lokalnej analizy logów.

---

## 6. Co świadomie odkładamy

Poza najbliższy horyzont vNext wypadają na razie:

- nowe transporty pośrednie między `Direct` a `RabbitMq`,
- rozbudowany operatorski frontend dla brokerless,
- pełna unifikacja wszystkich integracji pod jeden super-kontrakt konfiguracyjny,
- głębsza rozbudowa dashboardu Adaptera niezwiązana z brokerless path.

To są dobre kandydatury na kolejną fazę, ale dziś miałyby niższy zwrot niż domknięcie czterech etapów powyżej.

---

## 7. Kryterium sukcesu całej fazy

Po zakończeniu tej roadmapy chcemy mieć sytuację, w której:

- `Direct` jest czytelny, przewidywalny i łatwy do diagnozy,
- odczyt `.hlog` nie wymaga bocznej zależności od Adaptera,
- brokerless flow ma testy na problemy, które już realnie wystąpiły,
- użytkownik może nie tylko zapisać logi lokalnie, ale też sensownie je obejrzeć i wyeksportować.
