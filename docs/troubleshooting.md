# Troubleshooting

Ta strona zbiera najczęstsze problemy operacyjne i najtańsze kroki, które szybko rozdzielają problem konfiguracji od problemu danych.

## 1. Plugin nie pojawia się w `/api/plugins`

Najczęstsze przyczyny:

- `pluginId` nie jest na allowliście `AllowedPlugins`,
- plugin nie może połączyć się z RabbitMQ,
- komunikat rejestracyjny nie dochodzi do `hobiary.announce`.

Najkrótsza diagnoza:

1. Sprawdź konfigurację `AllowedPlugins`.
2. Zweryfikuj poświadczenia RabbitMQ po stronie pluginu.
3. Otwórz `/health` i `/metrics`.
4. Sprawdź, czy rośnie licznik odebranych wiadomości.

## 2. Dashboard działa, ale nie ma logów

Najczęstsze przyczyny:

- błędny `pluginId` w filtrze,
- zbyt wąski zakres czasu,
- logi są odrzucane przez walidację schematu albo rate limiting,
- plugin wysyła dane w innym mapowaniu niż oczekuje Adapter.

Najkrótsza diagnoza:

1. Wywołaj `/api/logs?pluginId=...` ręcznie.
2. Poszerz zakres `from` i `to`.
3. Sprawdź `/metrics`, czy rosną liczniki `rejected`, `filtered` albo `schema invalid`.
4. Zweryfikuj mapowanie pól i `pluginId` w `/api/plugins/{pluginId}`.

## 3. `401 Unauthorized` na `/dashboard` albo `/api/*`

Najczęstsza przyczyna to ustawione `HobiaryLogPipe.HttpApiKey` bez przekazania klucza przez klienta.

Najkrótsza diagnoza:

- użyj `X-Api-Key`, `Authorization: ApiKey <key>`, `Authorization: Bearer <key>`, albo
- otwórz dashboard przez `/dashboard?apiKey=...`.

Jeżeli problem nadal występuje, porównaj dokładnie wartość klucza po stronie klienta i Adaptera.

## 4. SSE ciągle się reconnectuje

Najczęstsze przyczyny:

- chwilowe problemy sieciowe,
- błędny `HttpApiKey`,
- niestabilny backend HTTP,
- problem jest po stronie dashboardu, a nie strumienia danych.

Najkrótsza diagnoza:

1. Pomiń dashboard.
2. Otwórz bezpośrednio `/api/logs/stream` albo `/api/logs/stream/{pluginId}`.
3. Sprawdź, czy reconnect występuje także bez UI.

To najszybciej rozdziela problem frontendowy od problemu HTTP/SSE.

## 5. Adapter nie zapisuje do HobiaryLogDB

Najczęstsze przyczyny:

- brak `hobiarylogdb.dll` albo `libhobiarylogdb.so`,
- brak prawa zapisu do katalogu logów,
- niepoprawna ścieżka `BaseLogDirectory`.

Najkrótsza diagnoza:

1. Zweryfikuj obecność biblioteki natywnej.
2. Sprawdź prawa zapisu do katalogu.
3. Upewnij się, że katalog bazowy istnieje albo może zostać utworzony.

## 6. `invalid_plugin_id` albo `plugin_not_found`

To są dwa różne problemy:

- `invalid_plugin_id` oznacza zły format identyfikatora,
- `plugin_not_found` oznacza poprawny format, ale brak znanego pluginu.

Najkrótsza diagnoza:

- sprawdź format `pluginId`,
- sprawdź `/api/plugins`,
- porównaj identyfikator użyty przez plugin z tym użytym w wywołaniu API.

## 7. Minimalna sekwencja diagnostyczna

Jeśli nie wiesz, od czego zacząć, idź zawsze w tej kolejności:

1. `/health`
2. `/api/plugins`
3. `/metrics`
4. `/api/logs?pluginId=...`
5. `/api/logs/stream` lub dashboard

Ta sekwencja minimalizuje błądzenie między warstwą UI, brokerem, walidacją i persystencją.