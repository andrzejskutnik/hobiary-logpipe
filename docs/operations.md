# Dashboard i Operacje

Ta strona zbiera praktyczne informacje o pracy z wbudowanym dashboardem, endpointami strumieniowymi i podstawową diagnostyką Adaptera.

## Dashboard

Dashboard jest dostępny pod endpointem:

```text
GET /dashboard
```

Jeżeli ustawisz `HobiaryLogPipe.HttpApiKey`, otwieraj dashboard jako:

```text
/dashboard?apiKey=...
```

Alternatywnie użyj nagłówka `X-Api-Key` przy wywołaniach API.

Dla klientów serwer-serwer możesz też użyć `Authorization: ApiKey <key>` lub `Authorization: Bearer <key>`.

## Co pokazuje dashboard

- listę zarejestrowanych pluginów,
- statystyki logów per poziom,
- historię logów z paginacją opartą o `cursor`,
- eksport NDJSON dla aktualnego zakresu,
- stan reconnectów SSE.

Dashboard nie ma osobnego backendu pośredniczącego. Korzysta bezpośrednio z istniejących endpointów API, więc każde zachowanie można odtworzyć ręcznie klientem HTTP.

## SSE

Do podglądu zdarzeń na żywo dostępne są dwa endpointy:

| Endpoint | Zastosowanie |
|---|---|
| `GET /api/logs/stream` | wszystkie zdarzenia |
| `GET /api/logs/stream/{pluginId}` | tylko wybrany plugin |

SSE jest wykorzystywane przez dashboard do live feedu i sygnalizacji zmian statusu pluginów.

## Typowy przepływ diagnostyczny

1. Otwórz `/dashboard` i sprawdź, czy plugin jest widoczny jako zarejestrowany.
2. Zweryfikuj `stats`, aby upewnić się, że adapter zapisuje logi w oczekiwanym zakresie czasu.
3. Sprawdź `/api/logs` z `pluginId`, `from`, `to` i `level`, gdy potrzebujesz odtworzyć problem bez UI.
4. Użyj `/api/logs/stream` albo `/api/logs/stream/{pluginId}`, gdy problem dotyczy zdarzeń bieżących.
5. Użyj eksportu NDJSON, gdy potrzebujesz przekazać wycinek logów dalej do analizy.

## Szybka checklista operacyjna

- plugin jest widoczny na liście `/api/plugins`,
- `/health` nie zgłasza degradacji zależności,
- `/metrics` pokazuje wzrost liczników zgodny z ruchem,
- dashboard aktualizuje live feed bez ciągłego reconnectu,
- `/api/logs` zwraca dane dla tego samego `pluginId`, który widzisz w dashboardzie.

## Endpointy pomocnicze operacyjnie

| Endpoint | Do czego służy |
|---|---|
| `GET /health` | szybki stan zależności Adaptera |
| `GET /metrics` | liczniki techniczne i operacyjne |
| `GET /api/plugins` | aktualny obraz rejestracji pluginów |
| `GET /api/plugins/{pluginId}/stats` | agregaty logów dla pluginu |

## Uwagi praktyczne

- Preferowany mechanizm paginacji to `cursor`; `offset` pozostał tylko dla klientów legacy.
- Jeśli dashboard nie pokazuje danych, najpierw sprawdź klucz `HttpApiKey` i poprawność `pluginId`.
- Gdy SSE się rozłącza, dashboard pokazuje stan reconnectu, ale sam problem najłatwiej odtworzyć bezpośrednio na endpointach `/api/logs/stream*`.

!!! tip "Najtańsza diagnoza"
	Jeśli nie masz pewności, czy problem dotyczy UI czy backendu, pomiń dashboard i odtwórz ten sam scenariusz przez `/api/plugins`, `/api/logs` i `/api/logs/stream`. To najszybciej rozdziela problem prezentacji od problemu danych.