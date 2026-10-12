using System.Diagnostics;
using HobiaryLogPipe.Adapter;
using HobiaryLogPipe.Contracts;
using HobiaryLogPipe.HobiaryLog;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

var options = PerformanceOptions.Parse(args);
var results = new List<PerformanceResult>
{
    await MeasureProcessorAsync(options),
    MeasureBatchAckTracker(options),
};

Console.WriteLine("HobiaryLogPipe performance results");
Console.WriteLine($"Iterations: {options.Iterations:N0}, batch size: {options.BatchSize:N0}, batches: {options.Batches:N0}");
foreach (var result in results)
{
    Console.WriteLine($"{result.Name}: {result.OperationsPerSecond:N0} ops/s, p50={result.P50Microseconds:N1} us, p95={result.P95Microseconds:N1} us");
}

static async Task<PerformanceResult> MeasureProcessorAsync(PerformanceOptions options)
{
    var directory = Path.Combine(Path.GetTempPath(), $"hobiary-performance-{Guid.NewGuid():N}");
    try
    {
        using var client = new HobiaryLogClient(directory);
        await using var writer = new BatchWriter(client, TimeSpan.FromHours(1));
        using var registry = new PluginRegistry();
        registry.Register(new PluginRegistration
        {
            PluginId = "performance-plugin",
            FieldMapping = new FieldMappingConfig(),
            Client = client,
            BatchWriter = writer,
        });

        using var processor = new LogMessageProcessor(
            NullLogger<LogMessageProcessor>.Instance,
            registry,
            new PipeMetrics(),
            new SseHub(),
            new AdapterOptions());
        var payload = System.Text.Encoding.UTF8.GetBytes(
            "{\"level\":\"Info\",\"message\":\"performance payload\",\"source\":\"benchmark\",\"service\":\"performance\"}");
        var message = new IncomingLogMessage("performance-plugin", payload, null);

        for (var i = 0; i < options.WarmupIterations; i++)
            await processor.ProcessAsync(message);

        var samples = new List<double>(options.Batches);
        for (var batch = 0; batch < options.Batches; batch++)
        {
            var stopwatch = Stopwatch.StartNew();
            for (var i = 0; i < options.BatchSize; i++)
                await processor.ProcessAsync(message);
            stopwatch.Stop();
            samples.Add(stopwatch.Elapsed.TotalMilliseconds * 1_000 / options.BatchSize);
        }

        return PerformanceResult.FromSamples("LogMessageProcessor", samples);
    }
    finally
    {
        if (Directory.Exists(directory))
            Directory.Delete(directory, recursive: true);
    }
}

static PerformanceResult MeasureBatchAckTracker(PerformanceOptions options)
{
    var samples = new List<double>(options.Batches);
    for (var batch = 0; batch < options.Batches; batch++)
    {
        var tracker = new BatchAckTracker(options.BatchSize, timeoutMs: 200);
        var stopwatch = Stopwatch.StartNew();
        for (var i = 0; i < options.BatchSize; i++)
            tracker.Record((ulong)i, i);
        stopwatch.Stop();
        samples.Add(stopwatch.Elapsed.TotalMilliseconds * 1_000 / options.BatchSize);
    }

    return PerformanceResult.FromSamples("BatchAckTracker", samples);
}

internal sealed record PerformanceResult(
    string Name,
    double OperationsPerSecond,
    double P50Microseconds,
    double P95Microseconds)
{
    public static PerformanceResult FromSamples(string name, List<double> microsecondsPerOperation)
    {
        microsecondsPerOperation.Sort();
        var p50 = microsecondsPerOperation[(int)((microsecondsPerOperation.Count - 1) * 0.50)];
        var p95 = microsecondsPerOperation[(int)((microsecondsPerOperation.Count - 1) * 0.95)];
        var average = microsecondsPerOperation.Average();
        return new PerformanceResult(name, 1_000_000 / average, p50, p95);
    }
}

internal sealed record PerformanceOptions(
    int Iterations,
    int WarmupIterations,
    int BatchSize,
    int Batches)
{
    public static PerformanceOptions Parse(string[] args)
    {
        var iterations = ReadInt(args, "--iterations", 100_000);
        var warmup = ReadInt(args, "--warmup", Math.Min(iterations / 10, 10_000));
        var batchSize = ReadInt(args, "--batch-size", 1_000);
        var batches = Math.Max(1, (int)Math.Ceiling(iterations / (double)batchSize));
        return new PerformanceOptions(iterations, warmup, batchSize, batches);
    }

    private static int ReadInt(string[] args, string name, int fallback)
    {
        var index = Array.IndexOf(args, name);
        return index >= 0 && index + 1 < args.Length && int.TryParse(args[index + 1], out var value) && value > 0
            ? value
            : fallback;
    }
}
