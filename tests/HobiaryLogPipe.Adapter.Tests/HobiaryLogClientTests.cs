using HobiaryLogPipe.HobiaryLog;

namespace HobiaryLogPipe.Adapter.Tests;

public sealed class HobiaryLogClientTests
{
    [Fact]
    public void TryCreate_WithInvalidPath_ReturnsDetailedDiagnosticMessage()
    {
        var dir = CreateInvalidPath();

        var result = HobiaryLogClient.TryCreate(dir, out var client, out var error);

        Assert.False(result);
        Assert.Null(client);
        Assert.NotNull(error);
        Assert.Contains("Nie można przygotować katalogu logu", error);
        Assert.Contains("Sprawdź poprawność ścieżki", error);
    }

    [Fact]
    public void TryCreate_ReturnsConsistentState()
    {
        // TryCreate używa tymczasowego katalogu.
        // W środowisku testowym bez DLL: result=false, error != null, client == null.
        // W środowisku z DLL (CI/CD z hobiarylogdb.dll): result=true, client != null.
        var dir = Path.Combine(Path.GetTempPath(), $"hlog_test_{Guid.NewGuid()}");

        var result = HobiaryLogClient.TryCreate(dir, out var client, out var error);

        if (result)
        {
            Assert.NotNull(client);
            Assert.Null(error);
            client!.Dispose();
        }
        else
        {
            Assert.Null(client);
            Assert.NotNull(error);
            Assert.NotEmpty(error!);
        }
    }

    [Fact]
    public void TryCreate_WhenSuccessful_ClientIsDisposable()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"hlog_test_{Guid.NewGuid()}");
        var ok = HobiaryLogClient.TryCreate(dir, out var client, out _);

        if (ok)
        {
            // Dispose nie rzuca
            client!.Dispose();
            // Podwójny Dispose też nie rzuca
            client.Dispose();
        }
        else
        {
            // DLL niedostępna w tym środowisku — test pominięty przez brak klienta
            Assert.Null(client);
        }
    }

    [Fact]
    public void BuildAppendFailureMessage_ContainsCodeDirectoryAndPayloadSize()
    {
        var message = HobiaryLogDiagnostics.BuildAppendFailureMessage(-1, "logs-direct", 321);

        Assert.Contains("hlog_append zwróciło kod -1", message);
        Assert.Contains("logs-direct", message);
        Assert.Contains("Payload bytes: 321", message);
    }

    private static string CreateInvalidPath()
    {
        var invalidChar = Path.GetInvalidPathChars()[0];
        return Path.Combine(Path.GetTempPath(), $"hlog_test_{invalidChar}_{Guid.NewGuid()}");
    }
}
