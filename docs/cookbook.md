# Cookbook

Ta strona zbiera krótkie przepisy na najczęstsze zadania bez przeskakiwania między kilkoma dokumentami. Sekcje są pogrupowane per workflow i per framework integracyjnym.

## 1. Uruchomienie lokalne w 5 minut

1. Skonfiguruj RabbitMQ i exchange `hobiary.announce` oraz `hobiary.data`.
2. Skopiuj `config/appsettings.example.json` do `appsettings.json`.
3. Uzupełnij `BaseLogDirectory`, `AllowedPlugins` i `RabbitMQ`.
4. Uruchom:

```powershell
dotnet build HobiaryLogPipe.slnx
dotnet run --project src/HobiaryLogPipe.Adapter
```

5. Otwórz `/health`, potem `/dashboard`.

## 1a. Lokalny smoke flow z `DemoApp`

Jeśli chcesz zweryfikować Adapter na realnym producencie zamiast minimalnego `SmokePublisher`, użyj `samples/DemoApp`, który przechodzi przez `Serilog`, `MicrosoftLogging` i `OpenTelemetry`.

1. Uruchom Adapter z tymczasowo rozszerzoną allowlistą tylko w bieżącej sesji terminala:

```powershell
$env:DOTNET_ENVIRONMENT = 'Development'
$env:HobiaryLogPipe__AllowedPlugins__0 = 'orders-service'
$env:HobiaryLogPipe__AllowedPlugins__1 = 'demo-serilog'
$env:HobiaryLogPipe__AllowedPlugins__2 = 'demo-mel'
$env:HobiaryLogPipe__AllowedPlugins__3 = 'demo-otel'
dotnet run --project .\src\HobiaryLogPipe.Adapter
```

2. W drugim terminalu uruchom producenta:

```powershell
$env:HOBIARY_HOLD_SECONDS = '20'
dotnet run --project .\samples\DemoApp\DemoApp.csproj
```

3. Smoke test najwygodniej wykonaj dla `demo-mel`:

```powershell
pwsh -File .\tools\platform-demo-smoke.ps1 \
    -ApiBaseUrl "http://localhost:5050" \
    -PluginId "demo-mel" \
    -ApiKey "$env:HobiaryLogPipe__HttpApiKey" \
    -LogDirectory ".\src\HobiaryLogPipe.Adapter\logs-dev"
```

To jest najkrótszy wariant, jeśli chcesz sprawdzić nie tylko sam transport `InputPlugin`, ale też rzeczywiste integracje bibliotek pluginowych.

## 2. Podłączenie nowego pluginu

```csharp
builder.Services.AddHobiaryLogPipe(opt =>
{
    opt.PluginId = "orders-service";
    opt.Host = "localhost";
    opt.Username = "orders-user";
    opt.Password = "haslo";
});
```

Po starcie sprawdź:

- `GET /api/plugins`
- `GET /api/plugins/orders-service`
- `GET /api/logs?pluginId=orders-service`

## 2a. Serilog

Jeśli aplikacja już używa Serilog, najprostsza ścieżka to użycie integracji `HobiaryLogPipe.Serilog`.

```csharp
Log.Logger = new LoggerConfiguration()
    .WriteTo.HobiaryLogPipe(options =>
    {
        options.PluginId = "orders-service";
        options.Host = "localhost";
        options.Username = "orders-user";
        options.Password = "haslo";
    })
    .CreateLogger();
```

Po starcie zweryfikuj `GET /api/plugins/orders-service` i dashboard.

## 2b. Microsoft Logging

Jeśli aplikacja pracuje na `ILogger`, użyj providera `HobiaryLogPipe.MicrosoftLogging`.

```csharp
builder.Logging.AddHobiaryLogPipe(options =>
{
    options.PluginId = "orders-service";
    options.Host = "localhost";
    options.Username = "orders-user";
    options.Password = "haslo";
});
```

To jest dobry wariant dla workerów, usług ASP.NET Core i aplikacji opartych o standardowy pipeline `ILogger`.

## 2c. OpenTelemetry

Jeśli logi i ślady są już w OpenTelemetry, użyj eksportera `HobiaryLogPipe.OpenTelemetry`.

```csharp
builder.Logging.AddOpenTelemetry(logging =>
{
    logging.AddHobiaryLogPipeExporter(options =>
    {
        options.PluginId = "orders-service";
        options.Host = "localhost";
        options.Username = "orders-user";
        options.Password = "haslo";
    });
});
```

To podejście ma sens szczególnie wtedy, gdy chcesz zachować `traceId` i `correlationId` spójne z resztą telemetry.

## 3. Szybka diagnoza: dashboard nie pokazuje danych

1. `GET /health`
2. `GET /api/plugins`
3. `GET /metrics`
4. `GET /api/logs?pluginId=...`
5. `GET /api/logs/stream/{pluginId}`

Jeśli dane są w API, a nie ma ich w UI, problem jest po stronie dashboardu lub przeglądarki.

## 4. Eksport fragmentu logów do NDJSON

