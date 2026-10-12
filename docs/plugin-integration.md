# Integracja Pluginow

Ta strona opisuje najkrótszą ścieżkę podłączenia aplikacji do HobiaryLogPipe jako producenta logów.

## Minimalna konfiguracja pluginu

Plugin powinien znać co najmniej:

- `PluginId`,
- `Host`,
- `Username`,
- `Password`.

Przykład rejestracji:

```csharp
builder.Services.AddHobiaryLogPipe(opt =>
{
    opt.PluginId = "orders-service";
    opt.Host = "localhost";
    opt.Username = "orders-user";
    opt.Password = "haslo";
});
```

## Co dzieje się po starcie

1. Plugin publikuje komunikat rejestracyjny na `hobiary.announce`.
2. Adapter sprawdza `pluginId` względem allowlisty.
3. Logi trafiają na `hobiary.data` z routing key równym `pluginId`.
4. Adapter mapuje pola i zapisuje rekordy do HobiaryLogDB.

## Szybka walidacja integracji

- plugin pojawia się w `GET /api/plugins`,
- `GET /api/plugins/{pluginId}` pokazuje szczegóły rejestracji,
- `GET /api/logs?pluginId=...` zwraca pierwsze wpisy,
- dashboard pokazuje zdarzenia dla tego samego `pluginId`.

## Dobre praktyki

- używaj stabilnego `pluginId`, bez znaków spoza przyjętej konwencji,
- przechowuj poświadczenia RabbitMQ poza kodem aplikacji,
- jeśli potrzebujesz diagnostyki requestów, wypełniaj `traceId` i `correlationId`,
- nowe integracje opieraj o paginację `cursor`, nie o `offset`.

## Gdzie iść dalej

- szczegóły kontraktu HTTP są w `REST API`,
- diagnostyka działania w `Dashboard i Operacje`,
- pełniejsza konfiguracja adaptera w `Przewodnik Uzytkownika` i `Konfiguracja adaptera`.