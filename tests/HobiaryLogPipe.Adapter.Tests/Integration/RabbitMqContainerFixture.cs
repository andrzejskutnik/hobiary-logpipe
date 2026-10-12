namespace HobiaryLogPipe.Adapter.Tests.Integration;

/// <summary>
/// Fixture wskazująca na istniejący serwer RabbitMQ.
/// Parametry konfigurowane przez zmienne środowiskowe:
///   RABBIT_HOST  (domyślnie: localhost)
///   RABBIT_PORT  (domyślnie: 5672)
///   RABBIT_USER  (domyślnie: guest)
///   RABBIT_PASS  (domyślnie: guest)
/// </summary>
public sealed class RabbitMqContainerFixture : IAsyncLifetime
{
    public string Hostname { get; } =
        Environment.GetEnvironmentVariable("RABBIT_HOST") ?? "localhost";

    public int Port { get; } =
        int.TryParse(Environment.GetEnvironmentVariable("RABBIT_PORT"), out var p) ? p : 5672;

    public string Username { get; } =
        Environment.GetEnvironmentVariable("RABBIT_USER") ?? "guest";

    public string Password { get; } =
        Environment.GetEnvironmentVariable("RABBIT_PASS") ?? "guest";

    public Task InitializeAsync() => Task.CompletedTask;
    public Task DisposeAsync() => Task.CompletedTask;
}
