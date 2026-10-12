# Deployment Adaptera

Ta strona zbiera minimalny zestaw wymagań i kroków potrzebnych do uruchomienia Adaptera na nowym środowisku.

## Wymagania środowiskowe

- .NET 10 Runtime lub SDK,
- dostępny broker RabbitMQ,
- biblioteka `hobiarylogdb.dll` albo `libhobiarylogdb.so`,
- katalog zapisu wskazany w `HobiaryLogPipe.BaseLogDirectory`,
- poprawna konfiguracja `AllowedPlugins` i poświadczeń RabbitMQ.

## Minimalna checklista przed startem

1. Skopiuj wzorzec z `config/appsettings.example.json` do właściwego `appsettings.json`.
2. Uzupełnij `BaseLogDirectory`, `AllowedPlugins` oraz sekcję `RabbitMQ`.
3. Jeśli środowisko tego wymaga, ustaw `HttpApiKey`.
4. Zweryfikuj, że proces ma prawa zapisu do katalogu logów.
5. Potwierdź, że natywna biblioteka HobiaryLogDB jest dostępna dla procesu Adaptera.

## Uruchomienie lokalne

```powershell
dotnet build HobiaryLogPipe.slnx
dotnet run --project src/HobiaryLogPipe.Adapter
```

## Walidacja po starcie

- `GET /health` odpowiada statusem zdrowym,
- `GET /api/plugins` zwraca oczekiwane pluginy po ich rejestracji,
- `GET /metrics` pokazuje ruch i brak nieoczekiwanych odrzuceń,
- dashboard pod `/dashboard` ładuje się i aktualizuje live feed.

## Typowe ryzyka wdrożeniowe

- brak biblioteki HobiaryLogDB w ścieżce procesu,
- niezgodny `pluginId` względem `AllowedPlugins`,
- brak prawa zapisu do katalogu logów,
- błędne poświadczenia RabbitMQ,
- brak `HttpApiKey` po stronie klienta przy chronionym HTTP API.