```http
GET /api/plugins/orders-service/logs/export?cursor=1714555200000000&limit=100
```

Sprawdź nagłówki:

- `X-Export-Count`
- `X-Has-More`
- `X-Next-Cursor`

Jeśli `X-Has-More=true`, pobierz kolejną porcję z `X-Next-Cursor`.

## 5. Publikacja dokumentacji dla obecnego hostingu

Zbuduj i spakuj statyczne docs:

```powershell
.\tools\package-docs.ps1
```

Linux/macOS:

```bash
./tools/package-docs.sh
```

Wynik trafia do `artifacts/hobiary-logpipe-docs.zip` i może zostać rozpakowany na dowolnym serwerze statycznym.

## 6. Kiedy używać której strony dokumentacji

- `REST API` gdy potrzebujesz kontraktu endpointów,
- `Dashboard i Operacje` gdy diagnozujesz runtime,
- `Troubleshooting` gdy znasz objaw, ale nie przyczynę,
- `Deployment Adaptera` gdy uruchamiasz nowe środowisko,
- `Integracja Pluginow` gdy podłączasz producenta logów.

## 7. Autoryzacja HTTP API z klienta serwerowego

Jeśli endpointy są chronione `HttpApiKey`, obsługiwane są cztery warianty:

```http
Authorization: ApiKey secret-key
Authorization: Bearer secret-key
X-Api-Key: secret-key
?apiKey=secret-key
```

`Bearer` pozwala używać standardowych bibliotek HTTP (np. `HttpClient.DefaultRequestHeaders.Authorization`) bez dodatkowej konfiguracji:

```csharp
httpClient.DefaultRequestHeaders.Authorization =
    new AuthenticationHeaderValue("Bearer", apiKey);
```

## 8. Integracja z ASP.NET Core WebAPI (Minimal API)

### Scenariusz: wywoływanie REST API adaptera z aplikacji ASP.NET Core

Jeśli masz aplikację ASP.NET Core i chcesz odpytywać adapter HobiaryLogPipe (np. wyświetlać logi w panelu administracyjnym), możesz zarejestrować dedykowany `HttpClient`:

```csharp
// Program.cs
builder.Services.AddHttpClient("hobiary", client =>
{
    client.BaseAddress = new Uri(builder.Configuration["Hobiary:AdapterUrl"]!);
    var apiKey = builder.Configuration["Hobiary:ApiKey"];
    if (!string.IsNullOrEmpty(apiKey))
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", apiKey);
});
```

```json
// appsettings.json
{
  "Hobiary": {
    "AdapterUrl": "http://localhost:5200",
    "ApiKey": "secret-key"
  }
}
```

### Scenariusz: odpytywanie logów z kontrolera

```csharp
[ApiController]
[Route("admin/logs")]
public sealed class LogsController(IHttpClientFactory factory) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> GetLogs(
        [FromQuery] string pluginId,
        [FromQuery] string? source,
        [FromQuery] string? service,
        [FromQuery] string? level,
        CancellationToken ct)
    {
        var client = factory.CreateClient("hobiary");
        var url = $"/api/logs?pluginId={Uri.EscapeDataString(pluginId)}&limit=100";
        if (!string.IsNullOrEmpty(source))
            url += $"&source={Uri.EscapeDataString(source)}";
        if (!string.IsNullOrEmpty(service))
            url += $"&service={Uri.EscapeDataString(service)}";
        if (!string.IsNullOrEmpty(level))
            url += $"&level={Uri.EscapeDataString(level)}";

        var response = await client.GetAsync(url, ct);
        if (!response.IsSuccessStatusCode)
            return StatusCode((int)response.StatusCode);

        var content = await response.Content.ReadAsStringAsync(ct);
        return Content(content, "application/json");
    }
}
```

### Scenariusz: forward kontekstu trace z Minimal API

Jeśli używasz OpenTelemetry, `traceId` i `correlationId` są automatycznie zapisywane w rekordach.  
Aby je odczytać przy zapytaniu:

```csharp
app.MapGet("/api/trace/{traceId}", async (
    string traceId,
    IHttpClientFactory factory,
    CancellationToken ct) =>
{
    var client = factory.CreateClient("hobiary");
    // Pobierz logi danej operacji na podstawie zakresu czasu i źródła
    var logs = await client.GetFromJsonAsync<LogEntry[]>(
        $"/api/logs?pluginId=my-service&limit=200", ct);

    var traceOnly = logs?.Where(l => l.TraceId == traceId).ToArray()
                    ?? [];
    return Results.Ok(traceOnly);
});
```

> Filtrowanie po `traceId` odbywa się po stronie klienta — adapter zwraca logi per zakres czasu i poziom, a nie bezpośrednio po `traceId`.

### Scenariusz: filtrowanie po source i service

Adapter obsługuje parametry `source` i `service` bezpośrednio na `/api/logs`:

```http
GET /api/logs?pluginId=orders-service&source=OrdersRepository&service=api-gateway&level=Warn
```

Dzięki temu można ograniczyć wyniki do konkretnej klasy lub instancji serwisu bez pobierania wszystkich rekordów.