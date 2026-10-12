using System.Diagnostics.CodeAnalysis;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using HobiaryLogPipe.HobiaryLog;
using HobiaryLogPipe.Contracts;
using HobiaryLogPipe.InputPlugin;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace HobiaryLogPipe.InputPlugin.Tests;

public sealed class DirectTransportIntegrationTests
{
    [Fact]
    public async Task DirectTransport_WritesReadableHlogRecord_WhenNativeDllIsAvailable()
    {
        var rootDir = Path.Combine(Path.GetTempPath(), $"hlog_direct_it_{Guid.NewGuid():N}");
        var pluginId = "direct-it";
        var pluginDir = Path.Combine(rootDir, pluginId);
        var timestamp = DateTimeOffset.UtcNow;

        try
        {
            var options = CreateDirectOptions(rootDir, pluginId);

            await using (var writer = new LogPipeWriter(NullLogger<LogPipeWriter>.Instance, options))
            {
                await writer.StartAsync(CancellationToken.None);
                await writer.WriteAsync(new LogMessage
                {
                    Level = LogPipeLevel.Warn,
                    Message = "direct integration payload",
                    Source = "DirectTransportIntegrationTests",
                    Service = "inputplugin-tests",
                    Timestamp = timestamp,
                });
                await writer.StopAsync(CancellationToken.None);
            }

            if (!TryOpenClient(pluginDir, out var client))
                return;

            using (client)
            {
                Assert.True(Directory.Exists(pluginDir));
                Assert.NotEmpty(Directory.GetFiles(pluginDir, "*.hlog"));

                var results = QueryWindow(client, timestamp, timestamp, 10);

                var record = Assert.Single(results);
                Assert.Equal(HlogLevel.Warn, record.Level);
                Assert.Equal("DirectTransportIntegrationTests", record.Source);
                Assert.Equal("inputplugin-tests", record.Service);

                var payloadJson = GetPayloadText(record);
                Assert.Contains("direct integration payload", payloadJson);
                Assert.Contains("\"level\":\"warn\"", payloadJson);
            }
        }
        finally
        {
            if (Directory.Exists(rootDir))
                Directory.Delete(rootDir, recursive: true);
        }
    }

    [Fact]
    public async Task DirectTransport_WritesAllConcurrentRecords_WhenSingleWriterIsShared()
    {
        var rootDir = Path.Combine(Path.GetTempPath(), $"hlog_direct_concurrent_{Guid.NewGuid():N}");
        var pluginId = "direct-concurrent";
        var pluginDir = Path.Combine(rootDir, pluginId);
        var startedAt = DateTimeOffset.UtcNow;
        const int messageCount = 24;

        try
        {
            var options = CreateDirectOptions(rootDir, pluginId);

            await using (var writer = new LogPipeWriter(NullLogger<LogPipeWriter>.Instance, options))
            {
                await writer.StartAsync(CancellationToken.None);

                var writes = Enumerable.Range(0, messageCount)
                    .Select(index => Task.Run(() => writer.WriteAsync(new LogMessage
                    {
                        Level = LogPipeLevel.Info,
                        Message = $"concurrent-message-{index}",
                        Source = "DirectTransportIntegrationTests.Concurrent",
                        Service = "inputplugin-tests",
                        Timestamp = startedAt.AddMilliseconds(index),
                    })))
                    .ToArray();

                await Task.WhenAll(writes);
                await writer.StopAsync(CancellationToken.None);
            }

            if (!TryOpenClient(pluginDir, out var client))
                return;

            using (client)
            {
                var results = QueryWindow(client, startedAt, startedAt, messageCount + 10);

                Assert.Equal(messageCount, results.Count);

                var payloads = results
                    .Select(GetPayloadText)
                    .ToList();

                foreach (var index in Enumerable.Range(0, messageCount))
                    Assert.Contains(payloads, payload => payload.Contains($"concurrent-message-{index}", StringComparison.Ordinal));
            }
        }
        finally
        {
            if (Directory.Exists(rootDir))
                Directory.Delete(rootDir, recursive: true);
        }
    }

    [Fact]
    public async Task DirectTransport_PreservesRecordsAcrossWriterRestart_WhenReopenedOnSameDirectory()
    {
        var rootDir = Path.Combine(Path.GetTempPath(), $"hlog_direct_reopen_{Guid.NewGuid():N}");
        var pluginId = "direct-reopen";
        var pluginDir = Path.Combine(rootDir, pluginId);
        var firstTimestamp = DateTimeOffset.UtcNow;
        var secondTimestamp = firstTimestamp.AddSeconds(1);

        try
        {
            var options = CreateDirectOptions(rootDir, pluginId);

            await using (var writer = new LogPipeWriter(NullLogger<LogPipeWriter>.Instance, options))
            {
                await writer.StartAsync(CancellationToken.None);
                await writer.WriteAsync(new LogMessage
                {
                    Level = LogPipeLevel.Info,
                    Message = "before-restart",
                    Source = "DirectTransportIntegrationTests.Reopen",
                    Service = "inputplugin-tests",
                    Timestamp = firstTimestamp,
                });
                await writer.StopAsync(CancellationToken.None);
            }

            await using (var writer = new LogPipeWriter(NullLogger<LogPipeWriter>.Instance, options))
            {
                await writer.StartAsync(CancellationToken.None);
                await writer.WriteAsync(new LogMessage
                {
                    Level = LogPipeLevel.Warn,
                    Message = "after-restart",
                    Source = "DirectTransportIntegrationTests.Reopen",
                    Service = "inputplugin-tests",
                    Timestamp = secondTimestamp,
                });
                await writer.StopAsync(CancellationToken.None);
            }

            if (!TryOpenClient(pluginDir, out var client))
                return;

            using (client)
            {
                var results = QueryWindow(client, firstTimestamp, secondTimestamp, 10);

                Assert.Equal(2, results.Count);

                var payloads = results
                    .Select(GetPayloadText)
                    .ToList();

                Assert.Contains(payloads, payload => payload.Contains("before-restart", StringComparison.Ordinal));
                Assert.Contains(payloads, payload => payload.Contains("after-restart", StringComparison.Ordinal));
            }
        }
        finally
        {
            if (Directory.Exists(rootDir))
                Directory.Delete(rootDir, recursive: true);
        }
    }

    [Fact]
    public async Task HlogInspect_ExportsJsonForDirectLogs_WhenSampleIsInvoked()
    {
        var rootDir = Path.Combine(Path.GetTempPath(), $"hlog_inspect_it_{Guid.NewGuid():N}");
        var pluginId = "inspect-it";
        var timestamp = DateTimeOffset.UtcNow;

        try
        {
            await SeedDirectLogAsync(
                rootDir,
                pluginId,
                new LogMessage
                {
                    Level = LogPipeLevel.Error,
                    Message = "inspect integration payload",
                    Source = "DirectTransportIntegrationTests.HlogInspect",
                    Service = "inputplugin-tests",
                    Timestamp = timestamp,
                });

            var result = await RunHlogInspectAsync(rootDir, pluginId, "json");

            Assert.True(result.ExitCode == 0, $"HlogInspect failed with exit code {result.ExitCode}.{Environment.NewLine}STDERR:{Environment.NewLine}{result.StandardError}{Environment.NewLine}STDOUT:{Environment.NewLine}{result.StandardOutput}");
            Assert.Contains("inspect integration payload", result.StandardOutput, StringComparison.Ordinal);
            Assert.Contains("\"Level\": \"Error\"", result.StandardOutput, StringComparison.Ordinal);
            Assert.Contains("\"Source\": \"DirectTransportIntegrationTests.HlogInspect\"", result.StandardOutput, StringComparison.Ordinal);
            Assert.Contains("Read 1 record(s).", result.StandardError, StringComparison.Ordinal);
        }
        finally
        {
            if (Directory.Exists(rootDir))
                Directory.Delete(rootDir, recursive: true);
        }
    }

    [Fact]
    public async Task HlogInspect_ExportsNdjsonForDirectLogs_WhenSampleIsInvoked()
    {
        var rootDir = Path.Combine(Path.GetTempPath(), $"hlog_inspect_ndjson_{Guid.NewGuid():N}");
        var pluginId = "inspect-ndjson";
        var timestamp = DateTimeOffset.UtcNow;

        try
        {
            await SeedDirectLogAsync(
                rootDir,
                pluginId,
                new LogMessage
                {
                    Level = LogPipeLevel.Warn,
                    Message = "inspect ndjson payload",
                    Source = "DirectTransportIntegrationTests.HlogInspect.Ndjson",
                    Service = "inputplugin-tests",
                    Timestamp = timestamp,
                });

            var result = await RunHlogInspectAsync(rootDir, pluginId, "ndjson");

            Assert.True(result.ExitCode == 0, $"HlogInspect failed with exit code {result.ExitCode}.{Environment.NewLine}STDERR:{Environment.NewLine}{result.StandardError}{Environment.NewLine}STDOUT:{Environment.NewLine}{result.StandardOutput}");

            var lines = result.StandardOutput
                .Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

            var line = Assert.Single(lines);
            Assert.Contains("\"Level\":\"Warn\"", line, StringComparison.Ordinal);
            Assert.Contains("\"Message\":\"inspect ndjson payload\"", line, StringComparison.Ordinal);
            Assert.Contains("\"Source\":\"DirectTransportIntegrationTests.HlogInspect.Ndjson\"", line, StringComparison.Ordinal);
            Assert.Contains("Read 1 record(s).", result.StandardError, StringComparison.Ordinal);
        }
        finally
        {
            if (Directory.Exists(rootDir))
                Directory.Delete(rootDir, recursive: true);
        }
    }

    [Fact]
    public async Task HlogInspect_ExportsTableForDirectLogs_WhenSampleIsInvoked()
    {
        var rootDir = Path.Combine(Path.GetTempPath(), $"hlog_inspect_table_{Guid.NewGuid():N}");
        var pluginId = "inspect-table";
        var timestamp = DateTimeOffset.UtcNow;

        try
        {
            await SeedDirectLogAsync(
                rootDir,
                pluginId,
                new LogMessage
                {
                    Level = LogPipeLevel.Info,
                    Message = "inspect table payload",
                    Source = "DirectTransportIntegrationTests.HlogInspect.Table",
                    Service = "inputplugin-tests",
                    Timestamp = timestamp,
                });

            var result = await RunHlogInspectAsync(rootDir, pluginId, "table");

            Assert.True(result.ExitCode == 0, $"HlogInspect failed with exit code {result.ExitCode}.{Environment.NewLine}STDERR:{Environment.NewLine}{result.StandardError}{Environment.NewLine}STDOUT:{Environment.NewLine}{result.StandardOutput}");
            Assert.Contains("[", result.StandardOutput, StringComparison.Ordinal);
            Assert.Contains("Info ", result.StandardOutput, StringComparison.Ordinal);
            Assert.Contains("source=DirectTransportIntegrationTests.HlogInspect.Table", result.StandardOutput, StringComparison.Ordinal);
            Assert.Contains("service=inputplugin-tests", result.StandardOutput, StringComparison.Ordinal);
            Assert.Contains("message=inspect table payload", result.StandardOutput, StringComparison.Ordinal);
            Assert.Contains("Read 1 record(s).", result.StandardError, StringComparison.Ordinal);
        }
        finally
        {
            if (Directory.Exists(rootDir))
                Directory.Delete(rootDir, recursive: true);
        }
    }

    [Fact]
    public async Task HlogInspect_ProjectsSelectedFieldsInJson_WhenFieldsAreRequested()
    {
        var rootDir = Path.Combine(Path.GetTempPath(), $"hlog_inspect_fields_{Guid.NewGuid():N}");
        var pluginId = "inspect-fields";
        var timestamp = DateTimeOffset.UtcNow;

        try
        {
            await SeedDirectLogAsync(
                rootDir,
                pluginId,
                new LogMessage
                {
                    Level = LogPipeLevel.Info,
                    Message = "projected payload",
                    Source = "OrdersService",
                    Service = "api-gateway",
                    Timestamp = timestamp,
                });

            var result = await RunHlogInspectAsync(rootDir, pluginId, "json", fields: "timestamp,level,message");

            Assert.True(result.ExitCode == 0, $"HlogInspect failed with exit code {result.ExitCode}.{Environment.NewLine}STDERR:{Environment.NewLine}{result.StandardError}{Environment.NewLine}STDOUT:{Environment.NewLine}{result.StandardOutput}");
            Assert.Contains("\"timestamp\"", result.StandardOutput, StringComparison.Ordinal);
            Assert.Contains("\"level\"", result.StandardOutput, StringComparison.Ordinal);
            Assert.Contains("\"message\"", result.StandardOutput, StringComparison.Ordinal);
            Assert.Contains("projected payload", result.StandardOutput, StringComparison.Ordinal);
            Assert.DoesNotContain("\"source\"", result.StandardOutput, StringComparison.Ordinal);
            Assert.Contains("fields=timestamp,level,message", result.StandardError, StringComparison.Ordinal);
        }
        finally
        {
            if (Directory.Exists(rootDir))
                Directory.Delete(rootDir, recursive: true);
        }
    }

    [Fact]
    public async Task HlogInspect_ExportsCsvForDirectLogs_WhenSampleIsInvoked()
    {
        var rootDir = Path.Combine(Path.GetTempPath(), $"hlog_inspect_csv_{Guid.NewGuid():N}");
        var pluginId = "inspect-csv";
        var timestamp = DateTimeOffset.UtcNow;

        try
        {
            await SeedDirectLogAsync(
                rootDir,
                pluginId,
                new LogMessage
                {
                    Level = LogPipeLevel.Warn,
                    Message = "inspect \"csv\", payload",
                    Source = "DirectTransportIntegrationTests.HlogInspect.Csv",
                    Service = "inputplugin-tests",
                    Timestamp = timestamp,
                });

            var result = await RunHlogInspectAsync(rootDir, pluginId, "csv", fields: "message,level");

            Assert.True(result.ExitCode == 0, $"HlogInspect failed with exit code {result.ExitCode}.{Environment.NewLine}STDERR:{Environment.NewLine}{result.StandardError}{Environment.NewLine}STDOUT:{Environment.NewLine}{result.StandardOutput}");
            var lines = result.StandardOutput
                .Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            Assert.Equal(2, lines.Length);
            Assert.Equal("message,level", lines[0]);
            Assert.Equal("\"inspect \"\"csv\"\", payload\",Warn", lines[1]);
            Assert.Contains("fields=message,level", result.StandardError, StringComparison.Ordinal);
        }
        finally
        {
            if (Directory.Exists(rootDir))
                Directory.Delete(rootDir, recursive: true);
        }
    }

    [Fact]
    public async Task HlogInspect_ExpandsTracingFieldPreset_WhenFieldsPresetIsRequested()
    {
        var rootDir = Path.Combine(Path.GetTempPath(), $"hlog_inspect_preset_{Guid.NewGuid():N}");
        var pluginId = "inspect-preset";
        var timestamp = DateTimeOffset.UtcNow;
        const string traceId = "0123456789abcdef0123456789abcdef";
        const string correlationId = "aabbccddeeff00112233445566778899";

        try
        {
            await SeedDirectLogAsync(
                rootDir,
                pluginId,
                new LogMessage
                {
                    Level = LogPipeLevel.Info,
                    Message = "preset payload",
                    Source = "OrdersService",
                    Service = "api-gateway",
                    TraceId = traceId,
                    CorrelationId = correlationId,
                    Timestamp = timestamp,
                });

            var result = await RunHlogInspectAsync(rootDir, pluginId, "json", fields: "tracing");

            Assert.True(result.ExitCode == 0, $"HlogInspect failed with exit code {result.ExitCode}.{Environment.NewLine}STDERR:{Environment.NewLine}{result.StandardError}{Environment.NewLine}STDOUT:{Environment.NewLine}{result.StandardOutput}");
            Assert.Contains("\"traceId\"", result.StandardOutput, StringComparison.Ordinal);
            Assert.Contains("\"correlationId\"", result.StandardOutput, StringComparison.Ordinal);
            Assert.Contains("\"message\"", result.StandardOutput, StringComparison.Ordinal);
            Assert.DoesNotContain("\"level\"", result.StandardOutput, StringComparison.Ordinal);
            Assert.Contains("fields=timestamp,traceId,correlationId,source,service,message", result.StandardError, StringComparison.Ordinal);
        }
        finally
        {
            if (Directory.Exists(rootDir))
                Directory.Delete(rootDir, recursive: true);
        }
    }

    [Fact]
    public async Task HlogInspect_AcceptsDashedFieldAliases_WhenFieldsAreRequested()
    {
        var rootDir = Path.Combine(Path.GetTempPath(), $"hlog_inspect_fields_aliases_{Guid.NewGuid():N}");
        var pluginId = "inspect-fields-aliases";
        var timestamp = DateTimeOffset.UtcNow;
        const string traceId = "0123456789abcdef0123456789abcdef";
        const string correlationId = "aabbccddeeff00112233445566778899";

        try
        {
            await SeedDirectLogAsync(
                rootDir,
                pluginId,
                new LogMessage
                {
                    Level = LogPipeLevel.Warn,
                    Message = "field aliases payload",
                    Source = "OrdersService",
                    Service = "api-gateway",
                    TraceId = traceId,
                    CorrelationId = correlationId,
                    Timestamp = timestamp,
                });

            var result = await RunHlogInspectAsync(rootDir, pluginId, "json", fields: "trace-id,correlation-id,payload-json");

            Assert.True(result.ExitCode == 0, $"HlogInspect failed with exit code {result.ExitCode}.{Environment.NewLine}STDERR:{Environment.NewLine}{result.StandardError}{Environment.NewLine}STDOUT:{Environment.NewLine}{result.StandardOutput}");
            Assert.Contains("\"traceId\"", result.StandardOutput, StringComparison.Ordinal);
            Assert.Contains("\"correlationId\"", result.StandardOutput, StringComparison.Ordinal);
            Assert.Contains("\"payloadJson\"", result.StandardOutput, StringComparison.Ordinal);
            Assert.DoesNotContain("\"message\"", result.StandardOutput, StringComparison.Ordinal);
            Assert.Contains("fields=traceId,correlationId,payloadJson", result.StandardError, StringComparison.Ordinal);
        }
        finally
        {
            if (Directory.Exists(rootDir))
                Directory.Delete(rootDir, recursive: true);
        }
    }

    [Fact]
    public async Task HlogInspect_WritesOutputToFile_WhenOutputPathIsProvided()
    {
        var rootDir = Path.Combine(Path.GetTempPath(), $"hlog_inspect_output_{Guid.NewGuid():N}");
        var pluginId = "inspect-output";
        var timestamp = DateTimeOffset.UtcNow;
        var outputFile = Path.Combine(rootDir, "exports", "records.json");

        try
        {
            await SeedDirectLogAsync(
                rootDir,
                pluginId,
                new LogMessage
                {
                    Level = LogPipeLevel.Info,
                    Message = "output payload",
                    Source = "OrdersService",
                    Service = "api-gateway",
                    Timestamp = timestamp,
                });

            var result = await RunHlogInspectAsync(rootDir, pluginId, "json", outputPath: outputFile);

            Assert.True(result.ExitCode == 0, $"HlogInspect failed with exit code {result.ExitCode}.{Environment.NewLine}STDERR:{Environment.NewLine}{result.StandardError}{Environment.NewLine}STDOUT:{Environment.NewLine}{result.StandardOutput}");
            Assert.True(File.Exists(outputFile), $"Expected output file to exist: {outputFile}");
            var fileContent = await File.ReadAllTextAsync(outputFile);
            Assert.Contains("output payload", fileContent, StringComparison.Ordinal);
            Assert.Equal(string.Empty, result.StandardOutput);
            Assert.Contains($"output={outputFile}", result.StandardError, StringComparison.Ordinal);
        }
        finally
        {
            if (Directory.Exists(rootDir))
                Directory.Delete(rootDir, recursive: true);
        }
    }

    [Fact]
    public async Task HlogInspect_AppendsCsvWithoutDuplicatingHeader_WhenAppendOutputIsEnabled()
    {
        var rootDir = Path.Combine(Path.GetTempPath(), $"hlog_inspect_append_{Guid.NewGuid():N}");
        var pluginId = "inspect-append";
        var firstTimestamp = DateTimeOffset.UtcNow;
        var secondTimestamp = firstTimestamp.AddMilliseconds(1);
        var outputFile = Path.Combine(rootDir, "exports", "records.csv");

        try
        {
            await SeedDirectLogAsync(
                rootDir,
                pluginId,
                new LogMessage
                {
                    Level = LogPipeLevel.Info,
                    Message = "first payload",
                    Source = "OrdersService",
                    Service = "api-gateway",
                    Timestamp = firstTimestamp,
                });

            var firstRun = await RunHlogInspectAsync(rootDir, pluginId, "csv", fields: "core", outputPath: outputFile);
            Assert.True(firstRun.ExitCode == 0, $"HlogInspect first run failed with exit code {firstRun.ExitCode}.{Environment.NewLine}STDERR:{Environment.NewLine}{firstRun.StandardError}{Environment.NewLine}STDOUT:{Environment.NewLine}{firstRun.StandardOutput}");

            await SeedDirectLogAsync(
                rootDir,
                pluginId,
                new LogMessage
                {
                    Level = LogPipeLevel.Error,
                    Message = "second payload",
                    Source = "OrdersService",
                    Service = "api-gateway",
                    Timestamp = secondTimestamp,
                });

            var secondRun = await RunHlogInspectAsync(rootDir, pluginId, "csv", fields: "core", outputPath: outputFile, appendOutput: true, afterTimestamp: firstTimestamp.ToString("O"));
            Assert.True(secondRun.ExitCode == 0, $"HlogInspect second run failed with exit code {secondRun.ExitCode}.{Environment.NewLine}STDERR:{Environment.NewLine}{secondRun.StandardError}{Environment.NewLine}STDOUT:{Environment.NewLine}{secondRun.StandardOutput}");

            var lines = (await File.ReadAllLinesAsync(outputFile))
                .Where(line => !string.IsNullOrWhiteSpace(line))
                .ToArray();

            Assert.Equal(3, lines.Length);
            Assert.Equal("timestamp,level,source,service,message", lines[0]);
            Assert.Contains("first payload", lines[1], StringComparison.Ordinal);
            Assert.Contains("second payload", lines[2], StringComparison.Ordinal);
            Assert.Contains("appendOutput=True", secondRun.StandardError, StringComparison.Ordinal);
        }
        finally
        {
            if (Directory.Exists(rootDir))
                Directory.Delete(rootDir, recursive: true);
        }
    }

    [Fact]
    public async Task HlogInspect_ExpandsErrorsFieldPreset_WhenFieldsPresetIsRequested()
    {
        var rootDir = Path.Combine(Path.GetTempPath(), $"hlog_inspect_errors_{Guid.NewGuid():N}");
        var pluginId = "inspect-errors";
        var timestamp = DateTimeOffset.UtcNow;

        try
        {
            await SeedDirectLogAsync(
                rootDir,
                pluginId,
                new LogMessage
                {
                    Level = LogPipeLevel.Error,
                    Message = "errors payload",
                    Source = "OrdersService",
                    Service = "api-gateway",
                    Timestamp = timestamp,
                });

            var result = await RunHlogInspectAsync(rootDir, pluginId, "json", fields: "errors");

            Assert.True(result.ExitCode == 0, $"HlogInspect failed with exit code {result.ExitCode}.{Environment.NewLine}STDERR:{Environment.NewLine}{result.StandardError}{Environment.NewLine}STDOUT:{Environment.NewLine}{result.StandardOutput}");
            Assert.Contains("\"level\"", result.StandardOutput, StringComparison.Ordinal);
            Assert.Contains("\"payloadJson\"", result.StandardOutput, StringComparison.Ordinal);
            Assert.Contains("errors payload", result.StandardOutput, StringComparison.Ordinal);
            Assert.DoesNotContain("\"traceId\"", result.StandardOutput, StringComparison.Ordinal);
            Assert.Contains("fields=timestamp,level,source,service,message,payloadJson", result.StandardError, StringComparison.Ordinal);
        }
        finally
        {
            if (Directory.Exists(rootDir))
                Directory.Delete(rootDir, recursive: true);
        }
    }

    [Fact]
    public async Task HlogInspect_ExpandsSecurityFieldPreset_WhenFieldsPresetIsRequested()
    {
        var rootDir = Path.Combine(Path.GetTempPath(), $"hlog_inspect_security_{Guid.NewGuid():N}");
        var pluginId = "inspect-security";
        var timestamp = DateTimeOffset.UtcNow;
        const string traceId = "0123456789abcdef0123456789abcdef";
        const string correlationId = "aabbccddeeff00112233445566778899";

        try
        {
            await SeedDirectLogAsync(
                rootDir,
                pluginId,
                new LogMessage
                {
                    Level = LogPipeLevel.Warn,
                    Message = "security payload",
                    Source = "OrdersService",
                    Service = "api-gateway",
                    TraceId = traceId,
                    CorrelationId = correlationId,
                    Timestamp = timestamp,
                });

            var result = await RunHlogInspectAsync(rootDir, pluginId, "json", fields: "security");

            Assert.True(result.ExitCode == 0, $"HlogInspect failed with exit code {result.ExitCode}.{Environment.NewLine}STDERR:{Environment.NewLine}{result.StandardError}{Environment.NewLine}STDOUT:{Environment.NewLine}{result.StandardOutput}");
            Assert.Contains("\"traceId\"", result.StandardOutput, StringComparison.Ordinal);
            Assert.Contains("\"correlationId\"", result.StandardOutput, StringComparison.Ordinal);
            Assert.Contains("\"payloadJson\"", result.StandardOutput, StringComparison.Ordinal);
            Assert.DoesNotContain("\"level\"", result.StandardOutput, StringComparison.Ordinal);
            Assert.Contains("fields=timestamp,source,service,traceId,correlationId,payloadJson", result.StandardError, StringComparison.Ordinal);
        }
        finally
        {
            if (Directory.Exists(rootDir))
                Directory.Delete(rootDir, recursive: true);
        }
    }

    [Fact]
    public async Task HlogInspect_SplitsCsvOutputIntoMultipleFiles_WhenSplitOutputSizeIsEnabled()
    {
        var rootDir = Path.Combine(Path.GetTempPath(), $"hlog_inspect_split_{Guid.NewGuid():N}");
        var pluginId = "inspect-split";
        var timestamp = DateTimeOffset.UtcNow;
        var outputFile = Path.Combine(rootDir, "exports", "records.csv");

        try
        {
            await SeedDirectLogAsync(
                rootDir,
                pluginId,
                new LogMessage
                {
                    Level = LogPipeLevel.Info,
                    Message = new string('A', 80),
                    Source = "OrdersService",
                    Service = "api-gateway",
                    Timestamp = timestamp,
                });

            await SeedDirectLogAsync(
                rootDir,
                pluginId,
                new LogMessage
                {
                    Level = LogPipeLevel.Error,
                    Message = new string('B', 80),
                    Source = "OrdersService",
                    Service = "api-gateway",
                    Timestamp = timestamp.AddMilliseconds(1),
                });

            var result = await RunHlogInspectAsync(rootDir, pluginId, "csv", fields: "core", outputPath: outputFile, splitOutputSizeBytes: 150);

            Assert.True(result.ExitCode == 0, $"HlogInspect failed with exit code {result.ExitCode}.{Environment.NewLine}STDERR:{Environment.NewLine}{result.StandardError}{Environment.NewLine}STDOUT:{Environment.NewLine}{result.StandardOutput}");
            var part1 = Path.Combine(rootDir, "exports", "records.part0001.csv");
            var part2 = Path.Combine(rootDir, "exports", "records.part0002.csv");
            Assert.True(File.Exists(part1), $"Expected split output file to exist: {part1}");
            Assert.True(File.Exists(part2), $"Expected split output file to exist: {part2}");

            var part1Lines = (await File.ReadAllLinesAsync(part1)).Where(line => !string.IsNullOrWhiteSpace(line)).ToArray();
            var part2Lines = (await File.ReadAllLinesAsync(part2)).Where(line => !string.IsNullOrWhiteSpace(line)).ToArray();
            Assert.Equal("timestamp,level,source,service,message", part1Lines[0]);
            Assert.Equal("timestamp,level,source,service,message", part2Lines[0]);
            Assert.Contains(new string('A', 80), string.Join(Environment.NewLine, part1Lines), StringComparison.Ordinal);
            Assert.Contains(new string('B', 80), string.Join(Environment.NewLine, part2Lines), StringComparison.Ordinal);
            Assert.Contains("splitOutputSize=150", result.StandardError, StringComparison.Ordinal);
        }
        finally
        {
            if (Directory.Exists(rootDir))
                Directory.Delete(rootDir, recursive: true);
        }
    }

    [Fact]
    public async Task HlogInspect_SplitsNdjsonOutputIntoMultipleFiles_WhenSplitOutputSizeIsEnabled()
    {
        var rootDir = Path.Combine(Path.GetTempPath(), $"hlog_inspect_split_ndjson_{Guid.NewGuid():N}");
        var pluginId = "inspect-split-ndjson";
        var timestamp = DateTimeOffset.UtcNow;
        var outputFile = Path.Combine(rootDir, "exports", "records.ndjson");

        try
        {
            await SeedDirectLogAsync(
                rootDir,
                pluginId,
                new LogMessage
                {
                    Level = LogPipeLevel.Info,
                    Message = new string('C', 80),
                    Source = "OrdersService",
                    Service = "api-gateway",
                    Timestamp = timestamp,
                });

            await SeedDirectLogAsync(
                rootDir,
                pluginId,
                new LogMessage
                {
                    Level = LogPipeLevel.Fatal,
                    Message = new string('D', 80),
                    Source = "OrdersService",
                    Service = "api-gateway",
                    Timestamp = timestamp.AddMilliseconds(1),
                });

            var result = await RunHlogInspectAsync(rootDir, pluginId, "ndjson", fields: "forensics", outputPath: outputFile, splitOutputSizeBytes: 180);

            Assert.True(result.ExitCode == 0, $"HlogInspect failed with exit code {result.ExitCode}.{Environment.NewLine}STDERR:{Environment.NewLine}{result.StandardError}{Environment.NewLine}STDOUT:{Environment.NewLine}{result.StandardOutput}");
            var part1 = Path.Combine(rootDir, "exports", "records.part0001.ndjson");
            var part2 = Path.Combine(rootDir, "exports", "records.part0002.ndjson");
            Assert.True(File.Exists(part1), $"Expected split output file to exist: {part1}");
            Assert.True(File.Exists(part2), $"Expected split output file to exist: {part2}");

            var part1Lines = (await File.ReadAllLinesAsync(part1)).Where(line => !string.IsNullOrWhiteSpace(line)).ToArray();
            var part2Lines = (await File.ReadAllLinesAsync(part2)).Where(line => !string.IsNullOrWhiteSpace(line)).ToArray();
            Assert.Single(part1Lines);
            Assert.Single(part2Lines);
            Assert.Contains("\"payloadJson\"", part1Lines[0], StringComparison.Ordinal);
            Assert.Contains("\"traceId\"", part1Lines[0], StringComparison.Ordinal);
            Assert.Contains(new string('C', 80), part1Lines[0], StringComparison.Ordinal);
            Assert.Contains(new string('D', 80), part2Lines[0], StringComparison.Ordinal);
            Assert.Contains("splitOutputSize=180", result.StandardError, StringComparison.Ordinal);
        }
        finally
        {
            if (Directory.Exists(rootDir))
                Directory.Delete(rootDir, recursive: true);
        }
    }

    [Fact]
    public async Task HlogInspect_WritesSplitManifest_WhenManifestOutputIsProvided()
    {
        var rootDir = Path.Combine(Path.GetTempPath(), $"hlog_inspect_manifest_{Guid.NewGuid():N}");
        var pluginId = "inspect-manifest";
        var timestamp = DateTimeOffset.UtcNow;
        var outputFile = Path.Combine(rootDir, "exports", "records.ndjson");
        var manifestFile = Path.Combine(rootDir, "exports", "records.manifest.json");

        try
        {
            await SeedDirectLogAsync(
                rootDir,
                pluginId,
                new LogMessage
                {
                    Level = LogPipeLevel.Info,
                    Message = new string('M', 80),
                    Source = "OrdersService",
                    Service = "api-gateway",
                    Timestamp = timestamp,
                });

            await SeedDirectLogAsync(
                rootDir,
                pluginId,
                new LogMessage
                {
                    Level = LogPipeLevel.Warn,
                    Message = new string('N', 80),
                    Source = "OrdersService",
                    Service = "api-gateway",
                    Timestamp = timestamp.AddMilliseconds(1),
                });

            var result = await RunHlogInspectAsync(rootDir, pluginId, "ndjson", fields: "triage", outputPath: outputFile, splitOutputSizeBytes: 180, manifestOutputPath: manifestFile);

            Assert.True(result.ExitCode == 0, $"HlogInspect failed with exit code {result.ExitCode}.{Environment.NewLine}STDERR:{Environment.NewLine}{result.StandardError}{Environment.NewLine}STDOUT:{Environment.NewLine}{result.StandardOutput}");
            Assert.True(File.Exists(manifestFile), $"Expected manifest output file to exist: {manifestFile}");

            using var manifestDocument = JsonDocument.Parse(await File.ReadAllTextAsync(manifestFile));
            var root = manifestDocument.RootElement;
            Assert.Equal("ndjson", root.GetProperty("Format").GetString());
            Assert.Equal("records.ndjson", root.GetProperty("OutputFile").GetString());
            Assert.Equal(180, root.GetProperty("SplitOutputSizeBytes").GetInt32());
            var parts = root.GetProperty("Parts");
            Assert.Equal(2, parts.GetArrayLength());
            Assert.Equal("records.part0001.ndjson", parts[0].GetProperty("FileName").GetString());
            Assert.True(parts[0].GetProperty("RecordCount").GetInt32() >= 1);
            Assert.Contains("manifestOutput=", result.StandardError, StringComparison.Ordinal);
        }
        finally
        {
            if (Directory.Exists(rootDir))
                Directory.Delete(rootDir, recursive: true);
        }
    }

    [Fact]
    public async Task HlogInspect_WritesNdjsonManifest_WhenNdjsonManifestFormatIsProvided()
    {
        var rootDir = Path.Combine(Path.GetTempPath(), $"hlog_inspect_manifest_ndjson_{Guid.NewGuid():N}");
        var pluginId = "inspect-manifest-ndjson";
        var timestamp = DateTimeOffset.UtcNow;
        var outputFile = Path.Combine(rootDir, "exports", "records.ndjson");
        var manifestFile = Path.Combine(rootDir, "exports", "records.manifest.ndjson");

        try
        {
            await SeedDirectLogAsync(
                rootDir,
                pluginId,
                new LogMessage
                {
                    Level = LogPipeLevel.Info,
                    Message = new string('P', 80),
                    Source = "OrdersService",
                    Service = "api-gateway",
                    Timestamp = timestamp,
                });

            await SeedDirectLogAsync(
                rootDir,
                pluginId,
                new LogMessage
                {
                    Level = LogPipeLevel.Warn,
                    Message = new string('Q', 80),
                    Source = "OrdersService",
                    Service = "api-gateway",
                    Timestamp = timestamp.AddMilliseconds(1),
                });

            var result = await RunHlogInspectAsync(rootDir, pluginId, "ndjson", fields: "triage", outputPath: outputFile, splitOutputSizeBytes: 180, manifestOutputPath: manifestFile, manifestFormat: "ndjson");

            Assert.True(result.ExitCode == 0, $"HlogInspect failed with exit code {result.ExitCode}.{Environment.NewLine}STDERR:{Environment.NewLine}{result.StandardError}{Environment.NewLine}STDOUT:{Environment.NewLine}{result.StandardOutput}");
            var manifestLines = (await File.ReadAllLinesAsync(manifestFile)).Where(line => !string.IsNullOrWhiteSpace(line)).ToArray();
            Assert.Equal(2, manifestLines.Length);
            using var firstLine = JsonDocument.Parse(manifestLines[0]);
            Assert.Equal("ndjson", firstLine.RootElement.GetProperty("Format").GetString());
            Assert.Equal("records.part0001.ndjson", firstLine.RootElement.GetProperty("Part").GetProperty("FileName").GetString());
            Assert.Contains("manifestFormat=ndjson", result.StandardError, StringComparison.Ordinal);
        }
        finally
        {
            if (Directory.Exists(rootDir))
                Directory.Delete(rootDir, recursive: true);
        }
    }

    [Fact]
    public async Task HlogInspect_WritesNdjsonManifestWithTotals_WhenManifestIncludeTotalsIsProvided()
    {
        var rootDir = Path.Combine(Path.GetTempPath(), $"hlog_inspect_manifest_totals_{Guid.NewGuid():N}");
        var pluginId = "inspect-manifest-totals";
        var timestamp = DateTimeOffset.UtcNow;
        var outputFile = Path.Combine(rootDir, "exports", "records.ndjson");
        var manifestFile = Path.Combine(rootDir, "exports", "records.manifest.ndjson");

        try
        {
            await SeedDirectLogAsync(
                rootDir,
                pluginId,
                new LogMessage
                {
                    Level = LogPipeLevel.Info,
                    Message = new string('V', 80),
                    Source = "OrdersService",
                    Service = "api-gateway",
                    Timestamp = timestamp,
                });

            await SeedDirectLogAsync(
                rootDir,
                pluginId,
                new LogMessage
                {
                    Level = LogPipeLevel.Warn,
                    Message = new string('W', 80),
                    Source = "OrdersService",
                    Service = "api-gateway",
                    Timestamp = timestamp.AddMilliseconds(1),
                });

            var result = await RunHlogInspectAsync(rootDir, pluginId, "ndjson", fields: "triage", outputPath: outputFile, splitOutputSizeBytes: 180, manifestOutputPath: manifestFile, manifestFormat: "ndjson", manifestIncludeTotals: true);

            Assert.True(result.ExitCode == 0, $"HlogInspect failed with exit code {result.ExitCode}.{Environment.NewLine}STDERR:{Environment.NewLine}{result.StandardError}{Environment.NewLine}STDOUT:{Environment.NewLine}{result.StandardOutput}");
            var manifestLines = (await File.ReadAllLinesAsync(manifestFile)).Where(line => !string.IsNullOrWhiteSpace(line)).ToArray();
            Assert.Equal(3, manifestLines.Length);
            using var totalsLine = JsonDocument.Parse(manifestLines[^1]);
            Assert.Equal("totals", totalsLine.RootElement.GetProperty("Type").GetString());
            Assert.Equal(2, totalsLine.RootElement.GetProperty("PartCount").GetInt32());
            Assert.True(totalsLine.RootElement.GetProperty("RecordCount").GetInt32() >= 2);
            Assert.Contains("manifestIncludeTotals=True", result.StandardError, StringComparison.Ordinal);
        }
        finally
        {
            if (Directory.Exists(rootDir))
                Directory.Delete(rootDir, recursive: true);
        }
    }

    [Fact]
    public async Task HlogInspect_WritesNdjsonManifestWithWindow_WhenManifestIncludeWindowIsProvided()
    {
        var rootDir = Path.Combine(Path.GetTempPath(), $"hlog_inspect_manifest_window_{Guid.NewGuid():N}");
        var pluginId = "inspect-manifest-window";
        var timestamp = DateTimeOffset.UtcNow;
        var matchingTimestamp = timestamp.AddMilliseconds(1);
        var outputFile = Path.Combine(rootDir, "exports", "records.ndjson");
        var manifestFile = Path.Combine(rootDir, "exports", "records.manifest.ndjson");
        var afterTimestamp = timestamp;

        try
        {
            await SeedDirectLogAsync(
                rootDir,
                pluginId,
                new LogMessage
                {
                    Level = LogPipeLevel.Info,
                    Message = "manifest window first",
                    Source = "OrdersService",
                    Service = "api-gateway",
                    Timestamp = timestamp,
                });

            await SeedDirectLogAsync(
                rootDir,
                pluginId,
                new LogMessage
                {
                    Level = LogPipeLevel.Warn,
                    Message = "manifest window second",
                    Source = "OrdersService",
                    Service = "api-gateway",
                    Timestamp = matchingTimestamp,
                });

            var result = await RunHlogInspectAsync(rootDir, pluginId, "ndjson", source: "OrdersService", sort: "desc", limit: 1, afterTimestamp: afterTimestamp.ToString("O"), fields: "triage", outputPath: outputFile, splitOutputSizeBytes: 180, manifestOutputPath: manifestFile, manifestFormat: "ndjson", manifestIncludeTotals: true, manifestIncludeWindow: true);

            Assert.True(result.ExitCode == 0, $"HlogInspect failed with exit code {result.ExitCode}.{Environment.NewLine}STDERR:{Environment.NewLine}{result.StandardError}{Environment.NewLine}STDOUT:{Environment.NewLine}{result.StandardOutput}");
            var manifestLines = (await File.ReadAllLinesAsync(manifestFile)).Where(line => !string.IsNullOrWhiteSpace(line)).ToArray();
            using var totalsLine = JsonDocument.Parse(manifestLines[^1]);
            var window = totalsLine.RootElement.GetProperty("Window");
            var filters = totalsLine.RootElement.GetProperty("Filters");
            Assert.Equal("desc", window.GetProperty("Sort").GetString());
            Assert.Equal(1, window.GetProperty("ReturnedCount").GetInt32());
            Assert.Equal(DateTimeOffset.FromUnixTimeMilliseconds(afterTimestamp.ToUnixTimeMilliseconds()), window.GetProperty("AfterTimestamp").GetDateTimeOffset());
            Assert.Equal("OrdersService", filters.GetProperty("Source").GetString());
            Assert.Equal("Trace", filters.GetProperty("Level").GetString());
            Assert.Contains("manifestIncludeWindow=True", result.StandardError, StringComparison.Ordinal);
        }
        finally
        {
            if (Directory.Exists(rootDir))
                Directory.Delete(rootDir, recursive: true);
        }
    }

    [Fact]
    public async Task HlogInspect_WritesNdjsonManifestWithLevelCounts_WhenManifestIncludeLevelCountsIsProvided()
    {
        var rootDir = Path.Combine(Path.GetTempPath(), $"hlog_inspect_manifest_levels_{Guid.NewGuid():N}");
        var pluginId = "inspect-manifest-levels";
        var timestamp = DateTimeOffset.UtcNow;
        var outputFile = Path.Combine(rootDir, "exports", "records.ndjson");
        var manifestFile = Path.Combine(rootDir, "exports", "records.manifest.ndjson");

        try
        {
            await SeedDirectLogAsync(
                rootDir,
                pluginId,
                new LogMessage
                {
                    Level = LogPipeLevel.Info,
                    Message = new string('L', 80),
                    Source = "OrdersService",
                    Service = "api-gateway",
                    Timestamp = timestamp,
                });

            await SeedDirectLogAsync(
                rootDir,
                pluginId,
                new LogMessage
                {
                    Level = LogPipeLevel.Error,
                    Message = new string('M', 80),
                    Source = "OrdersService",
                    Service = "api-gateway",
                    Timestamp = timestamp.AddMilliseconds(1),
                });

            var result = await RunHlogInspectAsync(rootDir, pluginId, "ndjson", fields: "triage", outputPath: outputFile, splitOutputSizeBytes: 180, manifestOutputPath: manifestFile, manifestFormat: "ndjson", manifestIncludeTotals: true, manifestIncludeLevelCounts: true);

            Assert.True(result.ExitCode == 0, $"HlogInspect failed with exit code {result.ExitCode}.{Environment.NewLine}STDERR:{Environment.NewLine}{result.StandardError}{Environment.NewLine}STDOUT:{Environment.NewLine}{result.StandardOutput}");
            var manifestLines = (await File.ReadAllLinesAsync(manifestFile)).Where(line => !string.IsNullOrWhiteSpace(line)).ToArray();
            using var totalsLine = JsonDocument.Parse(manifestLines[^1]);
            var levels = totalsLine.RootElement.GetProperty("LevelCounts");
            Assert.Equal(1, levels.GetProperty("Info").GetInt32());
            Assert.Equal(1, levels.GetProperty("Error").GetInt32());
            Assert.Equal(0, levels.GetProperty("Warn").GetInt32());
            Assert.Contains("manifestIncludeLevelCounts=True", result.StandardError, StringComparison.Ordinal);
        }
        finally
        {
            if (Directory.Exists(rootDir))
                Directory.Delete(rootDir, recursive: true);
        }
    }

    [Fact]
    public async Task HlogInspect_WritesNdjsonManifestWithMatchCounts_WhenManifestIncludeMatchCountsIsProvided()
    {
        var rootDir = Path.Combine(Path.GetTempPath(), $"hlog_inspect_manifest_match_counts_{Guid.NewGuid():N}");
        var pluginId = "inspect-manifest-match-counts";
        var timestamp = DateTimeOffset.UtcNow;
        var outputFile = Path.Combine(rootDir, "exports", "records.ndjson");
        var manifestFile = Path.Combine(rootDir, "exports", "records.manifest.ndjson");

        try
        {
            await SeedDirectLogAsync(
                rootDir,
                pluginId,
                new LogMessage
                {
                    Level = LogPipeLevel.Info,
                    Message = "match counts first",
                    Source = "OrdersService",
                    Service = "api-gateway",
                    Timestamp = timestamp,
                });

            await SeedDirectLogAsync(
                rootDir,
                pluginId,
                new LogMessage
                {
                    Level = LogPipeLevel.Warn,
                    Message = "match counts second",
                    Source = "OrdersService",
                    Service = "api-gateway",
                    Timestamp = timestamp.AddMilliseconds(1),
                });

            await SeedDirectLogAsync(
                rootDir,
                pluginId,
                new LogMessage
                {
                    Level = LogPipeLevel.Error,
                    Message = "match counts filtered out",
                    Source = "BillingService",
                    Service = "api-gateway",
                    Timestamp = timestamp.AddMilliseconds(2),
                });

            var result = await RunHlogInspectAsync(rootDir, pluginId, "ndjson", source: "OrdersService", limit: 1, sort: "asc", fields: "triage", outputPath: outputFile, splitOutputSizeBytes: 4096, manifestOutputPath: manifestFile, manifestFormat: "ndjson", manifestIncludeTotals: true, manifestIncludeMatchCounts: true);

            Assert.True(result.ExitCode == 0, $"HlogInspect failed with exit code {result.ExitCode}.{Environment.NewLine}STDERR:{Environment.NewLine}{result.StandardError}{Environment.NewLine}STDOUT:{Environment.NewLine}{result.StandardOutput}");
            var manifestLines = (await File.ReadAllLinesAsync(manifestFile)).Where(line => !string.IsNullOrWhiteSpace(line)).ToArray();
            using var totalsLine = JsonDocument.Parse(manifestLines[^1]);
            var matched = totalsLine.RootElement.GetProperty("MatchedLevelCounts");
            var returned = totalsLine.RootElement.GetProperty("ReturnedLevelCounts");
            Assert.Equal(1, matched.GetProperty("Info").GetInt32());
            Assert.Equal(1, matched.GetProperty("Warn").GetInt32());
            Assert.Equal(0, matched.GetProperty("Error").GetInt32());
            Assert.Equal(1, returned.GetProperty("Info").GetInt32());
            Assert.Equal(0, returned.GetProperty("Warn").GetInt32());
            Assert.Contains("manifestIncludeMatchCounts=True", result.StandardError, StringComparison.Ordinal);
        }
        finally
        {
            if (Directory.Exists(rootDir))
                Directory.Delete(rootDir, recursive: true);
        }
    }

    [Fact]
    public async Task HlogInspect_UsesSplitOutputTemplate_WhenTemplateIsProvided()
    {
        var rootDir = Path.Combine(Path.GetTempPath(), $"hlog_inspect_split_template_{Guid.NewGuid():N}");
        var pluginId = "inspect-split-template";
        var timestamp = DateTimeOffset.UtcNow;
        var outputFile = Path.Combine(rootDir, "exports", "records.ndjson");

        try
        {
            await SeedDirectLogAsync(
                rootDir,
                pluginId,
                new LogMessage
                {
                    Level = LogPipeLevel.Info,
                    Message = new string('T', 80),
                    Source = "OrdersService",
                    Service = "api-gateway",
                    Timestamp = timestamp,
                });

            await SeedDirectLogAsync(
                rootDir,
                pluginId,
                new LogMessage
                {
                    Level = LogPipeLevel.Error,
                    Message = new string('U', 80),
                    Source = "OrdersService",
                    Service = "api-gateway",
                    Timestamp = timestamp.AddMilliseconds(1),
                });

            var result = await RunHlogInspectAsync(rootDir, pluginId, "ndjson", fields: "triage", outputPath: outputFile, splitOutputSizeBytes: 180, splitOutputTemplate: "chunks/{name}-chunk-{part}{ext}");

            Assert.True(result.ExitCode == 0, $"HlogInspect failed with exit code {result.ExitCode}.{Environment.NewLine}STDERR:{Environment.NewLine}{result.StandardError}{Environment.NewLine}STDOUT:{Environment.NewLine}{result.StandardOutput}");
            var part1 = Path.Combine(rootDir, "exports", "chunks", "records-chunk-0001.ndjson");
            var part2 = Path.Combine(rootDir, "exports", "chunks", "records-chunk-0002.ndjson");
            Assert.True(File.Exists(part1), $"Expected templated split output file to exist: {part1}");
            Assert.True(File.Exists(part2), $"Expected templated split output file to exist: {part2}");
            Assert.Contains("splitOutputTemplate=chunks/{name}-chunk-{part}{ext}", result.StandardError, StringComparison.Ordinal);
        }
        finally
        {
            if (Directory.Exists(rootDir))
                Directory.Delete(rootDir, recursive: true);
        }
    }

    [Fact]
    public async Task HlogInspect_UsesSplitOutputStartIndex_WhenStartIndexIsProvided()
    {
        var rootDir = Path.Combine(Path.GetTempPath(), $"hlog_inspect_split_start_{Guid.NewGuid():N}");
        var pluginId = "inspect-split-start";
        var timestamp = DateTimeOffset.UtcNow;
        var outputFile = Path.Combine(rootDir, "exports", "records.ndjson");

        try
        {
            await SeedDirectLogAsync(
                rootDir,
                pluginId,
                new LogMessage
                {
                    Level = LogPipeLevel.Info,
                    Message = new string('S', 80),
                    Source = "OrdersService",
                    Service = "api-gateway",
                    Timestamp = timestamp,
                });

            await SeedDirectLogAsync(
                rootDir,
                pluginId,
                new LogMessage
                {
                    Level = LogPipeLevel.Error,
                    Message = new string('R', 80),
                    Source = "OrdersService",
                    Service = "api-gateway",
                    Timestamp = timestamp.AddMilliseconds(1),
                });

            var result = await RunHlogInspectAsync(rootDir, pluginId, "ndjson", fields: "triage", outputPath: outputFile, splitOutputSizeBytes: 180, splitOutputStartIndex: 10);

            Assert.True(result.ExitCode == 0, $"HlogInspect failed with exit code {result.ExitCode}.{Environment.NewLine}STDERR:{Environment.NewLine}{result.StandardError}{Environment.NewLine}STDOUT:{Environment.NewLine}{result.StandardOutput}");
            var part10 = Path.Combine(rootDir, "exports", "records.part0010.ndjson");
            var part11 = Path.Combine(rootDir, "exports", "records.part0011.ndjson");
            Assert.True(File.Exists(part10), $"Expected split output file to exist: {part10}");
            Assert.True(File.Exists(part11), $"Expected split output file to exist: {part11}");
            Assert.Contains("splitOutputStartIndex=10", result.StandardError, StringComparison.Ordinal);
        }
        finally
        {
            if (Directory.Exists(rootDir))
                Directory.Delete(rootDir, recursive: true);
        }
    }

    [Fact]
    public async Task HlogInspect_UsesPlainTemplateNumberStyle_ForSplitOutputTemplate()
    {
        var rootDir = Path.Combine(Path.GetTempPath(), $"hlog_inspect_split_plain_{Guid.NewGuid():N}");
        var pluginId = "inspect-split-plain";
        var timestamp = DateTimeOffset.UtcNow;
        var outputFile = Path.Combine(rootDir, "exports", "records.ndjson");

        try
        {
            await SeedDirectLogAsync(
                rootDir,
                pluginId,
                new LogMessage
                {
                    Level = LogPipeLevel.Info,
                    Message = new string('A', 80),
                    Source = "OrdersService",
                    Service = "api-gateway",
                    Timestamp = timestamp,
                });

            await SeedDirectLogAsync(
                rootDir,
                pluginId,
                new LogMessage
                {
                    Level = LogPipeLevel.Error,
                    Message = new string('B', 80),
                    Source = "OrdersService",
                    Service = "api-gateway",
                    Timestamp = timestamp.AddMilliseconds(1),
                });

            var result = await RunHlogInspectAsync(rootDir, pluginId, "ndjson", fields: "triage", outputPath: outputFile, splitOutputSizeBytes: 180, splitOutputTemplate: "chunks/{name}-chunk-{part}{ext}", templateNumberStyle: "plain");

            Assert.True(result.ExitCode == 0, $"HlogInspect failed with exit code {result.ExitCode}.{Environment.NewLine}STDERR:{Environment.NewLine}{result.StandardError}{Environment.NewLine}STDOUT:{Environment.NewLine}{result.StandardOutput}");
            var part1 = Path.Combine(rootDir, "exports", "chunks", "records-chunk-1.ndjson");
            var part2 = Path.Combine(rootDir, "exports", "chunks", "records-chunk-2.ndjson");
            Assert.True(File.Exists(part1), $"Expected templated split output file to exist: {part1}");
            Assert.True(File.Exists(part2), $"Expected templated split output file to exist: {part2}");
            Assert.Contains("templateNumberStyle=plain", result.StandardError, StringComparison.Ordinal);
        }
        finally
        {
            if (Directory.Exists(rootDir))
                Directory.Delete(rootDir, recursive: true);
        }
    }

    [Fact]
    public async Task HlogInspect_WritesSummaryOutput_WhenSummaryPathIsProvided()
    {
        var rootDir = Path.Combine(Path.GetTempPath(), $"hlog_inspect_summary_{Guid.NewGuid():N}");
        var pluginId = "inspect-summary";
        var timestamp = DateTimeOffset.UtcNow;
        var summaryFile = Path.Combine(rootDir, "exports", "records.summary.json");

        try
        {
            await SeedDirectLogAsync(
                rootDir,
                pluginId,
                new LogMessage
                {
                    Level = LogPipeLevel.Info,
                    Message = "summary first",
                    Source = "OrdersService",
                    Service = "api-gateway",
                    Timestamp = timestamp,
                });

            await SeedDirectLogAsync(
                rootDir,
                pluginId,
                new LogMessage
                {
                    Level = LogPipeLevel.Error,
                    Message = "summary second",
                    Source = "OrdersService",
                    Service = "api-gateway",
                    Timestamp = timestamp.AddMilliseconds(1),
                });

            var result = await RunHlogInspectAsync(rootDir, pluginId, "json", summaryOutputPath: summaryFile, limit: 1, offset: 0, sort: "asc");

            Assert.True(result.ExitCode == 0, $"HlogInspect failed with exit code {result.ExitCode}.{Environment.NewLine}STDERR:{Environment.NewLine}{result.StandardError}{Environment.NewLine}STDOUT:{Environment.NewLine}{result.StandardOutput}");
            Assert.True(File.Exists(summaryFile), $"Expected summary output file to exist: {summaryFile}");

            using var summaryDocument = JsonDocument.Parse(await File.ReadAllTextAsync(summaryFile));
            var root = summaryDocument.RootElement;
            Assert.Equal(2, root.GetProperty("Meta").GetProperty("MatchedCount").GetInt32());
            Assert.Equal(1, root.GetProperty("Meta").GetProperty("ReturnedCount").GetInt32());
            Assert.Equal(1, root.GetProperty("MatchedLevels").GetProperty("Info").GetInt32());
            Assert.Equal(1, root.GetProperty("MatchedLevels").GetProperty("Error").GetInt32());
            Assert.Equal(1, root.GetProperty("ReturnedLevels").GetProperty("Info").GetInt32());
            Assert.Contains("summaryOutput=", result.StandardError, StringComparison.Ordinal);
        }
        finally
        {
            if (Directory.Exists(rootDir))
                Directory.Delete(rootDir, recursive: true);
        }
    }

    [Fact]
    public async Task HlogInspect_WritesTableSummaryOutput_WhenTableSummaryFormatIsProvided()
    {
        var rootDir = Path.Combine(Path.GetTempPath(), $"hlog_inspect_summary_table_{Guid.NewGuid():N}");
        var pluginId = "inspect-summary-table";
        var timestamp = DateTimeOffset.UtcNow;
        var summaryFile = Path.Combine(rootDir, "exports", "records.summary.txt");

        try
        {
            await SeedDirectLogAsync(
                rootDir,
                pluginId,
                new LogMessage
                {
                    Level = LogPipeLevel.Info,
                    Message = "summary table first",
                    Source = "OrdersService",
                    Service = "api-gateway",
                    Timestamp = timestamp,
                });

            await SeedDirectLogAsync(
                rootDir,
                pluginId,
                new LogMessage
                {
                    Level = LogPipeLevel.Warn,
                    Message = "summary table second",
                    Source = "OrdersService",
                    Service = "api-gateway",
                    Timestamp = timestamp.AddMilliseconds(1),
                });

            var result = await RunHlogInspectAsync(rootDir, pluginId, "json", summaryOutputPath: summaryFile, summaryFormat: "table", limit: 1, sort: "asc");

            Assert.True(result.ExitCode == 0, $"HlogInspect failed with exit code {result.ExitCode}.{Environment.NewLine}STDERR:{Environment.NewLine}{result.StandardError}{Environment.NewLine}STDOUT:{Environment.NewLine}{result.StandardOutput}");
            Assert.True(File.Exists(summaryFile), $"Expected summary output file to exist: {summaryFile}");
            var summaryText = await File.ReadAllTextAsync(summaryFile);
            Assert.Contains("matchedCount=2 returnedCount=1", summaryText, StringComparison.Ordinal);
            Assert.Contains("matchedLevels trace=0 debug=0 info=1 warn=1 error=0 fatal=0", summaryText, StringComparison.Ordinal);
            Assert.Contains("summaryFormat=table", result.StandardError, StringComparison.Ordinal);
        }
        finally
        {
            if (Directory.Exists(rootDir))
                Directory.Delete(rootDir, recursive: true);
        }
    }

    [Fact]
    public async Task HlogInspect_WritesSummaryWithLastRecord_WhenSummaryIncludeRecordsIsProvided()
    {
        var rootDir = Path.Combine(Path.GetTempPath(), $"hlog_inspect_summary_records_{Guid.NewGuid():N}");
        var pluginId = "inspect-summary-records";
        var timestamp = DateTimeOffset.UtcNow;
        var summaryFile = Path.Combine(rootDir, "exports", "records.summary.json");

        try
        {
            await SeedDirectLogAsync(
                rootDir,
                pluginId,
                new LogMessage
                {
                    Level = LogPipeLevel.Info,
                    Message = "summary sample first",
                    Source = "OrdersService",
                    Service = "api-gateway",
                    Timestamp = timestamp,
                });

            await SeedDirectLogAsync(
                rootDir,
                pluginId,
                new LogMessage
                {
                    Level = LogPipeLevel.Error,
                    Message = "summary sample last",
                    Source = "OrdersService",
                    Service = "api-gateway",
                    Timestamp = timestamp.AddMilliseconds(1),
                });

            var result = await RunHlogInspectAsync(rootDir, pluginId, "json", summaryOutputPath: summaryFile, summaryIncludeRecords: "last", sort: "asc");

            Assert.True(result.ExitCode == 0, $"HlogInspect failed with exit code {result.ExitCode}.{Environment.NewLine}STDERR:{Environment.NewLine}{result.StandardError}{Environment.NewLine}STDOUT:{Environment.NewLine}{result.StandardOutput}");
            using var summaryDocument = JsonDocument.Parse(await File.ReadAllTextAsync(summaryFile));
            var records = summaryDocument.RootElement.GetProperty("Records");
            Assert.Equal(1, records.GetArrayLength());
            Assert.Equal("summary sample last", records[0].GetProperty("Message").GetString());
            Assert.Contains("summaryIncludeRecords=last", result.StandardError, StringComparison.Ordinal);
        }
        finally
        {
            if (Directory.Exists(rootDir))
                Directory.Delete(rootDir, recursive: true);
        }
    }

    [Fact]
    public async Task HlogInspect_WritesSummaryWithProjectedRecordFields_WhenSummaryRecordFieldsAreProvided()
    {
        var rootDir = Path.Combine(Path.GetTempPath(), $"hlog_inspect_summary_projected_{Guid.NewGuid():N}");
        var pluginId = "inspect-summary-projected";
        var timestamp = DateTimeOffset.UtcNow;
        var summaryFile = Path.Combine(rootDir, "exports", "records.summary.json");

        try
        {
            await SeedDirectLogAsync(
                rootDir,
                pluginId,
                new LogMessage
                {
                    Level = LogPipeLevel.Info,
                    Message = "summary projected first",
                    Source = "OrdersService",
                    Service = "api-gateway",
                    Timestamp = timestamp,
                });

            await SeedDirectLogAsync(
                rootDir,
                pluginId,
                new LogMessage
                {
                    Level = LogPipeLevel.Error,
                    Message = "summary projected last",
                    Source = "OrdersService",
                    Service = "api-gateway",
                    Timestamp = timestamp.AddMilliseconds(1),
                });

            var result = await RunHlogInspectAsync(rootDir, pluginId, "json", summaryOutputPath: summaryFile, summaryIncludeRecords: "last", summaryRecordFields: "timestamp,message", sort: "asc");

            Assert.True(result.ExitCode == 0, $"HlogInspect failed with exit code {result.ExitCode}.{Environment.NewLine}STDERR:{Environment.NewLine}{result.StandardError}{Environment.NewLine}STDOUT:{Environment.NewLine}{result.StandardOutput}");
            using var summaryDocument = JsonDocument.Parse(await File.ReadAllTextAsync(summaryFile));
            var record = summaryDocument.RootElement.GetProperty("Records")[0];
            Assert.True(record.TryGetProperty("timestamp", out _));
            Assert.Equal("summary projected last", record.GetProperty("message").GetString());
            Assert.False(record.TryGetProperty("level", out _));
            Assert.Contains("summaryRecordFields=timestamp,message", result.StandardError, StringComparison.Ordinal);
        }
        finally
        {
            if (Directory.Exists(rootDir))
                Directory.Delete(rootDir, recursive: true);
        }
    }

    [Fact]
    public async Task HlogInspect_AcceptsDashedFieldAliases_WhenSummaryRecordFieldsAreRequested()
    {
        var rootDir = Path.Combine(Path.GetTempPath(), $"hlog_inspect_summary_fields_aliases_{Guid.NewGuid():N}");
        var pluginId = "inspect-summary-fields-aliases";
        var timestamp = DateTimeOffset.UtcNow;
        var summaryFile = Path.Combine(rootDir, "exports", "records.summary.json");
        const string traceId = "0123456789abcdef0123456789abcdef";
        const string correlationId = "aabbccddeeff00112233445566778899";

        try
        {
            await SeedDirectLogAsync(
                rootDir,
                pluginId,
                new LogMessage
                {
                    Level = LogPipeLevel.Info,
                    Message = "summary aliases first",
                    Source = "OrdersService",
                    Service = "api-gateway",
                    TraceId = traceId,
                    CorrelationId = correlationId,
                    Timestamp = timestamp,
                });

            await SeedDirectLogAsync(
                rootDir,
                pluginId,
                new LogMessage
                {
                    Level = LogPipeLevel.Error,
                    Message = "summary aliases last",
                    Source = "OrdersService",
                    Service = "api-gateway",
                    TraceId = traceId,
                    CorrelationId = correlationId,
                    Timestamp = timestamp.AddMilliseconds(1),
                });

            var result = await RunHlogInspectAsync(rootDir, pluginId, "json", summaryOutputPath: summaryFile, summaryIncludeRecords: "last", summaryRecordFields: "trace-id,correlation-id,payload-json", sort: "asc");

            Assert.True(result.ExitCode == 0, $"HlogInspect failed with exit code {result.ExitCode}.{Environment.NewLine}STDERR:{Environment.NewLine}{result.StandardError}{Environment.NewLine}STDOUT:{Environment.NewLine}{result.StandardOutput}");
            using var summaryDocument = JsonDocument.Parse(await File.ReadAllTextAsync(summaryFile));
            var record = summaryDocument.RootElement.GetProperty("Records")[0];
            Assert.Equal(traceId, record.GetProperty("traceId").GetString());
            Assert.Equal(correlationId, record.GetProperty("correlationId").GetString());
            Assert.True(record.TryGetProperty("payloadJson", out _));
            Assert.False(record.TryGetProperty("message", out _));
            Assert.Contains("summaryRecordFields=traceId,correlationId,payloadJson", result.StandardError, StringComparison.Ordinal);
        }
        finally
        {
            if (Directory.Exists(rootDir))
                Directory.Delete(rootDir, recursive: true);
        }
    }

    [Fact]
    public async Task HlogInspect_WritesMultipleSummaryRecords_WhenSummaryRecordCountIsProvided()
    {
        var rootDir = Path.Combine(Path.GetTempPath(), $"hlog_inspect_summary_count_{Guid.NewGuid():N}");
        var pluginId = "inspect-summary-count";
        var timestamp = DateTimeOffset.UtcNow;
        var summaryFile = Path.Combine(rootDir, "exports", "records.summary.json");

        try
        {
            await SeedDirectLogAsync(
                rootDir,
                pluginId,
                new LogMessage
                {
                    Level = LogPipeLevel.Info,
                    Message = "summary count first",
                    Source = "OrdersService",
                    Service = "api-gateway",
                    Timestamp = timestamp,
                });

            await SeedDirectLogAsync(
                rootDir,
                pluginId,
                new LogMessage
                {
                    Level = LogPipeLevel.Warn,
                    Message = "summary count second",
                    Source = "OrdersService",
                    Service = "api-gateway",
                    Timestamp = timestamp.AddMilliseconds(1),
                });

            await SeedDirectLogAsync(
                rootDir,
                pluginId,
                new LogMessage
                {
                    Level = LogPipeLevel.Error,
                    Message = "summary count third",
                    Source = "OrdersService",
                    Service = "api-gateway",
                    Timestamp = timestamp.AddMilliseconds(2),
                });

            var result = await RunHlogInspectAsync(rootDir, pluginId, "json", summaryOutputPath: summaryFile, summaryIncludeRecords: "last", summaryRecordCount: 2, summaryRecordFields: "message", sort: "asc");

            Assert.True(result.ExitCode == 0, $"HlogInspect failed with exit code {result.ExitCode}.{Environment.NewLine}STDERR:{Environment.NewLine}STDERR:{Environment.NewLine}{result.StandardError}{Environment.NewLine}STDOUT:{Environment.NewLine}{result.StandardOutput}");
            using var summaryDocument = JsonDocument.Parse(await File.ReadAllTextAsync(summaryFile));
            var records = summaryDocument.RootElement.GetProperty("Records");
            Assert.Equal(2, records.GetArrayLength());
            Assert.Equal("summary count second", records[0].GetProperty("message").GetString());
            Assert.Equal("summary count third", records[1].GetProperty("message").GetString());
            Assert.Contains("summaryRecordCount=2", result.StandardError, StringComparison.Ordinal);
        }
        finally
        {
            if (Directory.Exists(rootDir))
                Directory.Delete(rootDir, recursive: true);
        }
    }

    [Fact]
    public async Task HlogInspect_WritesTableSummaryWithJsonRecordBlock_WhenSummaryRecordsFormatIsJson()
    {
        var rootDir = Path.Combine(Path.GetTempPath(), $"hlog_inspect_summary_records_json_{Guid.NewGuid():N}");
        var pluginId = "inspect-summary-records-json";
        var timestamp = DateTimeOffset.UtcNow;
        var summaryFile = Path.Combine(rootDir, "exports", "records.summary.txt");

        try
        {
            await SeedDirectLogAsync(
                rootDir,
                pluginId,
                new LogMessage
                {
                    Level = LogPipeLevel.Info,
                    Message = "summary records json first",
                    Source = "OrdersService",
                    Service = "api-gateway",
                    Timestamp = timestamp,
                });

            await SeedDirectLogAsync(
                rootDir,
                pluginId,
                new LogMessage
                {
                    Level = LogPipeLevel.Warn,
                    Message = "summary records json second",
                    Source = "OrdersService",
                    Service = "api-gateway",
                    Timestamp = timestamp.AddMilliseconds(1),
                });

            var result = await RunHlogInspectAsync(rootDir, pluginId, "json", summaryOutputPath: summaryFile, summaryFormat: "table", summaryIncludeRecords: "last", summaryRecordCount: 2, summaryRecordFields: "timestamp,message", summaryRecordsFormat: "json", sort: "asc");

            Assert.True(result.ExitCode == 0, $"HlogInspect failed with exit code {result.ExitCode}.{Environment.NewLine}STDERR:{Environment.NewLine}{result.StandardError}{Environment.NewLine}STDOUT:{Environment.NewLine}{result.StandardOutput}");
            var summaryText = await File.ReadAllTextAsync(summaryFile);
            Assert.Contains("records=last count=2 format=json", summaryText, StringComparison.Ordinal);
            Assert.Contains("\"message\": \"summary records json first\"", summaryText, StringComparison.Ordinal);
            Assert.Contains("\"message\": \"summary records json second\"", summaryText, StringComparison.Ordinal);
            Assert.Contains("summaryRecordsFormat=json", result.StandardError, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            if (Directory.Exists(rootDir))
                Directory.Delete(rootDir, recursive: true);
        }
    }

    [Fact]
    public async Task HlogInspect_WritesTableSummaryWithNdjsonRecordBlock_WhenSummaryRecordsFormatIsNdjson()
    {
        var rootDir = Path.Combine(Path.GetTempPath(), $"hlog_inspect_summary_records_ndjson_{Guid.NewGuid():N}");
        var pluginId = "inspect-summary-records-ndjson";
        var timestamp = DateTimeOffset.UtcNow;
        var summaryFile = Path.Combine(rootDir, "exports", "records.summary.txt");

        try
        {
            await SeedDirectLogAsync(
                rootDir,
                pluginId,
                new LogMessage
                {
                    Level = LogPipeLevel.Info,
                    Message = "summary records ndjson first",
                    Source = "OrdersService",
                    Service = "api-gateway",
                    Timestamp = timestamp,
                });

            await SeedDirectLogAsync(
                rootDir,
                pluginId,
                new LogMessage
                {
                    Level = LogPipeLevel.Warn,
                    Message = "summary records ndjson second",
                    Source = "OrdersService",
                    Service = "api-gateway",
                    Timestamp = timestamp.AddMilliseconds(1),
                });

            var result = await RunHlogInspectAsync(rootDir, pluginId, "json", summaryOutputPath: summaryFile, summaryFormat: "table", summaryIncludeRecords: "last", summaryRecordCount: 2, summaryRecordFields: "timestamp,message", summaryRecordsFormat: "ndjson", sort: "asc");

            Assert.True(result.ExitCode == 0, $"HlogInspect failed with exit code {result.ExitCode}.{Environment.NewLine}STDERR:{Environment.NewLine}{result.StandardError}{Environment.NewLine}STDOUT:{Environment.NewLine}{result.StandardOutput}");
            var summaryText = await File.ReadAllTextAsync(summaryFile);
            Assert.Contains("records=last count=2 format=ndjson", summaryText, StringComparison.Ordinal);
            Assert.Contains("\"message\":\"summary records ndjson first\"", summaryText, StringComparison.Ordinal);
            Assert.Contains("\"message\":\"summary records ndjson second\"", summaryText, StringComparison.Ordinal);
            Assert.Contains("summaryRecordsFormat=ndjson", result.StandardError, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            if (Directory.Exists(rootDir))
                Directory.Delete(rootDir, recursive: true);
        }
    }

    [Fact]
    public async Task HlogInspect_WritesSummaryRecordsToSeparateFile_WhenSummaryRecordsOutputIsProvided()
    {
        var rootDir = Path.Combine(Path.GetTempPath(), $"hlog_inspect_summary_records_output_{Guid.NewGuid():N}");
        var pluginId = "inspect-summary-records-output";
        var timestamp = DateTimeOffset.UtcNow;
        var summaryFile = Path.Combine(rootDir, "exports", "records.summary.json");
        var summaryRecordsFile = Path.Combine(rootDir, "exports", "records.sample.json");

        try
        {
            await SeedDirectLogAsync(
                rootDir,
                pluginId,
                new LogMessage
                {
                    Level = LogPipeLevel.Info,
                    Message = "sample output first",
                    Source = "OrdersService",
                    Service = "api-gateway",
                    Timestamp = timestamp,
                });

            await SeedDirectLogAsync(
                rootDir,
                pluginId,
                new LogMessage
                {
                    Level = LogPipeLevel.Error,
                    Message = "sample output last",
                    Source = "OrdersService",
                    Service = "api-gateway",
                    Timestamp = timestamp.AddMilliseconds(1),
                });

            var result = await RunHlogInspectAsync(rootDir, pluginId, "json", summaryOutputPath: summaryFile, summaryIncludeRecords: "last", summaryRecordFields: "timestamp,message", summaryRecordsFormat: "json", summaryRecordsOutputPath: summaryRecordsFile, sort: "asc");

            Assert.True(result.ExitCode == 0, $"HlogInspect failed with exit code {result.ExitCode}.{Environment.NewLine}STDERR:{Environment.NewLine}{result.StandardError}{Environment.NewLine}STDOUT:{Environment.NewLine}{result.StandardOutput}");
            Assert.True(File.Exists(summaryFile), $"Expected summary output file to exist: {summaryFile}");
            Assert.True(File.Exists(summaryRecordsFile), $"Expected summary records output file to exist: {summaryRecordsFile}");

            using var summaryRecordsDocument = JsonDocument.Parse(await File.ReadAllTextAsync(summaryRecordsFile));
            Assert.Equal(1, summaryRecordsDocument.RootElement.GetArrayLength());
            var record = summaryRecordsDocument.RootElement[0];
            Assert.Equal("sample output last", record.GetProperty("message").GetString());
            Assert.False(record.TryGetProperty("level", out _));
            Assert.Contains("summaryRecordsOutput=", result.StandardError, StringComparison.Ordinal);
        }
        finally
        {
            if (Directory.Exists(rootDir))
                Directory.Delete(rootDir, recursive: true);
        }
    }

    [Fact]
    public async Task HlogInspect_WritesSummaryRecordsAsNdjsonToSeparateFile_WhenSummaryRecordsOutputIsProvided()
    {
        var rootDir = Path.Combine(Path.GetTempPath(), $"hlog_inspect_summary_records_output_ndjson_{Guid.NewGuid():N}");
        var pluginId = "inspect-summary-records-output-ndjson";
        var timestamp = DateTimeOffset.UtcNow;
        var summaryFile = Path.Combine(rootDir, "exports", "records.summary.json");
        var summaryRecordsFile = Path.Combine(rootDir, "exports", "records.sample.ndjson");

        try
        {
            await SeedDirectLogAsync(
                rootDir,
                pluginId,
                new LogMessage
                {
                    Level = LogPipeLevel.Info,
                    Message = "sample output ndjson first",
                    Source = "OrdersService",
                    Service = "api-gateway",
                    Timestamp = timestamp,
                });

            await SeedDirectLogAsync(
                rootDir,
                pluginId,
                new LogMessage
                {
                    Level = LogPipeLevel.Error,
                    Message = "sample output ndjson last",
                    Source = "OrdersService",
                    Service = "api-gateway",
                    Timestamp = timestamp.AddMilliseconds(1),
                });

            var result = await RunHlogInspectAsync(rootDir, pluginId, "json", summaryOutputPath: summaryFile, summaryIncludeRecords: "last", summaryRecordFields: "timestamp,message", summaryRecordsFormat: "ndjson", summaryRecordsOutputPath: summaryRecordsFile, sort: "asc");

            Assert.True(result.ExitCode == 0, $"HlogInspect failed with exit code {result.ExitCode}.{Environment.NewLine}STDERR:{Environment.NewLine}{result.StandardError}{Environment.NewLine}STDOUT:{Environment.NewLine}{result.StandardOutput}");
            Assert.True(File.Exists(summaryRecordsFile), $"Expected summary records output file to exist: {summaryRecordsFile}");

            var lines = (await File.ReadAllLinesAsync(summaryRecordsFile)).Where(line => !string.IsNullOrWhiteSpace(line)).ToArray();
            Assert.Single(lines);
            using var recordDocument = JsonDocument.Parse(lines[0]);
            Assert.Equal("sample output ndjson last", recordDocument.RootElement.GetProperty("message").GetString());
            Assert.False(recordDocument.RootElement.TryGetProperty("level", out _));
            Assert.Contains("summaryRecordsFormat=ndjson", result.StandardError, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            if (Directory.Exists(rootDir))
                Directory.Delete(rootDir, recursive: true);
        }
    }

    [Fact]
    public async Task HlogInspect_WritesTableSummaryWithCsvRecordBlock_WhenSummaryRecordsFormatIsCsv()
    {
        var rootDir = Path.Combine(Path.GetTempPath(), $"hlog_inspect_summary_records_csv_{Guid.NewGuid():N}");
        var pluginId = "inspect-summary-records-csv";
        var timestamp = DateTimeOffset.UtcNow;
        var summaryFile = Path.Combine(rootDir, "exports", "records.summary.txt");

        try
        {
            await SeedDirectLogAsync(
                rootDir,
                pluginId,
                new LogMessage
                {
                    Level = LogPipeLevel.Info,
                    Message = "summary records csv first",
                    Source = "OrdersService",
                    Service = "api-gateway",
                    Timestamp = timestamp,
                });

            await SeedDirectLogAsync(
                rootDir,
                pluginId,
                new LogMessage
                {
                    Level = LogPipeLevel.Warn,
                    Message = "summary records csv second",
                    Source = "OrdersService",
                    Service = "api-gateway",
                    Timestamp = timestamp.AddMilliseconds(1),
                });

            var result = await RunHlogInspectAsync(rootDir, pluginId, "json", summaryOutputPath: summaryFile, summaryFormat: "table", summaryIncludeRecords: "last", summaryRecordCount: 2, summaryRecordFields: "timestamp,message", summaryRecordsFormat: "csv", sort: "asc");

            Assert.True(result.ExitCode == 0, $"HlogInspect failed with exit code {result.ExitCode}.{Environment.NewLine}STDERR:{Environment.NewLine}{result.StandardError}{Environment.NewLine}STDOUT:{Environment.NewLine}{result.StandardOutput}");
            var summaryText = await File.ReadAllTextAsync(summaryFile);
            Assert.Contains("records=last count=2 format=csv", summaryText, StringComparison.Ordinal);
            Assert.Contains("timestamp,message", summaryText, StringComparison.Ordinal);
            Assert.Contains("summary records csv first", summaryText, StringComparison.Ordinal);
            Assert.Contains("summary records csv second", summaryText, StringComparison.Ordinal);
            Assert.Contains("summaryRecordsFormat=csv", result.StandardError, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            if (Directory.Exists(rootDir))
                Directory.Delete(rootDir, recursive: true);
        }
    }

    [Fact]
    public async Task HlogInspect_WritesSummaryRecordsAsCsvToSeparateFile_WhenSummaryRecordsOutputIsProvided()
    {
        var rootDir = Path.Combine(Path.GetTempPath(), $"hlog_inspect_summary_records_output_csv_{Guid.NewGuid():N}");
        var pluginId = "inspect-summary-records-output-csv";
        var timestamp = DateTimeOffset.UtcNow;
        var summaryFile = Path.Combine(rootDir, "exports", "records.summary.json");
        var summaryRecordsFile = Path.Combine(rootDir, "exports", "records.sample.csv");

        try
        {
            await SeedDirectLogAsync(
                rootDir,
                pluginId,
                new LogMessage
                {
                    Level = LogPipeLevel.Info,
                    Message = "sample output csv first",
                    Source = "OrdersService",
                    Service = "api-gateway",
                    Timestamp = timestamp,
                });

            await SeedDirectLogAsync(
                rootDir,
                pluginId,
                new LogMessage
                {
                    Level = LogPipeLevel.Error,
                    Message = "sample output csv last",
                    Source = "OrdersService",
                    Service = "api-gateway",
                    Timestamp = timestamp.AddMilliseconds(1),
                });

            var result = await RunHlogInspectAsync(rootDir, pluginId, "json", summaryOutputPath: summaryFile, summaryIncludeRecords: "last", summaryRecordFields: "timestamp,message", summaryRecordsFormat: "csv", summaryRecordsOutputPath: summaryRecordsFile, sort: "asc");

            Assert.True(result.ExitCode == 0, $"HlogInspect failed with exit code {result.ExitCode}.{Environment.NewLine}STDERR:{Environment.NewLine}{result.StandardError}{Environment.NewLine}STDOUT:{Environment.NewLine}{result.StandardOutput}");
            var csvText = await File.ReadAllTextAsync(summaryRecordsFile);
            Assert.Contains("timestamp,message", csvText, StringComparison.Ordinal);
            Assert.Contains("sample output csv last", csvText, StringComparison.Ordinal);
            Assert.DoesNotContain(",level,", csvText, StringComparison.Ordinal);
            Assert.Contains("summaryRecordsFormat=csv", result.StandardError, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            if (Directory.Exists(rootDir))
                Directory.Delete(rootDir, recursive: true);
        }
    }

    [Fact]
    public async Task HlogInspect_WritesSummaryRecordsToTemplatedFile_WhenSummaryRecordsTemplateIsProvided()
    {
        var rootDir = Path.Combine(Path.GetTempPath(), $"hlog_inspect_summary_records_template_{Guid.NewGuid():N}");
        var pluginId = "inspect-summary-records-template";
        var timestamp = DateTimeOffset.UtcNow;
        var summaryFile = Path.Combine(rootDir, "exports", "records.summary.json");
        var expectedRecordsFile = Path.Combine(rootDir, "exports", "records.summary-last.ndjson");

        try
        {
            await SeedDirectLogAsync(
                rootDir,
                pluginId,
                new LogMessage
                {
                    Level = LogPipeLevel.Info,
                    Message = "sample template first",
                    Source = "OrdersService",
                    Service = "api-gateway",
                    Timestamp = timestamp,
                });

            await SeedDirectLogAsync(
                rootDir,
                pluginId,
                new LogMessage
                {
                    Level = LogPipeLevel.Error,
                    Message = "sample template last",
                    Source = "OrdersService",
                    Service = "api-gateway",
                    Timestamp = timestamp.AddMilliseconds(1),
                });

            var result = await RunHlogInspectAsync(rootDir, pluginId, "json", summaryOutputPath: summaryFile, summaryIncludeRecords: "last", summaryRecordFields: "timestamp,message", summaryRecordsFormat: "ndjson", summaryRecordsOutputTemplate: "{name}-{mode}{ext}", sort: "asc");

            Assert.True(result.ExitCode == 0, $"HlogInspect failed with exit code {result.ExitCode}.{Environment.NewLine}STDERR:{Environment.NewLine}{result.StandardError}{Environment.NewLine}STDOUT:{Environment.NewLine}{result.StandardOutput}");
            Assert.True(File.Exists(expectedRecordsFile), $"Expected summary records output file to exist: {expectedRecordsFile}");
            var lines = (await File.ReadAllLinesAsync(expectedRecordsFile)).Where(line => !string.IsNullOrWhiteSpace(line)).ToArray();
            Assert.Single(lines);
            using var recordDocument = JsonDocument.Parse(lines[0]);
            Assert.Equal("sample template last", recordDocument.RootElement.GetProperty("message").GetString());
            Assert.Contains("summaryRecordsTemplate={name}-{mode}{ext}", result.StandardError, StringComparison.Ordinal);
        }
        finally
        {
            if (Directory.Exists(rootDir))
                Directory.Delete(rootDir, recursive: true);
        }
    }

    [Fact]
    public async Task HlogInspect_WritesJsonManifestForSingleOutput_WhenManifestOutputIsProvidedWithoutSplit()
    {
        var rootDir = Path.Combine(Path.GetTempPath(), $"hlog_inspect_single_manifest_json_{Guid.NewGuid():N}");
        var pluginId = "inspect-single-manifest-json";
        var timestamp = DateTimeOffset.UtcNow;
        var outputFile = Path.Combine(rootDir, "exports", "records.json");
        var manifestFile = Path.Combine(rootDir, "exports", "records.manifest.json");

        try
        {
            await SeedDirectLogAsync(
                rootDir,
                pluginId,
                new LogMessage
                {
                    Level = LogPipeLevel.Info,
                    Message = "single manifest first",
                    Source = "OrdersService",
                    Service = "api-gateway",
                    Timestamp = timestamp,
                });

            await SeedDirectLogAsync(
                rootDir,
                pluginId,
                new LogMessage
                {
                    Level = LogPipeLevel.Warn,
                    Message = "single manifest second",
                    Source = "OrdersService",
                    Service = "api-gateway",
                    Timestamp = timestamp.AddMilliseconds(1),
                });

            var result = await RunHlogInspectAsync(rootDir, pluginId, "json", outputPath: outputFile, manifestOutputPath: manifestFile, sort: "asc");

            Assert.True(result.ExitCode == 0, $"HlogInspect failed with exit code {result.ExitCode}.{Environment.NewLine}STDERR:{Environment.NewLine}{result.StandardError}{Environment.NewLine}STDOUT:{Environment.NewLine}{result.StandardOutput}");
            using var manifestDocument = JsonDocument.Parse(await File.ReadAllTextAsync(manifestFile));
            var root = manifestDocument.RootElement;
            Assert.Equal("json", root.GetProperty("Format").GetString());
            Assert.Equal("records.json", root.GetProperty("OutputFile").GetString());
            Assert.Equal(JsonValueKind.Null, root.GetProperty("SplitOutputSizeBytes").ValueKind);
            var parts = root.GetProperty("Parts");
            Assert.Equal(1, parts.GetArrayLength());
            Assert.Equal("records.json", parts[0].GetProperty("FileName").GetString());
            Assert.Equal(2, parts[0].GetProperty("RecordCount").GetInt32());
            Assert.Contains("manifestOutput=", result.StandardError, StringComparison.Ordinal);
        }
        finally
        {
            if (Directory.Exists(rootDir))
                Directory.Delete(rootDir, recursive: true);
        }
    }

    [Fact]
    public async Task HlogInspect_WritesNdjsonManifestWithTotalsForSingleOutput_WhenManifestIncludeTotalsIsProvided()
    {
        var rootDir = Path.Combine(Path.GetTempPath(), $"hlog_inspect_single_manifest_ndjson_{Guid.NewGuid():N}");
        var pluginId = "inspect-single-manifest-ndjson";
        var timestamp = DateTimeOffset.UtcNow;
        var outputFile = Path.Combine(rootDir, "exports", "records.ndjson");
        var manifestFile = Path.Combine(rootDir, "exports", "records.manifest.ndjson");

        try
        {
            await SeedDirectLogAsync(
                rootDir,
                pluginId,
                new LogMessage
                {
                    Level = LogPipeLevel.Info,
                    Message = "single manifest ndjson first",
                    Source = "OrdersService",
                    Service = "api-gateway",
                    Timestamp = timestamp,
                });

            await SeedDirectLogAsync(
                rootDir,
                pluginId,
                new LogMessage
                {
                    Level = LogPipeLevel.Error,
                    Message = "single manifest ndjson second",
                    Source = "OrdersService",
                    Service = "api-gateway",
                    Timestamp = timestamp.AddMilliseconds(1),
                });

            var result = await RunHlogInspectAsync(rootDir, pluginId, "ndjson", fields: "triage", outputPath: outputFile, manifestOutputPath: manifestFile, manifestFormat: "ndjson", manifestIncludeTotals: true, sort: "asc");

            Assert.True(result.ExitCode == 0, $"HlogInspect failed with exit code {result.ExitCode}.{Environment.NewLine}STDERR:{Environment.NewLine}{result.StandardError}{Environment.NewLine}STDOUT:{Environment.NewLine}{result.StandardOutput}");
            var manifestLines = (await File.ReadAllLinesAsync(manifestFile)).Where(line => !string.IsNullOrWhiteSpace(line)).ToArray();
            Assert.Equal(2, manifestLines.Length);
            using var partLine = JsonDocument.Parse(manifestLines[0]);
            using var totalsLine = JsonDocument.Parse(manifestLines[1]);
            Assert.Equal("part", partLine.RootElement.GetProperty("Type").GetString());
            Assert.Equal("records.ndjson", partLine.RootElement.GetProperty("Part").GetProperty("FileName").GetString());
            Assert.Equal("totals", totalsLine.RootElement.GetProperty("Type").GetString());
            Assert.Equal(1, totalsLine.RootElement.GetProperty("PartCount").GetInt32());
            Assert.Equal(2, totalsLine.RootElement.GetProperty("RecordCount").GetInt32());
            Assert.Equal(JsonValueKind.Null, totalsLine.RootElement.GetProperty("SplitOutputSizeBytes").ValueKind);
        }
        finally
        {
            if (Directory.Exists(rootDir))
                Directory.Delete(rootDir, recursive: true);
        }
    }

    [Fact]
    public async Task HlogInspect_WritesManifestWithOutputHashes_WhenManifestIncludeOutputHashIsProvided()
    {
        var rootDir = Path.Combine(Path.GetTempPath(), $"hlog_inspect_manifest_hash_{Guid.NewGuid():N}");
        var pluginId = "inspect-manifest-hash";
        var timestamp = DateTimeOffset.UtcNow;
        var outputFile = Path.Combine(rootDir, "exports", "records.ndjson");
        var manifestFile = Path.Combine(rootDir, "exports", "records.manifest.json");

        try
        {
            await SeedDirectLogAsync(
                rootDir,
                pluginId,
                new LogMessage
                {
                    Level = LogPipeLevel.Info,
                    Message = "manifest hash first",
                    Source = "OrdersService",
                    Service = "api-gateway",
                    Timestamp = timestamp,
                });

            await SeedDirectLogAsync(
                rootDir,
                pluginId,
                new LogMessage
                {
                    Level = LogPipeLevel.Warn,
                    Message = "manifest hash second",
                    Source = "OrdersService",
                    Service = "api-gateway",
                    Timestamp = timestamp.AddMilliseconds(1),
                });

            var result = await RunHlogInspectAsync(rootDir, pluginId, "ndjson", fields: "triage", outputPath: outputFile, manifestOutputPath: manifestFile, manifestIncludeOutputHash: true, sort: "asc");

            Assert.True(result.ExitCode == 0, $"HlogInspect failed with exit code {result.ExitCode}.{Environment.NewLine}STDERR:{Environment.NewLine}{result.StandardError}{Environment.NewLine}STDOUT:{Environment.NewLine}{result.StandardOutput}");
            using var manifestDocument = JsonDocument.Parse(await File.ReadAllTextAsync(manifestFile));
            var part = manifestDocument.RootElement.GetProperty("Parts")[0];
            var recordedHash = part.GetProperty("OutputHashSha256").GetString();
            Assert.False(string.IsNullOrWhiteSpace(recordedHash));
            Assert.Equal(64, recordedHash!.Length);
            Assert.Contains("manifestIncludeOutputHash=True", result.StandardError, StringComparison.Ordinal);
        }
        finally
        {
            if (Directory.Exists(rootDir))
                Directory.Delete(rootDir, recursive: true);
        }
    }

    [Fact]
    public async Task HlogInspect_WritesOutputHashesAtManifestRoot_WhenJsonManifestIncludesOutputHash()
    {
        var rootDir = Path.Combine(Path.GetTempPath(), $"hlog_inspect_manifest_root_hash_{Guid.NewGuid():N}");
        var pluginId = "inspect-manifest-root-hash";
        var timestamp = DateTimeOffset.UtcNow;
        var outputFile = Path.Combine(rootDir, "exports", "records.ndjson");
        var manifestFile = Path.Combine(rootDir, "exports", "records.manifest.json");

        try
        {
            await SeedDirectLogAsync(
                rootDir,
                pluginId,
                new LogMessage
                {
                    Level = LogPipeLevel.Info,
                    Message = "manifest root hash first",
                    Source = "OrdersService",
                    Service = "api-gateway",
                    Timestamp = timestamp,
                });

            var result = await RunHlogInspectAsync(rootDir, pluginId, "ndjson", fields: "triage", outputPath: outputFile, manifestOutputPath: manifestFile, manifestIncludeOutputHash: true, sort: "asc");

            Assert.True(result.ExitCode == 0, $"HlogInspect failed with exit code {result.ExitCode}.{Environment.NewLine}STDERR:{Environment.NewLine}{result.StandardError}{Environment.NewLine}STDOUT:{Environment.NewLine}{result.StandardOutput}");
            using var manifestDocument = JsonDocument.Parse(await File.ReadAllTextAsync(manifestFile));
            var hashes = manifestDocument.RootElement.GetProperty("OutputHashesSha256");
            var property = hashes.EnumerateObject().Single();
            Assert.Equal("records.ndjson", property.Name);
            Assert.Equal(64, property.Value.GetString()!.Length);
        }
        finally
        {
            if (Directory.Exists(rootDir))
                Directory.Delete(rootDir, recursive: true);
        }
    }

    [Fact]
    public async Task HlogInspect_WritesOutputHashesIntoTotalsRecord_WhenManifestIncludesTotalsAndOutputHash()
    {
        var rootDir = Path.Combine(Path.GetTempPath(), $"hlog_inspect_manifest_totals_hash_{Guid.NewGuid():N}");
        var pluginId = "inspect-manifest-totals-hash";
        var timestamp = DateTimeOffset.UtcNow;
        var outputFile = Path.Combine(rootDir, "exports", "records.ndjson");
        var manifestFile = Path.Combine(rootDir, "exports", "records.manifest.ndjson");

        try
        {
            await SeedDirectLogAsync(
                rootDir,
                pluginId,
                new LogMessage
                {
                    Level = LogPipeLevel.Info,
                    Message = "manifest totals hash first",
                    Source = "OrdersService",
                    Service = "api-gateway",
                    Timestamp = timestamp,
                });

            await SeedDirectLogAsync(
                rootDir,
                pluginId,
                new LogMessage
                {
                    Level = LogPipeLevel.Error,
                    Message = "manifest totals hash second",
                    Source = "OrdersService",
                    Service = "api-gateway",
                    Timestamp = timestamp.AddMilliseconds(1),
                });

            var result = await RunHlogInspectAsync(rootDir, pluginId, "ndjson", fields: "triage", outputPath: outputFile, manifestOutputPath: manifestFile, manifestFormat: "ndjson", manifestIncludeTotals: true, manifestIncludeOutputHash: true, sort: "asc");

            Assert.True(result.ExitCode == 0, $"HlogInspect failed with exit code {result.ExitCode}.{Environment.NewLine}STDERR:{Environment.NewLine}{result.StandardError}{Environment.NewLine}STDOUT:{Environment.NewLine}{result.StandardOutput}");
            var manifestLines = (await File.ReadAllLinesAsync(manifestFile)).Where(line => !string.IsNullOrWhiteSpace(line)).ToArray();
            Assert.Equal(2, manifestLines.Length);
            using var totalsLine = JsonDocument.Parse(manifestLines[1]);
            var hashMap = totalsLine.RootElement.GetProperty("OutputHashesSha256");
            Assert.Equal(JsonValueKind.Object, hashMap.ValueKind);
            var hashProperty = hashMap.EnumerateObject().Single();
            Assert.Equal("records.ndjson", hashProperty.Name);
            Assert.Equal(64, hashProperty.Value.GetString()!.Length);
        }
        finally
        {
            if (Directory.Exists(rootDir))
                Directory.Delete(rootDir, recursive: true);
        }
    }

    [Fact]
    public async Task HlogInspect_ReturnsError_WhenManifestIncludeOutputHashIsUsedWithoutManifestOutput()
    {
        var rootDir = Path.Combine(Path.GetTempPath(), $"hlog_inspect_manifest_hash_requires_manifest_{Guid.NewGuid():N}");
        var pluginId = "inspect-manifest-hash-requires-manifest";

        try
        {
            var result = await RunHlogInspectAsync(rootDir, pluginId, "json", manifestIncludeOutputHash: true);

            Assert.Equal(1, result.ExitCode);
            Assert.Contains("'--manifest-include-output-hash' requires '--manifest-output'.", result.StandardError, StringComparison.Ordinal);
        }
        finally
        {
            if (Directory.Exists(rootDir))
                Directory.Delete(rootDir, recursive: true);
        }
    }

    [Fact]
    public async Task HlogInspect_WritesManifestFiltersAndCommandInJson_WhenRequested()
    {
        var rootDir = Path.Combine(Path.GetTempPath(), $"hlog_inspect_manifest_filters_json_{Guid.NewGuid():N}");
        var pluginId = "inspect-manifest-filters-json";
        var timestamp = DateTimeOffset.UtcNow;
        var outputFile = Path.Combine(rootDir, "exports", "records.ndjson");
        var manifestFile = Path.Combine(rootDir, "exports", "records.manifest.json");

        try
        {
            await SeedDirectLogAsync(
                rootDir,
                pluginId,
                new LogMessage
                {
                    Level = LogPipeLevel.Warn,
                    Message = "orders timeout reached",
                    Source = "OrdersService",
                    Service = "api-gateway",
                    Timestamp = timestamp,
                });

            var result = await RunHlogInspectAsync(rootDir, pluginId, "ndjson", source: "OrdersService", contains: "timeout,orders", containsField: "message", containsMode: "all", outputPath: outputFile, manifestOutputPath: manifestFile, manifestIncludeFilters: true, manifestIncludeCommand: true, sort: "asc");

            Assert.True(result.ExitCode == 0, $"HlogInspect failed with exit code {result.ExitCode}.{Environment.NewLine}STDERR:{Environment.NewLine}{result.StandardError}{Environment.NewLine}STDOUT:{Environment.NewLine}{result.StandardOutput}");
            using var manifestDocument = JsonDocument.Parse(await File.ReadAllTextAsync(manifestFile));
            var filters = manifestDocument.RootElement.GetProperty("Filters");
            Assert.Equal("OrdersService", filters.GetProperty("Source").GetString());
            Assert.Equal("timeout,orders", filters.GetProperty("Contains").GetString());
            Assert.Equal("message", filters.GetProperty("ContainsField").GetString());
            Assert.Equal("all", filters.GetProperty("ContainsMode").GetString());
            Assert.Contains("--manifest-include-command", manifestDocument.RootElement.GetProperty("CommandLine").GetString(), StringComparison.Ordinal);
            Assert.Contains("manifestIncludeFilters=True", result.StandardError, StringComparison.Ordinal);
            Assert.Contains("manifestIncludeCommand=True", result.StandardError, StringComparison.Ordinal);
        }
        finally
        {
            if (Directory.Exists(rootDir))
                Directory.Delete(rootDir, recursive: true);
        }
    }

    [Fact]
    public async Task HlogInspect_WritesManifestFiltersAndCommandInNdjsonTotals_WhenRequested()
    {
        var rootDir = Path.Combine(Path.GetTempPath(), $"hlog_inspect_manifest_filters_ndjson_{Guid.NewGuid():N}");
        var pluginId = "inspect-manifest-filters-ndjson";
        var timestamp = DateTimeOffset.UtcNow;
        var outputFile = Path.Combine(rootDir, "exports", "records.ndjson");
        var manifestFile = Path.Combine(rootDir, "exports", "records.manifest.ndjson");

        try
        {
            await SeedDirectLogAsync(
                rootDir,
                pluginId,
                new LogMessage
                {
                    Level = LogPipeLevel.Info,
                    Message = "orders timeout reached",
                    Source = "OrdersService",
                    Service = "api-gateway",
                    Timestamp = timestamp,
                });

            var result = await RunHlogInspectAsync(rootDir, pluginId, "ndjson", source: "OrdersService", contains: "timeout,orders", containsField: "message", containsMode: "all", outputPath: outputFile, manifestOutputPath: manifestFile, manifestFormat: "ndjson", manifestIncludeTotals: true, manifestIncludeFilters: true, manifestIncludeCommand: true, sort: "asc");

            Assert.True(result.ExitCode == 0, $"HlogInspect failed with exit code {result.ExitCode}.{Environment.NewLine}STDERR:{Environment.NewLine}{result.StandardError}{Environment.NewLine}STDOUT:{Environment.NewLine}{result.StandardOutput}");
            var manifestLines = (await File.ReadAllLinesAsync(manifestFile)).Where(line => !string.IsNullOrWhiteSpace(line)).ToArray();
            using var totalsDocument = JsonDocument.Parse(manifestLines[^1]);
            var filters = totalsDocument.RootElement.GetProperty("Filters");
            Assert.Equal("message", filters.GetProperty("ContainsField").GetString());
            Assert.Equal("all", filters.GetProperty("ContainsMode").GetString());
            Assert.Contains("--manifest-include-filters", totalsDocument.RootElement.GetProperty("CommandLine").GetString(), StringComparison.Ordinal);
        }
        finally
        {
            if (Directory.Exists(rootDir))
                Directory.Delete(rootDir, recursive: true);
        }
    }

    [Fact]
    public async Task HlogInspect_ReturnsError_WhenManifestIncludeFiltersIsUsedWithoutTotalsForNdjson()
    {
        var rootDir = Path.Combine(Path.GetTempPath(), $"hlog_inspect_manifest_filters_requires_totals_{Guid.NewGuid():N}");
        var pluginId = "inspect-manifest-filters-requires-totals";
        var outputFile = Path.Combine(rootDir, "exports", "records.ndjson");
        var manifestFile = Path.Combine(rootDir, "exports", "records.manifest.ndjson");

        try
        {
            var result = await RunHlogInspectAsync(rootDir, pluginId, "ndjson", outputPath: outputFile, manifestOutputPath: manifestFile, manifestFormat: "ndjson", manifestIncludeFilters: true);

            Assert.Equal(1, result.ExitCode);
            Assert.Contains("'--manifest-include-filters' requires '--manifest-include-totals' when '--manifest-format ndjson' is used.", result.StandardError, StringComparison.Ordinal);
        }
        finally
        {
            if (Directory.Exists(rootDir))
                Directory.Delete(rootDir, recursive: true);
        }
    }

    [Fact]
    public async Task HlogInspect_ReturnsHelpHint_WhenManifestFormatIsInvalid()
    {
        var rootDir = Path.Combine(Path.GetTempPath(), $"hlog_inspect_manifest_format_invalid_{Guid.NewGuid():N}");
        var pluginId = "inspect-manifest-format-invalid";
        var outputFile = Path.Combine(rootDir, "exports", "records.ndjson");
        var manifestFile = Path.Combine(rootDir, "exports", "records.manifest.json");

        try
        {
            var result = await RunHlogInspectAsync(rootDir, pluginId, "ndjson", outputPath: outputFile, manifestOutputPath: manifestFile, manifestFormat: "yaml");

            Assert.Equal(1, result.ExitCode);
            Assert.Contains("Invalid manifest format for --manifest-format: 'yaml'. Use Json or Ndjson. Use '--help' to see supported options.", result.StandardError, StringComparison.Ordinal);
        }
        finally
        {
            if (Directory.Exists(rootDir))
                Directory.Delete(rootDir, recursive: true);
        }
    }

    [Fact]
    public async Task HlogInspect_ReturnsHelpHint_WhenTemplateNumberStyleIsInvalid()
    {
        var rootDir = Path.Combine(Path.GetTempPath(), $"hlog_inspect_template_number_style_invalid_{Guid.NewGuid():N}");
        var pluginId = "inspect-template-number-style-invalid";
        var outputFile = Path.Combine(rootDir, "exports", "records.ndjson");

        try
        {
            var result = await RunHlogInspectAsync(rootDir, pluginId, "ndjson", outputPath: outputFile, splitOutputSizeBytes: 180, splitOutputTemplate: "chunks/{name}-chunk-{part}{ext}", templateNumberStyle: "compact");

            Assert.Equal(1, result.ExitCode);
            Assert.Contains("Invalid template number style for --template-number-style: 'compact'. Use Padded or Plain. Use '--help' to see supported options.", result.StandardError, StringComparison.Ordinal);
        }
        finally
        {
            if (Directory.Exists(rootDir))
                Directory.Delete(rootDir, recursive: true);
        }
    }

    [Fact]
    public async Task HlogInspect_ReturnsError_WhenSummaryRecordsTemplateIsCombinedWithSummaryRecordsOutput()
    {
        var rootDir = Path.Combine(Path.GetTempPath(), $"hlog_inspect_summary_template_conflict_{Guid.NewGuid():N}");
        var pluginId = "inspect-summary-template-conflict";
        var summaryFile = Path.Combine(rootDir, "exports", "records.summary.json");
        var summaryRecordsFile = Path.Combine(rootDir, "exports", "records.sample.json");

        try
        {
            var result = await RunHlogInspectAsync(rootDir, pluginId, "json", summaryOutputPath: summaryFile, summaryIncludeRecords: "last", summaryRecordsOutputPath: summaryRecordsFile, summaryRecordsOutputTemplate: "{name}-{mode}{ext}");

            Assert.Equal(1, result.ExitCode);
            Assert.Contains("'--summary-records-template' cannot be combined with '--summary-records-output'.", result.StandardError, StringComparison.Ordinal);
        }
        finally
        {
            if (Directory.Exists(rootDir))
                Directory.Delete(rootDir, recursive: true);
        }
    }

    [Fact]
    public async Task HlogInspect_ReturnsError_WhenSummaryRecordsTemplateContainsUnknownToken()
    {
        var rootDir = Path.Combine(Path.GetTempPath(), $"hlog_inspect_summary_template_invalid_token_{Guid.NewGuid():N}");
        var pluginId = "inspect-summary-template-invalid-token";
        var summaryFile = Path.Combine(rootDir, "exports", "records.summary.json");

        try
        {
            var result = await RunHlogInspectAsync(rootDir, pluginId, "json", summaryOutputPath: summaryFile, summaryIncludeRecords: "last", summaryRecordsOutputTemplate: "{name}-{unknown}{ext}");

            Assert.Equal(1, result.ExitCode);
            Assert.Contains("Invalid token '{unknown}' for '--summary-records-template'.", result.StandardError, StringComparison.Ordinal);
        }
        finally
        {
            if (Directory.Exists(rootDir))
                Directory.Delete(rootDir, recursive: true);
        }
    }

    [Fact]
    public async Task HlogInspect_ReturnsHelpHint_WhenFormatIsInvalid()
    {
        var rootDir = Path.Combine(Path.GetTempPath(), $"hlog_inspect_format_invalid_{Guid.NewGuid():N}");
        var pluginId = "inspect-format-invalid";

        try
        {
            var result = await RunHlogInspectAsync(rootDir, pluginId, "xml");

            Assert.Equal(1, result.ExitCode);
            Assert.Contains("Invalid format for --format: 'xml'. Use Table, Json, Ndjson or Csv. Use '--help' to see supported options.", result.StandardError, StringComparison.Ordinal);
        }
        finally
        {
            if (Directory.Exists(rootDir))
                Directory.Delete(rootDir, recursive: true);
        }
    }

    [Fact]
    public async Task HlogInspect_ReturnsHelpHint_WhenSummaryFormatIsInvalid()
    {
        var rootDir = Path.Combine(Path.GetTempPath(), $"hlog_inspect_summary_format_invalid_{Guid.NewGuid():N}");
        var pluginId = "inspect-summary-format-invalid";
        var summaryFile = Path.Combine(rootDir, "exports", "records.summary.json");

        try
        {
            var result = await RunHlogInspectAsync(rootDir, pluginId, "json", summaryOutputPath: summaryFile, summaryFormat: "yaml");

            Assert.Equal(1, result.ExitCode);
            Assert.Contains("Invalid summary format for --summary-format: 'yaml'. Use Json or Table. Use '--help' to see supported options.", result.StandardError, StringComparison.Ordinal);
        }
        finally
        {
            if (Directory.Exists(rootDir))
                Directory.Delete(rootDir, recursive: true);
        }
    }

    [Fact]
    public async Task HlogInspect_ReturnsHelpHint_WhenSummaryGroupOrderIsInvalid()
    {
        var rootDir = Path.Combine(Path.GetTempPath(), $"hlog_inspect_summary_group_order_invalid_{Guid.NewGuid():N}");
        var pluginId = "inspect-summary-group-order-invalid";
        var summaryFile = Path.Combine(rootDir, "exports", "records.summary.json");

        try
        {
            var result = await RunHlogInspectAsync(rootDir, pluginId, "json", summaryOutputPath: summaryFile, summaryGroupBy: "service", summaryGroupOrder: "frequency");

            Assert.Equal(1, result.ExitCode);
            Assert.Contains("Invalid summary group order for --summary-group-order: 'frequency'. Use Count or Key. Use '--help' to see supported options.", result.StandardError, StringComparison.Ordinal);
        }
        finally
        {
            if (Directory.Exists(rootDir))
                Directory.Delete(rootDir, recursive: true);
        }
    }

    [Fact]
    public async Task HlogInspect_ReturnsHelpHint_WhenSummaryIncludeRecordsIsInvalid()
    {
        var rootDir = Path.Combine(Path.GetTempPath(), $"hlog_inspect_summary_include_invalid_{Guid.NewGuid():N}");
        var pluginId = "inspect-summary-include-invalid";
        var summaryFile = Path.Combine(rootDir, "exports", "records.summary.json");

        try
        {
            var result = await RunHlogInspectAsync(rootDir, pluginId, "json", summaryOutputPath: summaryFile, summaryIncludeRecords: "middle");

            Assert.Equal(1, result.ExitCode);
            Assert.Contains("Invalid summary include mode for --summary-include-records: 'middle'. Use First or Last. Use '--help' to see supported options.", result.StandardError, StringComparison.Ordinal);
        }
        finally
        {
            if (Directory.Exists(rootDir))
                Directory.Delete(rootDir, recursive: true);
        }
    }

    [Fact]
    public async Task HlogInspect_ReturnsHelpHint_WhenSummaryRecordsFormatIsInvalid()
    {
        var rootDir = Path.Combine(Path.GetTempPath(), $"hlog_inspect_summary_records_format_invalid_{Guid.NewGuid():N}");
        var pluginId = "inspect-summary-records-format-invalid";
        var summaryFile = Path.Combine(rootDir, "exports", "records.summary.json");

        try
        {
            var result = await RunHlogInspectAsync(rootDir, pluginId, "json", summaryOutputPath: summaryFile, summaryIncludeRecords: "last", summaryRecordsFormat: "xml");

            Assert.Equal(1, result.ExitCode);
            Assert.Contains("Invalid summary records format for --summary-records-format: 'xml'. Use Json, Table, Ndjson or Csv. Use '--help' to see supported options.", result.StandardError, StringComparison.Ordinal);
        }
        finally
        {
            if (Directory.Exists(rootDir))
                Directory.Delete(rootDir, recursive: true);
        }
    }

    [Fact]
    public async Task HlogInspect_ReturnsError_WhenSplitOutputTemplateContainsUnknownToken()
    {
        var rootDir = Path.Combine(Path.GetTempPath(), $"hlog_inspect_split_template_invalid_token_{Guid.NewGuid():N}");
        var pluginId = "inspect-split-template-invalid-token";
        var outputFile = Path.Combine(rootDir, "exports", "records.ndjson");

        try
        {
            var result = await RunHlogInspectAsync(rootDir, pluginId, "ndjson", outputPath: outputFile, splitOutputSizeBytes: 256, splitOutputTemplate: "chunks/{name}-{unknown}{ext}");

            Assert.Equal(1, result.ExitCode);
            Assert.Contains("Invalid token '{unknown}' for '--split-output-template'.", result.StandardError, StringComparison.Ordinal);
        }
        finally
        {
            if (Directory.Exists(rootDir))
                Directory.Delete(rootDir, recursive: true);
        }
    }

    [Fact]
    public async Task HlogInspect_WritesSummaryRecordsToTemplatedFile_WithPlainCountToken()
    {
        var rootDir = Path.Combine(Path.GetTempPath(), $"hlog_inspect_summary_template_count_{Guid.NewGuid():N}");
        var pluginId = "inspect-summary-template-count";
        var timestamp = DateTimeOffset.UtcNow;
        var summaryFile = Path.Combine(rootDir, "exports", "records.summary.json");

        try
        {
            await SeedDirectLogAsync(
                rootDir,
                pluginId,
                new LogMessage
                {
                    Level = LogPipeLevel.Info,
                    Message = "summary count first",
                    Source = "OrdersService",
                    Service = "api-gateway",
                    Timestamp = timestamp,
                });

            await SeedDirectLogAsync(
                rootDir,
                pluginId,
                new LogMessage
                {
                    Level = LogPipeLevel.Warn,
                    Message = "summary count second",
                    Source = "OrdersService",
                    Service = "api-gateway",
                    Timestamp = timestamp.AddMilliseconds(1),
                });

            var result = await RunHlogInspectAsync(rootDir, pluginId, "json", summaryOutputPath: summaryFile, summaryIncludeRecords: "last", summaryRecordCount: 2, summaryRecordFields: "timestamp,message", summaryRecordsFormat: "ndjson", summaryRecordsOutputTemplate: "{name}-{mode}-{count}{ext}", templateNumberStyle: "plain", sort: "asc");

            Assert.True(result.ExitCode == 0, $"HlogInspect failed with exit code {result.ExitCode}.{Environment.NewLine}STDERR:{Environment.NewLine}{result.StandardError}{Environment.NewLine}STDOUT:{Environment.NewLine}{result.StandardOutput}");
            var summaryRecordsFile = Path.Combine(rootDir, "exports", "records.summary-last-2.ndjson");
            Assert.True(File.Exists(summaryRecordsFile), $"Expected templated summary records file to exist: {summaryRecordsFile}");
            Assert.Contains("summaryRecordsTemplate={name}-{mode}-{count}{ext}", result.StandardError, StringComparison.Ordinal);
            Assert.Contains("templateNumberStyle=plain", result.StandardError, StringComparison.Ordinal);
        }
        finally
        {
            if (Directory.Exists(rootDir))
                Directory.Delete(rootDir, recursive: true);
        }
    }

    [Fact]
    public async Task HlogInspect_WritesSummaryGroupsInJson_WhenSummaryGroupByIsProvided()
    {
        var rootDir = Path.Combine(Path.GetTempPath(), $"hlog_inspect_summary_group_json_{Guid.NewGuid():N}");
        var pluginId = "inspect-summary-group-json";
        var timestamp = DateTimeOffset.UtcNow;
        var summaryFile = Path.Combine(rootDir, "exports", "records.summary.json");

        try
        {
            await SeedDirectLogAsync(
                rootDir,
                pluginId,
                new LogMessage
                {
                    Level = LogPipeLevel.Info,
                    Message = "service first",
                    Source = "OrdersService",
                    Service = "api-gateway",
                    Timestamp = timestamp,
                });

            await SeedDirectLogAsync(
                rootDir,
                pluginId,
                new LogMessage
                {
                    Level = LogPipeLevel.Warn,
                    Message = "service second",
                    Source = "BillingService",
                    Service = "worker",
                    Timestamp = timestamp.AddMilliseconds(1),
                });

            await SeedDirectLogAsync(
                rootDir,
                pluginId,
                new LogMessage
                {
                    Level = LogPipeLevel.Error,
                    Message = "service third",
                    Source = "PaymentsService",
                    Service = "worker",
                    Timestamp = timestamp.AddMilliseconds(2),
                });

            var result = await RunHlogInspectAsync(rootDir, pluginId, "json", summaryOutputPath: summaryFile, summaryGroupBy: "service", sort: "asc");

            Assert.True(result.ExitCode == 0, $"HlogInspect failed with exit code {result.ExitCode}.{Environment.NewLine}STDERR:{Environment.NewLine}{result.StandardError}{Environment.NewLine}STDOUT:{Environment.NewLine}{result.StandardOutput}");
            using var summaryDocument = JsonDocument.Parse(await File.ReadAllTextAsync(summaryFile));
            Assert.Equal("service", summaryDocument.RootElement.GetProperty("GroupBy").GetString());
            var groups = summaryDocument.RootElement.GetProperty("Groups");
            Assert.Equal(2, groups.GetArrayLength());
            Assert.Equal("worker", groups[0].GetProperty("Key").GetString());
            Assert.Equal(2, groups[0].GetProperty("Count").GetInt32());
            Assert.Equal("api-gateway", groups[1].GetProperty("Key").GetString());
            Assert.Equal(1, groups[1].GetProperty("Count").GetInt32());
            Assert.Contains("summaryGroupBy=service", result.StandardError, StringComparison.Ordinal);
        }
        finally
        {
            if (Directory.Exists(rootDir))
                Directory.Delete(rootDir, recursive: true);
        }
    }

    [Fact]
    public async Task HlogInspect_WritesSummaryGroupsInTable_WhenSummaryGroupByIsProvided()
    {
        var rootDir = Path.Combine(Path.GetTempPath(), $"hlog_inspect_summary_group_table_{Guid.NewGuid():N}");
        var pluginId = "inspect-summary-group-table";
        var timestamp = DateTimeOffset.UtcNow;
        var summaryFile = Path.Combine(rootDir, "exports", "records.summary.txt");

        try
        {
            await SeedDirectLogAsync(
                rootDir,
                pluginId,
                new LogMessage
                {
                    Level = LogPipeLevel.Warn,
                    Message = "level first",
                    Source = "OrdersService",
                    Service = "api-gateway",
                    Timestamp = timestamp,
                });

            await SeedDirectLogAsync(
                rootDir,
                pluginId,
                new LogMessage
                {
                    Level = LogPipeLevel.Error,
                    Message = "level second",
                    Source = "OrdersService",
                    Service = "api-gateway",
                    Timestamp = timestamp.AddMilliseconds(1),
                });

            await SeedDirectLogAsync(
                rootDir,
                pluginId,
                new LogMessage
                {
                    Level = LogPipeLevel.Error,
                    Message = "level third",
                    Source = "OrdersService",
                    Service = "api-gateway",
                    Timestamp = timestamp.AddMilliseconds(2),
                });

            var result = await RunHlogInspectAsync(rootDir, pluginId, "json", summaryOutputPath: summaryFile, summaryFormat: "table", summaryGroupBy: "level", sort: "asc");

            Assert.True(result.ExitCode == 0, $"HlogInspect failed with exit code {result.ExitCode}.{Environment.NewLine}STDERR:{Environment.NewLine}{result.StandardError}{Environment.NewLine}STDOUT:{Environment.NewLine}{result.StandardOutput}");
            var summaryContent = await File.ReadAllTextAsync(summaryFile);
            Assert.Contains("groupBy=level count=2", summaryContent, StringComparison.Ordinal);
            Assert.Contains("group key=Warn count=1", summaryContent, StringComparison.Ordinal);
            Assert.Contains("group key=Error count=2", summaryContent, StringComparison.Ordinal);
        }
        finally
        {
            if (Directory.Exists(rootDir))
                Directory.Delete(rootDir, recursive: true);
        }
    }

    [Fact]
    public async Task HlogInspect_WritesSummaryGroupsWithOrderAndLimitInJson_WhenRequested()
    {
        var rootDir = Path.Combine(Path.GetTempPath(), $"hlog_inspect_summary_group_controls_{Guid.NewGuid():N}");
        var pluginId = "inspect-summary-group-controls";
        var timestamp = DateTimeOffset.UtcNow;
        var summaryFile = Path.Combine(rootDir, "exports", "records.summary.json");

        try
        {
            await SeedDirectLogAsync(rootDir, pluginId, new LogMessage { Level = LogPipeLevel.Info, Message = "alpha one", Source = "AlphaService", Service = "api", Timestamp = timestamp });
            await SeedDirectLogAsync(rootDir, pluginId, new LogMessage { Level = LogPipeLevel.Info, Message = "alpha two", Source = "AlphaService", Service = "api", Timestamp = timestamp.AddMilliseconds(1) });
            await SeedDirectLogAsync(rootDir, pluginId, new LogMessage { Level = LogPipeLevel.Warn, Message = "beta one", Source = "BetaService", Service = "worker", Timestamp = timestamp.AddMilliseconds(2) });
            await SeedDirectLogAsync(rootDir, pluginId, new LogMessage { Level = LogPipeLevel.Error, Message = "gamma one", Source = "GammaService", Service = "batch", Timestamp = timestamp.AddMilliseconds(3) });

            var result = await RunHlogInspectAsync(rootDir, pluginId, "json", summaryOutputPath: summaryFile, summaryGroupBy: "source", summaryGroupLimit: 2, summaryGroupOrder: "key", sort: "asc");

            Assert.True(result.ExitCode == 0, $"HlogInspect failed with exit code {result.ExitCode}.{Environment.NewLine}STDERR:{Environment.NewLine}{result.StandardError}{Environment.NewLine}STDOUT:{Environment.NewLine}{result.StandardOutput}");
            using var summaryDocument = JsonDocument.Parse(await File.ReadAllTextAsync(summaryFile));
            Assert.Equal(2, summaryDocument.RootElement.GetProperty("GroupLimit").GetInt32());
            Assert.Equal("key", summaryDocument.RootElement.GetProperty("GroupOrder").GetString());
            var groups = summaryDocument.RootElement.GetProperty("Groups");
            Assert.Equal(2, groups.GetArrayLength());
            Assert.Equal("AlphaService", groups[0].GetProperty("Key").GetString());
            Assert.Equal("BetaService", groups[1].GetProperty("Key").GetString());
            Assert.Contains("summaryGroupLimit=2", result.StandardError, StringComparison.Ordinal);
            Assert.Contains("summaryGroupOrder=key", result.StandardError, StringComparison.Ordinal);
        }
        finally
        {
            if (Directory.Exists(rootDir))
                Directory.Delete(rootDir, recursive: true);
        }
    }

    [Fact]
    public async Task HlogInspect_WritesSummaryGroupsByTraceIdInJson_WhenRequested()
    {
        var rootDir = Path.Combine(Path.GetTempPath(), $"hlog_inspect_summary_group_trace_{Guid.NewGuid():N}");
        var pluginId = "inspect-summary-group-trace";
        var timestamp = DateTimeOffset.UtcNow;
        var summaryFile = Path.Combine(rootDir, "exports", "records.summary.json");
        var sharedTraceId = "0123456789abcdef0123456789abcdef";
        var otherTraceId = "fedcba9876543210fedcba9876543210";

        try
        {
            await SeedDirectLogAsync(rootDir, pluginId, new LogMessage { Level = LogPipeLevel.Info, Message = "trace first", Source = "OrdersService", Service = "api", TraceId = sharedTraceId, Timestamp = timestamp });
            await SeedDirectLogAsync(rootDir, pluginId, new LogMessage { Level = LogPipeLevel.Warn, Message = "trace second", Source = "BillingService", Service = "worker", TraceId = sharedTraceId, Timestamp = timestamp.AddMilliseconds(1) });
            await SeedDirectLogAsync(rootDir, pluginId, new LogMessage { Level = LogPipeLevel.Error, Message = "trace third", Source = "PaymentsService", Service = "worker", TraceId = otherTraceId, Timestamp = timestamp.AddMilliseconds(2) });

            var result = await RunHlogInspectAsync(rootDir, pluginId, "json", summaryOutputPath: summaryFile, summaryGroupBy: "traceId", sort: "asc");

            Assert.True(result.ExitCode == 0, $"HlogInspect failed with exit code {result.ExitCode}.{Environment.NewLine}STDERR:{Environment.NewLine}{result.StandardError}{Environment.NewLine}STDOUT:{Environment.NewLine}{result.StandardOutput}");
            using var summaryDocument = JsonDocument.Parse(await File.ReadAllTextAsync(summaryFile));
            Assert.Equal("traceId", summaryDocument.RootElement.GetProperty("GroupBy").GetString());
            var groups = summaryDocument.RootElement.GetProperty("Groups");
            Assert.Equal(2, groups.GetArrayLength());
            Assert.Equal(sharedTraceId, groups[0].GetProperty("Key").GetString());
            Assert.Equal(2, groups[0].GetProperty("Count").GetInt32());
            Assert.Equal(otherTraceId, groups[1].GetProperty("Key").GetString());
            Assert.Equal(1, groups[1].GetProperty("Count").GetInt32());
            Assert.Contains("summaryGroupBy=traceid", result.StandardError, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            if (Directory.Exists(rootDir))
                Directory.Delete(rootDir, recursive: true);
        }
    }

    [Fact]
    public async Task HlogInspect_WritesSummaryGroupsByCorrelationIdInJson_WhenRequested()
    {
        var rootDir = Path.Combine(Path.GetTempPath(), $"hlog_inspect_summary_group_correlation_json_{Guid.NewGuid():N}");
        var pluginId = "inspect-summary-group-correlation-json";
        var timestamp = DateTimeOffset.UtcNow;
        var summaryFile = Path.Combine(rootDir, "exports", "records.summary.json");
        var sharedCorrelationId = "aabbccddeeff00112233445566778899";
        var otherCorrelationId = "99887766554433221100ffeeddccbbaa";

        try
        {
            await SeedDirectLogAsync(rootDir, pluginId, new LogMessage { Level = LogPipeLevel.Info, Message = "correlation first", Source = "OrdersService", Service = "api", CorrelationId = sharedCorrelationId, Timestamp = timestamp });
            await SeedDirectLogAsync(rootDir, pluginId, new LogMessage { Level = LogPipeLevel.Warn, Message = "correlation second", Source = "BillingService", Service = "worker", CorrelationId = sharedCorrelationId, Timestamp = timestamp.AddMilliseconds(1) });
            await SeedDirectLogAsync(rootDir, pluginId, new LogMessage { Level = LogPipeLevel.Error, Message = "correlation third", Source = "PaymentsService", Service = "worker", CorrelationId = otherCorrelationId, Timestamp = timestamp.AddMilliseconds(2) });

            var result = await RunHlogInspectAsync(rootDir, pluginId, "json", summaryOutputPath: summaryFile, summaryGroupBy: "correlationId", sort: "asc");

            Assert.True(result.ExitCode == 0, $"HlogInspect failed with exit code {result.ExitCode}.{Environment.NewLine}STDERR:{Environment.NewLine}{result.StandardError}{Environment.NewLine}STDOUT:{Environment.NewLine}{result.StandardOutput}");
            using var summaryDocument = JsonDocument.Parse(await File.ReadAllTextAsync(summaryFile));
            Assert.Equal("correlationId", summaryDocument.RootElement.GetProperty("GroupBy").GetString());
            var groups = summaryDocument.RootElement.GetProperty("Groups");
            Assert.Equal(2, groups.GetArrayLength());
            Assert.Equal(sharedCorrelationId, groups[0].GetProperty("Key").GetString());
            Assert.Equal(2, groups[0].GetProperty("Count").GetInt32());
            Assert.Equal(otherCorrelationId, groups[1].GetProperty("Key").GetString());
            Assert.Equal(1, groups[1].GetProperty("Count").GetInt32());
            Assert.Contains("summaryGroupBy=correlationId", result.StandardError, StringComparison.Ordinal);
        }
        finally
        {
            if (Directory.Exists(rootDir))
                Directory.Delete(rootDir, recursive: true);
        }
    }

    [Fact]
    public async Task HlogInspect_WritesSummaryGroupsByCorrelationIdInTable_WhenRequested()
    {
        var rootDir = Path.Combine(Path.GetTempPath(), $"hlog_inspect_summary_group_correlation_table_{Guid.NewGuid():N}");
        var pluginId = "inspect-summary-group-correlation-table";
        var timestamp = DateTimeOffset.UtcNow;
        var summaryFile = Path.Combine(rootDir, "exports", "records.summary.txt");
        var sharedCorrelationId = "aabbccddeeff00112233445566778899";
        var otherCorrelationId = "99887766554433221100ffeeddccbbaa";

        try
        {
            await SeedDirectLogAsync(rootDir, pluginId, new LogMessage { Level = LogPipeLevel.Info, Message = "correlation first", Source = "OrdersService", Service = "api", CorrelationId = sharedCorrelationId, Timestamp = timestamp });
            await SeedDirectLogAsync(rootDir, pluginId, new LogMessage { Level = LogPipeLevel.Warn, Message = "correlation second", Source = "BillingService", Service = "worker", CorrelationId = sharedCorrelationId, Timestamp = timestamp.AddMilliseconds(1) });
            await SeedDirectLogAsync(rootDir, pluginId, new LogMessage { Level = LogPipeLevel.Error, Message = "correlation third", Source = "PaymentsService", Service = "worker", CorrelationId = otherCorrelationId, Timestamp = timestamp.AddMilliseconds(2) });

            var result = await RunHlogInspectAsync(rootDir, pluginId, "json", summaryOutputPath: summaryFile, summaryFormat: "table", summaryGroupBy: "correlationId", sort: "asc");

            Assert.True(result.ExitCode == 0, $"HlogInspect failed with exit code {result.ExitCode}.{Environment.NewLine}STDERR:{Environment.NewLine}{result.StandardError}{Environment.NewLine}STDOUT:{Environment.NewLine}{result.StandardOutput}");
            var summaryContent = await File.ReadAllTextAsync(summaryFile);
            Assert.Contains("groupBy=correlationId count=2", summaryContent, StringComparison.Ordinal);
            Assert.Contains($"group key={sharedCorrelationId} count=2", summaryContent, StringComparison.Ordinal);
            Assert.Contains($"group key={otherCorrelationId} count=1", summaryContent, StringComparison.Ordinal);
        }
        finally
        {
            if (Directory.Exists(rootDir))
                Directory.Delete(rootDir, recursive: true);
        }
    }

    [Fact]
    public async Task HlogInspect_AcceptsDashedSummaryGroupByAliases_WhenRequested()
    {
        var rootDir = Path.Combine(Path.GetTempPath(), $"hlog_inspect_summary_group_aliases_{Guid.NewGuid():N}");
        var pluginId = "inspect-summary-group-aliases";
        var timestamp = DateTimeOffset.UtcNow;
        var summaryFile = Path.Combine(rootDir, "exports", "records.summary.json");
        var sharedTraceId = "0123456789abcdef0123456789abcdef";
        var sharedCorrelationId = "aabbccddeeff00112233445566778899";

        try
        {
            await SeedDirectLogAsync(rootDir, pluginId, new LogMessage { Level = LogPipeLevel.Info, Message = "alias first", Source = "OrdersService", Service = "api", TraceId = sharedTraceId, CorrelationId = sharedCorrelationId, Timestamp = timestamp });
            await SeedDirectLogAsync(rootDir, pluginId, new LogMessage { Level = LogPipeLevel.Warn, Message = "alias second", Source = "BillingService", Service = "worker", TraceId = sharedTraceId, CorrelationId = sharedCorrelationId, Timestamp = timestamp.AddMilliseconds(1) });

            var traceResult = await RunHlogInspectAsync(rootDir, pluginId, "json", summaryOutputPath: summaryFile, summaryGroupBy: "trace-id", sort: "asc");

            Assert.True(traceResult.ExitCode == 0, $"HlogInspect failed with exit code {traceResult.ExitCode}.{Environment.NewLine}STDERR:{Environment.NewLine}{traceResult.StandardError}{Environment.NewLine}STDOUT:{Environment.NewLine}{traceResult.StandardOutput}");
            using (var summaryDocument = JsonDocument.Parse(await File.ReadAllTextAsync(summaryFile)))
            {
                Assert.Equal("traceId", summaryDocument.RootElement.GetProperty("GroupBy").GetString());
                Assert.Equal(1, summaryDocument.RootElement.GetProperty("Groups").GetArrayLength());
            }

            var correlationResult = await RunHlogInspectAsync(rootDir, pluginId, "json", summaryOutputPath: summaryFile, summaryGroupBy: "correlation-id", sort: "asc");

            Assert.True(correlationResult.ExitCode == 0, $"HlogInspect failed with exit code {correlationResult.ExitCode}.{Environment.NewLine}STDERR:{Environment.NewLine}{correlationResult.StandardError}{Environment.NewLine}STDOUT:{Environment.NewLine}{correlationResult.StandardOutput}");
            using var correlationSummaryDocument = JsonDocument.Parse(await File.ReadAllTextAsync(summaryFile));
            Assert.Equal("correlationId", correlationSummaryDocument.RootElement.GetProperty("GroupBy").GetString());
            Assert.Equal(1, correlationSummaryDocument.RootElement.GetProperty("Groups").GetArrayLength());
        }
        finally
        {
            if (Directory.Exists(rootDir))
                Directory.Delete(rootDir, recursive: true);
        }
    }

    [Fact]
    public async Task HlogInspect_ReturnsError_WhenSummaryGroupOrderIsUsedWithoutSummaryGroupBy()
    {
        var rootDir = Path.Combine(Path.GetTempPath(), $"hlog_inspect_summary_group_order_requires_group_{Guid.NewGuid():N}");
        var pluginId = "inspect-summary-group-order-requires-group";
        var summaryFile = Path.Combine(rootDir, "exports", "records.summary.json");

        try
        {
            var result = await RunHlogInspectAsync(rootDir, pluginId, "json", summaryOutputPath: summaryFile, summaryGroupOrder: "key");

            Assert.Equal(1, result.ExitCode);
            Assert.Contains("'--summary-group-order' requires '--summary-group-by'.", result.StandardError, StringComparison.Ordinal);
        }
        finally
        {
            if (Directory.Exists(rootDir))
                Directory.Delete(rootDir, recursive: true);
        }
    }

    [Fact]
    public async Task HlogInspect_ReturnsHelpHint_WhenSummaryGroupByIsInvalid()
    {
        var rootDir = Path.Combine(Path.GetTempPath(), $"hlog_inspect_summary_group_invalid_{Guid.NewGuid():N}");
        var pluginId = "inspect-summary-group-invalid";
        var summaryFile = Path.Combine(rootDir, "exports", "records.summary.json");

        try
        {
            var result = await RunHlogInspectAsync(rootDir, pluginId, "json", summaryOutputPath: summaryFile, summaryGroupBy: "tenant");

            Assert.Equal(1, result.ExitCode);
            Assert.Contains("Invalid summary group field for --summary-group-by: 'tenant'. Use Level, Source, Service, TraceId or CorrelationId (aliases: trace-id, correlation-id). Use '--help' to see supported options.", result.StandardError, StringComparison.Ordinal);
        }
        finally
        {
            if (Directory.Exists(rootDir))
                Directory.Delete(rootDir, recursive: true);
        }
    }

    [Fact]
    public async Task HlogInspect_ExpandsForensicsFieldPreset_WhenFieldsPresetIsRequested()
    {
        var rootDir = Path.Combine(Path.GetTempPath(), $"hlog_inspect_forensics_{Guid.NewGuid():N}");
        var pluginId = "inspect-forensics";
        var timestamp = DateTimeOffset.UtcNow;
        const string traceId = "fedcba9876543210fedcba9876543210";
        const string correlationId = "00112233445566778899aabbccddeeff";

        try
        {
            await SeedDirectLogAsync(
                rootDir,
                pluginId,
                new LogMessage
                {
                    Level = LogPipeLevel.Error,
                    Message = "forensics payload",
                    Source = "PaymentsService",
                    Service = "worker",
                    TraceId = traceId,
                    CorrelationId = correlationId,
                    Timestamp = timestamp,
                });

            var result = await RunHlogInspectAsync(rootDir, pluginId, "json", fields: "forensics");

            Assert.True(result.ExitCode == 0, $"HlogInspect failed with exit code {result.ExitCode}.{Environment.NewLine}STDERR:{Environment.NewLine}{result.StandardError}{Environment.NewLine}STDOUT:{Environment.NewLine}{result.StandardOutput}");
            Assert.Contains("\"level\"", result.StandardOutput, StringComparison.Ordinal);
            Assert.Contains("\"traceId\"", result.StandardOutput, StringComparison.Ordinal);
            Assert.Contains("\"correlationId\"", result.StandardOutput, StringComparison.Ordinal);
            Assert.Contains("\"payloadJson\"", result.StandardOutput, StringComparison.Ordinal);
            Assert.DoesNotContain("\"message\"", result.StandardOutput, StringComparison.Ordinal);
            Assert.Contains("fields=timestamp,level,source,service,traceId,correlationId,payloadJson", result.StandardError, StringComparison.Ordinal);
        }
        finally
        {
            if (Directory.Exists(rootDir))
                Directory.Delete(rootDir, recursive: true);
        }
    }

    [Fact]
    public async Task HlogInspect_ExpandsTriageFieldPreset_WhenFieldsPresetIsRequested()
    {
        var rootDir = Path.Combine(Path.GetTempPath(), $"hlog_inspect_triage_{Guid.NewGuid():N}");
        var pluginId = "inspect-triage";
        var timestamp = DateTimeOffset.UtcNow;
        const string traceId = "1234567890abcdef1234567890abcdef";

        try
        {
            await SeedDirectLogAsync(
                rootDir,
                pluginId,
                new LogMessage
                {
                    Level = LogPipeLevel.Error,
                    Message = "triage payload",
                    Source = "PaymentsService",
                    Service = "worker",
                    TraceId = traceId,
                    Timestamp = timestamp,
                });

            var result = await RunHlogInspectAsync(rootDir, pluginId, "json", fields: "triage");

            Assert.True(result.ExitCode == 0, $"HlogInspect failed with exit code {result.ExitCode}.{Environment.NewLine}STDERR:{Environment.NewLine}{result.StandardError}{Environment.NewLine}STDOUT:{Environment.NewLine}{result.StandardOutput}");
            Assert.Contains("\"level\"", result.StandardOutput, StringComparison.Ordinal);
            Assert.Contains("\"message\"", result.StandardOutput, StringComparison.Ordinal);
            Assert.Contains("\"traceId\"", result.StandardOutput, StringComparison.Ordinal);
            Assert.DoesNotContain("\"payloadJson\"", result.StandardOutput, StringComparison.Ordinal);
            Assert.DoesNotContain("\"service\"", result.StandardOutput, StringComparison.Ordinal);
            Assert.Contains("fields=timestamp,level,source,message,traceId", result.StandardError, StringComparison.Ordinal);
        }
        finally
        {
            if (Directory.Exists(rootDir))
                Directory.Delete(rootDir, recursive: true);
        }
    }

    [Fact]
    public async Task HlogInspect_FiltersBySourceAndService_WhenSampleIsInvoked()
    {
        var rootDir = Path.Combine(Path.GetTempPath(), $"hlog_inspect_filter_{Guid.NewGuid():N}");
        var pluginId = "inspect-filter";
        var timestamp = DateTimeOffset.UtcNow;

        try
        {
            await SeedDirectLogAsync(
                rootDir,
                pluginId,
                new LogMessage
                {
                    Level = LogPipeLevel.Info,
                    Message = "orders payload",
                    Source = "OrdersService",
                    Service = "api-gateway",
                    Timestamp = timestamp,
                });

            await SeedDirectLogAsync(
                rootDir,
                pluginId,
                new LogMessage
                {
                    Level = LogPipeLevel.Info,
                    Message = "billing payload",
                    Source = "BillingService",
                    Service = "worker",
                    Timestamp = timestamp.AddMilliseconds(1),
                });

            var result = await RunHlogInspectAsync(rootDir, pluginId, "json", "OrdersService", "api-gateway");

            Assert.True(result.ExitCode == 0, $"HlogInspect failed with exit code {result.ExitCode}.{Environment.NewLine}STDERR:{Environment.NewLine}{result.StandardError}{Environment.NewLine}STDOUT:{Environment.NewLine}{result.StandardOutput}");
            Assert.Contains("orders payload", result.StandardOutput, StringComparison.Ordinal);
            Assert.Contains("\"Source\": \"OrdersService\"", result.StandardOutput, StringComparison.Ordinal);
            Assert.Contains("\"Service\": \"api-gateway\"", result.StandardOutput, StringComparison.Ordinal);
            Assert.DoesNotContain("billing payload", result.StandardOutput, StringComparison.Ordinal);
            Assert.Contains("source=OrdersService", result.StandardError, StringComparison.Ordinal);
            Assert.Contains("service=api-gateway", result.StandardError, StringComparison.Ordinal);
        }
        finally
        {
            if (Directory.Exists(rootDir))
                Directory.Delete(rootDir, recursive: true);
        }
    }

    [Fact]
    public async Task HlogInspect_FiltersByMultipleSourcesAndServices_WhenCommaSeparatedFiltersAreProvided()
    {
        var rootDir = Path.Combine(Path.GetTempPath(), $"hlog_inspect_multi_filter_{Guid.NewGuid():N}");
        var pluginId = "inspect-multi-filter";
        var timestamp = DateTimeOffset.UtcNow;

        try
        {
            await SeedDirectLogAsync(
                rootDir,
                pluginId,
                new LogMessage
                {
                    Level = LogPipeLevel.Info,
                    Message = "orders payload",
                    Source = "OrdersService",
                    Service = "api-gateway",
                    Timestamp = timestamp,
                });

            await SeedDirectLogAsync(
                rootDir,
                pluginId,
                new LogMessage
                {
                    Level = LogPipeLevel.Warn,
                    Message = "billing payload",
                    Source = "BillingService",
                    Service = "worker",
                    Timestamp = timestamp.AddMilliseconds(1),
                });

            await SeedDirectLogAsync(
                rootDir,
                pluginId,
                new LogMessage
                {
                    Level = LogPipeLevel.Error,
                    Message = "inventory payload",
                    Source = "InventoryService",
                    Service = "batch",
                    Timestamp = timestamp.AddMilliseconds(2),
                });

            var result = await RunHlogInspectAsync(rootDir, pluginId, "json", source: "OrdersService,BillingService", service: "api-gateway,worker");

            Assert.True(result.ExitCode == 0, $"HlogInspect failed with exit code {result.ExitCode}.{Environment.NewLine}STDERR:{Environment.NewLine}{result.StandardError}{Environment.NewLine}STDOUT:{Environment.NewLine}{result.StandardOutput}");
            Assert.Contains("orders payload", result.StandardOutput, StringComparison.Ordinal);
            Assert.Contains("billing payload", result.StandardOutput, StringComparison.Ordinal);
            Assert.DoesNotContain("inventory payload", result.StandardOutput, StringComparison.Ordinal);
            Assert.Contains("source=OrdersService,BillingService", result.StandardError, StringComparison.Ordinal);
            Assert.Contains("service=api-gateway,worker", result.StandardError, StringComparison.Ordinal);
        }
        finally
        {
            if (Directory.Exists(rootDir))
                Directory.Delete(rootDir, recursive: true);
        }
    }

    [Fact]
    public async Task HlogInspect_FiltersByExactLevels_WhenLevelInIsProvided()
    {
        var rootDir = Path.Combine(Path.GetTempPath(), $"hlog_inspect_level_in_{Guid.NewGuid():N}");
        var pluginId = "inspect-level-in";
        var timestamp = DateTimeOffset.UtcNow;

        try
        {
            await SeedDirectLogAsync(
                rootDir,
                pluginId,
                new LogMessage
                {
                    Level = LogPipeLevel.Info,
                    Message = "info payload",
                    Source = "OrdersService",
                    Service = "api-gateway",
                    Timestamp = timestamp,
                });

            await SeedDirectLogAsync(
                rootDir,
                pluginId,
                new LogMessage
                {
                    Level = LogPipeLevel.Warn,
                    Message = "warn payload",
                    Source = "OrdersService",
                    Service = "api-gateway",
                    Timestamp = timestamp.AddMilliseconds(1),
                });

            await SeedDirectLogAsync(
                rootDir,
                pluginId,
                new LogMessage
                {
                    Level = LogPipeLevel.Error,
                    Message = "error payload",
                    Source = "OrdersService",
                    Service = "api-gateway",
                    Timestamp = timestamp.AddMilliseconds(2),
                });

            var result = await RunHlogInspectAsync(rootDir, pluginId, "json", levelIn: "Info,Error");

            Assert.True(result.ExitCode == 0, $"HlogInspect failed with exit code {result.ExitCode}.{Environment.NewLine}STDERR:{Environment.NewLine}{result.StandardError}{Environment.NewLine}STDOUT:{Environment.NewLine}{result.StandardOutput}");
            Assert.Contains("info payload", result.StandardOutput, StringComparison.Ordinal);
            Assert.Contains("error payload", result.StandardOutput, StringComparison.Ordinal);
            Assert.DoesNotContain("warn payload", result.StandardOutput, StringComparison.Ordinal);
            Assert.Contains("levelIn=Info,Error", result.StandardError, StringComparison.Ordinal);
        }
        finally
        {
            if (Directory.Exists(rootDir))
                Directory.Delete(rootDir, recursive: true);
        }
    }

    [Fact]
    public async Task HlogInspect_ReturnsHelpHint_WhenSortIsInvalid()
    {
        var rootDir = Path.Combine(Path.GetTempPath(), $"hlog_inspect_sort_invalid_{Guid.NewGuid():N}");
        var pluginId = "inspect-sort-invalid";

        try
        {
            var result = await RunHlogInspectAsync(rootDir, pluginId, "json", sort: "sideways");

            Assert.Equal(1, result.ExitCode);
            Assert.Contains("Invalid sort for --sort: 'sideways'. Use Asc or Desc. Use '--help' to see supported options.", result.StandardError, StringComparison.Ordinal);
        }
        finally
        {
            if (Directory.Exists(rootDir))
                Directory.Delete(rootDir, recursive: true);
        }
    }

    [Fact]
    public async Task HlogInspect_ReturnsError_WhenLevelInContainsInvalidLevel()
    {
        var rootDir = Path.Combine(Path.GetTempPath(), $"hlog_inspect_level_in_invalid_{Guid.NewGuid():N}");
        var pluginId = "inspect-level-in-invalid";

        try
        {
            var result = await RunHlogInspectAsync(rootDir, pluginId, "json", levelIn: "Info,Bogus");

            Assert.Equal(1, result.ExitCode);
            Assert.Contains("Invalid level for --level-in: 'Bogus'. Use Trace, Debug, Info, Warn, Error or Fatal. Use '--help' to see supported options.", result.StandardError, StringComparison.Ordinal);
        }
        finally
        {
            if (Directory.Exists(rootDir))
                Directory.Delete(rootDir, recursive: true);
        }
    }

    [Fact]
    public async Task HlogInspect_ReturnsError_WhenTraceIdContainsInvalidToken()
    {
        var rootDir = Path.Combine(Path.GetTempPath(), $"hlog_inspect_trace_invalid_{Guid.NewGuid():N}");
        var pluginId = "inspect-trace-invalid";

        try
        {
            var result = await RunHlogInspectAsync(rootDir, pluginId, "json", traceId: "bogus");

            Assert.Equal(1, result.ExitCode);
            Assert.Contains("Invalid identifier for --traceId: 'bogus'. Use 32 hexadecimal characters. Use '--help' to see supported options.", result.StandardError, StringComparison.Ordinal);
        }
        finally
        {
            if (Directory.Exists(rootDir))
                Directory.Delete(rootDir, recursive: true);
        }
    }

    [Fact]
    public async Task HlogInspect_ReturnsError_WhenCorrelationIdContainsInvalidToken()
    {
        var rootDir = Path.Combine(Path.GetTempPath(), $"hlog_inspect_correlation_invalid_{Guid.NewGuid():N}");
        var pluginId = "inspect-correlation-invalid";

        try
        {
            var result = await RunHlogInspectAsync(rootDir, pluginId, "json", correlationId: "bogus");

            Assert.Equal(1, result.ExitCode);
            Assert.Contains("Invalid identifier for --correlationId: 'bogus'. Use 32 hexadecimal characters or a GUID. Use '--help' to see supported options.", result.StandardError, StringComparison.Ordinal);
        }
        finally
        {
            if (Directory.Exists(rootDir))
                Directory.Delete(rootDir, recursive: true);
        }
    }

    [Fact]
    public async Task HlogInspect_FiltersByTraceId_WhenSampleIsInvoked()
    {
        var rootDir = Path.Combine(Path.GetTempPath(), $"hlog_inspect_trace_{Guid.NewGuid():N}");
        var pluginId = "inspect-trace";
        var matchingTraceId = "0123456789abcdef0123456789abcdef";
        var otherTraceId = "fedcba9876543210fedcba9876543210";
        var timestamp = DateTimeOffset.UtcNow;

        try
        {
            await SeedDirectLogAsync(
                rootDir,
                pluginId,
                new LogMessage
                {
                    Level = LogPipeLevel.Warn,
                    Message = "matching trace payload",
                    Source = "TraceSource",
                    Service = "trace-service",
                    TraceId = matchingTraceId,
                    Timestamp = timestamp,
                });

            await SeedDirectLogAsync(
                rootDir,
                pluginId,
                new LogMessage
                {
                    Level = LogPipeLevel.Warn,
                    Message = "other trace payload",
                    Source = "TraceSource",
                    Service = "trace-service",
                    TraceId = otherTraceId,
                    Timestamp = timestamp.AddMilliseconds(1),
                });

            var result = await RunHlogInspectAsync(rootDir, pluginId, "json", traceId: matchingTraceId);

            Assert.True(result.ExitCode == 0, $"HlogInspect failed with exit code {result.ExitCode}.{Environment.NewLine}STDERR:{Environment.NewLine}{result.StandardError}{Environment.NewLine}STDOUT:{Environment.NewLine}{result.StandardOutput}");
            Assert.Contains("matching trace payload", result.StandardOutput, StringComparison.Ordinal);
            Assert.Contains($"\"TraceId\": \"{matchingTraceId}\"", result.StandardOutput, StringComparison.Ordinal);
            Assert.DoesNotContain("other trace payload", result.StandardOutput, StringComparison.Ordinal);
            Assert.Contains($"traceId={matchingTraceId}", result.StandardError, StringComparison.Ordinal);
        }
        finally
        {
            if (Directory.Exists(rootDir))
                Directory.Delete(rootDir, recursive: true);
        }
    }

    [Fact]
    public async Task HlogInspect_FiltersByMultipleTraceIds_WhenCommaSeparatedFiltersAreProvided()
    {
        var rootDir = Path.Combine(Path.GetTempPath(), $"hlog_inspect_trace_multi_{Guid.NewGuid():N}");
        var pluginId = "inspect-trace-multi";
        var firstTraceId = "0123456789abcdef0123456789abcdef";
        var secondTraceId = "fedcba9876543210fedcba9876543210";
        var otherTraceId = "00112233445566778899aabbccddeeff";
        var timestamp = DateTimeOffset.UtcNow;

        try
        {
            await SeedDirectLogAsync(
                rootDir,
                pluginId,
                new LogMessage
                {
                    Level = LogPipeLevel.Warn,
                    Message = "first trace payload",
                    Source = "TraceSource",
                    Service = "trace-service",
                    TraceId = firstTraceId,
                    Timestamp = timestamp,
                });

            await SeedDirectLogAsync(
                rootDir,
                pluginId,
                new LogMessage
                {
                    Level = LogPipeLevel.Warn,
                    Message = "second trace payload",
                    Source = "TraceSource",
                    Service = "trace-service",
                    TraceId = secondTraceId,
                    Timestamp = timestamp.AddMilliseconds(1),
                });

            await SeedDirectLogAsync(
                rootDir,
                pluginId,
                new LogMessage
                {
                    Level = LogPipeLevel.Warn,
                    Message = "other trace payload",
                    Source = "TraceSource",
                    Service = "trace-service",
                    TraceId = otherTraceId,
                    Timestamp = timestamp.AddMilliseconds(2),
                });

            var result = await RunHlogInspectAsync(rootDir, pluginId, "json", traceId: $"{firstTraceId},{secondTraceId}");

            Assert.True(result.ExitCode == 0, $"HlogInspect failed with exit code {result.ExitCode}.{Environment.NewLine}STDERR:{Environment.NewLine}{result.StandardError}{Environment.NewLine}STDOUT:{Environment.NewLine}{result.StandardOutput}");
            Assert.Contains("first trace payload", result.StandardOutput, StringComparison.Ordinal);
            Assert.Contains("second trace payload", result.StandardOutput, StringComparison.Ordinal);
            Assert.DoesNotContain("other trace payload", result.StandardOutput, StringComparison.Ordinal);
            Assert.Contains($"traceId={firstTraceId},{secondTraceId}", result.StandardError, StringComparison.Ordinal);
        }
        finally
        {
            if (Directory.Exists(rootDir))
                Directory.Delete(rootDir, recursive: true);
        }
    }

    [Fact]
    public async Task HlogInspect_FiltersByCorrelationId_WhenSampleIsInvoked()
    {
        var rootDir = Path.Combine(Path.GetTempPath(), $"hlog_inspect_corr_{Guid.NewGuid():N}");
        var pluginId = "inspect-correlation";
        var matchingCorrelationId = "aabbccddeeff00112233445566778899";
        var otherCorrelationId = "99887766554433221100ffeeddccbbaa";
        var timestamp = DateTimeOffset.UtcNow;

        try
        {
            await SeedDirectLogAsync(
                rootDir,
                pluginId,
                new LogMessage
                {
                    Level = LogPipeLevel.Info,
                    Message = "matching correlation payload",
                    Source = "CorrelationSource",
                    Service = "correlation-service",
                    CorrelationId = matchingCorrelationId,
                    Timestamp = timestamp,
                });

            await SeedDirectLogAsync(
                rootDir,
                pluginId,
                new LogMessage
                {
                    Level = LogPipeLevel.Info,
                    Message = "other correlation payload",
                    Source = "CorrelationSource",
                    Service = "correlation-service",
                    CorrelationId = otherCorrelationId,
                    Timestamp = timestamp.AddMilliseconds(1),
                });

            var result = await RunHlogInspectAsync(rootDir, pluginId, "json", correlationId: matchingCorrelationId);

            Assert.True(result.ExitCode == 0, $"HlogInspect failed with exit code {result.ExitCode}.{Environment.NewLine}STDERR:{Environment.NewLine}{result.StandardError}{Environment.NewLine}STDOUT:{Environment.NewLine}{result.StandardOutput}");
            Assert.Contains("matching correlation payload", result.StandardOutput, StringComparison.Ordinal);
            Assert.Contains($"\"CorrelationId\": \"{matchingCorrelationId}\"", result.StandardOutput, StringComparison.Ordinal);
            Assert.DoesNotContain("other correlation payload", result.StandardOutput, StringComparison.Ordinal);
            Assert.Contains($"correlationId={matchingCorrelationId}", result.StandardError, StringComparison.Ordinal);
        }
        finally
        {
            if (Directory.Exists(rootDir))
                Directory.Delete(rootDir, recursive: true);
        }
    }

    [Fact]
    public async Task HlogInspect_FiltersByMultipleCorrelationIds_WhenCommaSeparatedFiltersAreProvided()
    {
        var rootDir = Path.Combine(Path.GetTempPath(), $"hlog_inspect_corr_multi_{Guid.NewGuid():N}");
        var pluginId = "inspect-correlation-multi";
        var firstCorrelationId = "aabbccddeeff00112233445566778899";
        var secondCorrelationId = "99887766554433221100ffeeddccbbaa";
        var otherCorrelationId = "11223344556677889900aabbccddeeff";
        var timestamp = DateTimeOffset.UtcNow;

        try
        {
            await SeedDirectLogAsync(
                rootDir,
                pluginId,
                new LogMessage
                {
                    Level = LogPipeLevel.Info,
                    Message = "first correlation payload",
                    Source = "CorrelationSource",
                    Service = "correlation-service",
                    CorrelationId = firstCorrelationId,
                    Timestamp = timestamp,
                });

            await SeedDirectLogAsync(
                rootDir,
                pluginId,
                new LogMessage
                {
                    Level = LogPipeLevel.Info,
                    Message = "second correlation payload",
                    Source = "CorrelationSource",
                    Service = "correlation-service",
                    CorrelationId = secondCorrelationId,
                    Timestamp = timestamp.AddMilliseconds(1),
                });

            await SeedDirectLogAsync(
                rootDir,
                pluginId,
                new LogMessage
                {
                    Level = LogPipeLevel.Info,
                    Message = "other correlation payload",
                    Source = "CorrelationSource",
                    Service = "correlation-service",
                    CorrelationId = otherCorrelationId,
                    Timestamp = timestamp.AddMilliseconds(2),
                });

            var result = await RunHlogInspectAsync(rootDir, pluginId, "json", correlationId: $"{firstCorrelationId},{secondCorrelationId}");

            Assert.True(result.ExitCode == 0, $"HlogInspect failed with exit code {result.ExitCode}.{Environment.NewLine}STDERR:{Environment.NewLine}{result.StandardError}{Environment.NewLine}STDOUT:{Environment.NewLine}{result.StandardOutput}");
            Assert.Contains("first correlation payload", result.StandardOutput, StringComparison.Ordinal);
            Assert.Contains("second correlation payload", result.StandardOutput, StringComparison.Ordinal);
            Assert.DoesNotContain("other correlation payload", result.StandardOutput, StringComparison.Ordinal);
            Assert.Contains($"correlationId={firstCorrelationId},{secondCorrelationId}", result.StandardError, StringComparison.Ordinal);
        }
        finally
        {
            if (Directory.Exists(rootDir))
                Directory.Delete(rootDir, recursive: true);
        }
    }

    [Fact]
    public async Task HlogInspect_FiltersByCorrelationId_WhenGuidFormatIsProvided()
    {
        var rootDir = Path.Combine(Path.GetTempPath(), $"hlog_inspect_corr_guid_{Guid.NewGuid():N}");
        var pluginId = "inspect-correlation-guid";
        var matchingCorrelationId = "aabbccddeeff00112233445566778899";
        var otherCorrelationId = "99887766554433221100ffeeddccbbaa";
        var guidCorrelationId = "aabbccdd-eeff-0011-2233-445566778899";
        var timestamp = DateTimeOffset.UtcNow;

        try
        {
            await SeedDirectLogAsync(
                rootDir,
                pluginId,
                new LogMessage
                {
                    Level = LogPipeLevel.Info,
                    Message = "matching guid correlation payload",
                    Source = "CorrelationSource",
                    Service = "correlation-service",
                    CorrelationId = matchingCorrelationId,
                    Timestamp = timestamp,
                });

            await SeedDirectLogAsync(
                rootDir,
                pluginId,
                new LogMessage
                {
                    Level = LogPipeLevel.Info,
                    Message = "other guid correlation payload",
                    Source = "CorrelationSource",
                    Service = "correlation-service",
                    CorrelationId = otherCorrelationId,
                    Timestamp = timestamp.AddMilliseconds(1),
                });

            var result = await RunHlogInspectAsync(rootDir, pluginId, "json", correlationId: guidCorrelationId);

            Assert.True(result.ExitCode == 0, $"HlogInspect failed with exit code {result.ExitCode}.{Environment.NewLine}STDERR:{Environment.NewLine}{result.StandardError}{Environment.NewLine}STDOUT:{Environment.NewLine}{result.StandardOutput}");
            Assert.Contains("matching guid correlation payload", result.StandardOutput, StringComparison.Ordinal);
            Assert.DoesNotContain("other guid correlation payload", result.StandardOutput, StringComparison.Ordinal);
            Assert.Contains($"correlationId={matchingCorrelationId}", result.StandardError, StringComparison.Ordinal);
        }
        finally
        {
            if (Directory.Exists(rootDir))
                Directory.Delete(rootDir, recursive: true);
        }
    }

    [Fact]
    public async Task HlogInspect_FiltersByIdentifierAliases_WhenDashedOptionsAreProvided()
    {
        var rootDir = Path.Combine(Path.GetTempPath(), $"hlog_inspect_identifier_alias_{Guid.NewGuid():N}");
        var pluginId = "inspect-identifier-alias";
        var matchingTraceId = "0123456789abcdef0123456789abcdef";
        var matchingCorrelationId = "aabbccddeeff00112233445566778899";
        var otherTraceId = "fedcba9876543210fedcba9876543210";
        var otherCorrelationId = "99887766554433221100ffeeddccbbaa";
        var timestamp = DateTimeOffset.UtcNow;

        try
        {
            await SeedDirectLogAsync(
                rootDir,
                pluginId,
                new LogMessage
                {
                    Level = LogPipeLevel.Info,
                    Message = "matching alias payload",
                    Source = "AliasSource",
                    Service = "alias-service",
                    TraceId = matchingTraceId,
                    CorrelationId = matchingCorrelationId,
                    Timestamp = timestamp,
                });

            await SeedDirectLogAsync(
                rootDir,
                pluginId,
                new LogMessage
                {
                    Level = LogPipeLevel.Info,
                    Message = "other alias payload",
                    Source = "AliasSource",
                    Service = "alias-service",
                    TraceId = otherTraceId,
                    CorrelationId = otherCorrelationId,
                    Timestamp = timestamp.AddMilliseconds(1),
                });

            var result = await RunHlogInspectAsync(rootDir, pluginId, "json", traceId: matchingTraceId, correlationId: matchingCorrelationId, useDashedIdentifierAliases: true);

            Assert.True(result.ExitCode == 0, $"HlogInspect failed with exit code {result.ExitCode}.{Environment.NewLine}STDERR:{Environment.NewLine}{result.StandardError}{Environment.NewLine}STDOUT:{Environment.NewLine}{result.StandardOutput}");
            Assert.Contains("matching alias payload", result.StandardOutput, StringComparison.Ordinal);
            Assert.DoesNotContain("other alias payload", result.StandardOutput, StringComparison.Ordinal);
            Assert.Contains($"traceId={matchingTraceId}", result.StandardError, StringComparison.Ordinal);
            Assert.Contains($"correlationId={matchingCorrelationId}", result.StandardError, StringComparison.Ordinal);
        }
        finally
        {
            if (Directory.Exists(rootDir))
                Directory.Delete(rootDir, recursive: true);
        }
    }

    [Fact]
    public async Task HlogInspect_FiltersByContains_WhenSampleIsInvoked()
    {
        var rootDir = Path.Combine(Path.GetTempPath(), $"hlog_inspect_contains_{Guid.NewGuid():N}");
        var pluginId = "inspect-contains";
        var timestamp = DateTimeOffset.UtcNow;

        try
        {
            await SeedDirectLogAsync(
                rootDir,
                pluginId,
                new LogMessage
                {
                    Level = LogPipeLevel.Error,
                    Message = "timeout while calling orders backend",
                    Source = "OrdersService",
                    Service = "api-gateway",
                    Timestamp = timestamp,
                });

            await SeedDirectLogAsync(
                rootDir,
                pluginId,
                new LogMessage
                {
                    Level = LogPipeLevel.Info,
                    Message = "background worker completed successfully",
                    Source = "BillingService",
                    Service = "worker",
                    Timestamp = timestamp.AddMilliseconds(1),
                });

            var result = await RunHlogInspectAsync(rootDir, pluginId, "json", contains: "timeout");

            Assert.True(result.ExitCode == 0, $"HlogInspect failed with exit code {result.ExitCode}.{Environment.NewLine}STDERR:{Environment.NewLine}{result.StandardError}{Environment.NewLine}STDOUT:{Environment.NewLine}{result.StandardOutput}");
            Assert.Contains("timeout while calling orders backend", result.StandardOutput, StringComparison.Ordinal);
            Assert.DoesNotContain("background worker completed successfully", result.StandardOutput, StringComparison.Ordinal);
            Assert.Contains("contains=timeout", result.StandardError, StringComparison.Ordinal);
        }
        finally
        {
            if (Directory.Exists(rootDir))
                Directory.Delete(rootDir, recursive: true);
        }
    }

    [Fact]
    public async Task HlogInspect_FiltersContainsAgainstMessageOnly_WhenContainsFieldIsMessage()
    {
        var rootDir = Path.Combine(Path.GetTempPath(), $"hlog_inspect_contains_message_{Guid.NewGuid():N}");
        var pluginId = "inspect-contains-message";
        var timestamp = DateTimeOffset.UtcNow;

        try
        {
            await SeedDirectLogAsync(
                rootDir,
                pluginId,
                new LogMessage
                {
                    Level = LogPipeLevel.Info,
                    Message = "message contains timeout",
                    Source = "OrdersService",
                    Service = "api-gateway",
                    Timestamp = timestamp,
                });

            await SeedDirectLogAsync(
                rootDir,
                pluginId,
                new LogMessage
                {
                    Level = LogPipeLevel.Info,
                    Message = "plain marker",
                    Source = "payload-timeout-source",
                    Service = "api-gateway",
                    Timestamp = timestamp.AddMilliseconds(1),
                });

            var result = await RunHlogInspectAsync(rootDir, pluginId, "json", contains: "timeout", containsField: "message");

            Assert.True(result.ExitCode == 0, $"HlogInspect failed with exit code {result.ExitCode}.{Environment.NewLine}STDERR:{Environment.NewLine}{result.StandardError}{Environment.NewLine}STDOUT:{Environment.NewLine}{result.StandardOutput}");
            Assert.Contains("message contains timeout", result.StandardOutput, StringComparison.Ordinal);
            Assert.DoesNotContain("payload-timeout-source", result.StandardOutput, StringComparison.Ordinal);
            Assert.Contains("containsField=message", result.StandardError, StringComparison.Ordinal);
        }
        finally
        {
            if (Directory.Exists(rootDir))
                Directory.Delete(rootDir, recursive: true);
        }
    }

    [Fact]
    public async Task HlogInspect_FiltersContainsAgainstPayloadOnly_WhenContainsFieldIsPayload()
    {
        var rootDir = Path.Combine(Path.GetTempPath(), $"hlog_inspect_contains_payload_{Guid.NewGuid():N}");
        var pluginId = "inspect-contains-payload";
        var timestamp = DateTimeOffset.UtcNow;

        try
        {
            await SeedDirectLogAsync(
                rootDir,
                pluginId,
                new LogMessage
                {
                    Level = LogPipeLevel.Info,
                    Message = "plain marker",
                    Source = "OrdersService",
                    Service = "api-gateway",
                    Timestamp = timestamp,
                });

            await SeedDirectLogAsync(
                rootDir,
                pluginId,
                new LogMessage
                {
                    Level = LogPipeLevel.Info,
                    Message = "plain marker",
                    Source = "payload-timeout-source",
                    Service = "api-gateway",
                    Timestamp = timestamp.AddMilliseconds(1),
                });

            var result = await RunHlogInspectAsync(rootDir, pluginId, "json", contains: "timeout", containsField: "payload");

            Assert.True(result.ExitCode == 0, $"HlogInspect failed with exit code {result.ExitCode}.{Environment.NewLine}STDERR:{Environment.NewLine}{result.StandardError}{Environment.NewLine}STDOUT:{Environment.NewLine}{result.StandardOutput}");
            Assert.DoesNotContain("\"Source\": \"OrdersService\"", result.StandardOutput, StringComparison.Ordinal);
            Assert.Contains("payload-timeout-source", result.StandardOutput, StringComparison.Ordinal);
            Assert.Contains("containsField=payload", result.StandardError, StringComparison.Ordinal);
        }
        finally
        {
            if (Directory.Exists(rootDir))
                Directory.Delete(rootDir, recursive: true);
        }
    }

    [Fact]
    public async Task HlogInspect_ReturnsError_WhenContainsFieldIsInvalid()
    {
        var rootDir = Path.Combine(Path.GetTempPath(), $"hlog_inspect_contains_field_invalid_{Guid.NewGuid():N}");
        var pluginId = "inspect-contains-field-invalid";

        try
        {
            var result = await RunHlogInspectAsync(rootDir, pluginId, "json", contains: "timeout", containsField: "metadata");

            Assert.Equal(1, result.ExitCode);
            Assert.Contains("Invalid contains field for --contains-field: 'metadata'. Use Any, Message or Payload. Use '--help' to see supported options.", result.StandardError, StringComparison.Ordinal);
        }
        finally
        {
            if (Directory.Exists(rootDir))
                Directory.Delete(rootDir, recursive: true);
        }
    }

    [Fact]
    public async Task HlogInspect_ReturnsHelpHint_WhenContainsModeIsInvalid()
    {
        var rootDir = Path.Combine(Path.GetTempPath(), $"hlog_inspect_contains_mode_invalid_{Guid.NewGuid():N}");
        var pluginId = "inspect-contains-mode-invalid";

        try
        {
            var result = await RunHlogInspectAsync(rootDir, pluginId, "json", contains: "timeout", containsMode: "first");

            Assert.Equal(1, result.ExitCode);
            Assert.Contains("Invalid contains mode for --contains-mode: 'first'. Use Any or All. Use '--help' to see supported options.", result.StandardError, StringComparison.Ordinal);
        }
        finally
        {
            if (Directory.Exists(rootDir))
                Directory.Delete(rootDir, recursive: true);
        }
    }

    [Fact]
    public async Task HlogInspect_ReturnsError_WhenFieldNameIsInvalid()
    {
        var rootDir = Path.Combine(Path.GetTempPath(), $"hlog_inspect_fields_invalid_{Guid.NewGuid():N}");
        var pluginId = "inspect-fields-invalid";

        try
        {
            var result = await RunHlogInspectAsync(rootDir, pluginId, "json", fields: "payload-body");

            Assert.Equal(1, result.ExitCode);
            Assert.Contains("Invalid field for --fields: 'payload-body'. Use core, tracing, ops, errors, security, audit, forensics, triage, timestamp, level, source, service, message, traceId/trace-id, correlationId/correlation-id or payloadJson/payload-json.", result.StandardError, StringComparison.Ordinal);
        }
        finally
        {
            if (Directory.Exists(rootDir))
                Directory.Delete(rootDir, recursive: true);
        }
    }

    [Fact]
    public async Task HlogInspect_FiltersContainsWithAllMode_WhenAllTokensMustMatch()
    {
        var rootDir = Path.Combine(Path.GetTempPath(), $"hlog_inspect_contains_mode_all_{Guid.NewGuid():N}");
        var pluginId = "inspect-contains-mode-all";
        var timestamp = DateTimeOffset.UtcNow;

        try
        {
            await SeedDirectLogAsync(rootDir, pluginId, new LogMessage { Level = LogPipeLevel.Info, Message = "timeout while calling orders backend", Source = "OrdersService", Service = "api-gateway", Timestamp = timestamp });
            await SeedDirectLogAsync(rootDir, pluginId, new LogMessage { Level = LogPipeLevel.Info, Message = "timeout while calling billing backend", Source = "BillingService", Service = "worker", Timestamp = timestamp.AddMilliseconds(1) });
            await SeedDirectLogAsync(rootDir, pluginId, new LogMessage { Level = LogPipeLevel.Info, Message = "orders synchronization completed", Source = "OrdersService", Service = "api-gateway", Timestamp = timestamp.AddMilliseconds(2) });

            var result = await RunHlogInspectAsync(rootDir, pluginId, "json", contains: "timeout,orders", containsField: "message", containsMode: "all");

            Assert.True(result.ExitCode == 0, $"HlogInspect failed with exit code {result.ExitCode}.{Environment.NewLine}STDERR:{Environment.NewLine}{result.StandardError}{Environment.NewLine}STDOUT:{Environment.NewLine}{result.StandardOutput}");
            Assert.Contains("timeout while calling orders backend", result.StandardOutput, StringComparison.Ordinal);
            Assert.DoesNotContain("timeout while calling billing backend", result.StandardOutput, StringComparison.Ordinal);
            Assert.DoesNotContain("orders synchronization completed", result.StandardOutput, StringComparison.Ordinal);
            Assert.Contains("containsMode=all", result.StandardError, StringComparison.Ordinal);
        }
        finally
        {
            if (Directory.Exists(rootDir))
                Directory.Delete(rootDir, recursive: true);
        }
    }

    [Fact]
    public async Task HlogInspect_ReturnsError_WhenContainsModeIsUsedWithoutContains()
    {
        var rootDir = Path.Combine(Path.GetTempPath(), $"hlog_inspect_contains_mode_requires_contains_{Guid.NewGuid():N}");
        var pluginId = "inspect-contains-mode-requires-contains";

        try
        {
            var result = await RunHlogInspectAsync(rootDir, pluginId, "json", containsMode: "all");

            Assert.Equal(1, result.ExitCode);
            Assert.Contains("'--contains-mode' requires '--contains'.", result.StandardError, StringComparison.Ordinal);
        }
        finally
        {
            if (Directory.Exists(rootDir))
                Directory.Delete(rootDir, recursive: true);
        }
    }

    [Fact]
    public async Task HlogInspect_SortsDescending_WhenSampleIsInvoked()
    {
        var rootDir = Path.Combine(Path.GetTempPath(), $"hlog_inspect_sort_{Guid.NewGuid():N}");
        var pluginId = "inspect-sort";
        var timestamp = DateTimeOffset.UtcNow;

        try
        {
            await SeedDirectLogAsync(
                rootDir,
                pluginId,
                new LogMessage
                {
                    Level = LogPipeLevel.Info,
                    Message = "older payload",
                    Source = "OrdersService",
                    Service = "api-gateway",
                    Timestamp = timestamp,
                });

            await SeedDirectLogAsync(
                rootDir,
                pluginId,
                new LogMessage
                {
                    Level = LogPipeLevel.Info,
                    Message = "newer payload",
                    Source = "OrdersService",
                    Service = "api-gateway",
                    Timestamp = timestamp.AddMilliseconds(1),
                });

            var result = await RunHlogInspectAsync(rootDir, pluginId, "json", sort: "desc");

            Assert.True(result.ExitCode == 0, $"HlogInspect failed with exit code {result.ExitCode}.{Environment.NewLine}STDERR:{Environment.NewLine}{result.StandardError}{Environment.NewLine}STDOUT:{Environment.NewLine}{result.StandardOutput}");
            var newerIndex = result.StandardOutput.IndexOf("newer payload", StringComparison.Ordinal);
            var olderIndex = result.StandardOutput.IndexOf("older payload", StringComparison.Ordinal);
            Assert.True(newerIndex >= 0 && olderIndex >= 0 && newerIndex < olderIndex, $"Expected descending order in output.{Environment.NewLine}{result.StandardOutput}");
            Assert.Contains("sort=desc", result.StandardError, StringComparison.Ordinal);
        }
        finally
        {
            if (Directory.Exists(rootDir))
                Directory.Delete(rootDir, recursive: true);
        }
    }

    [Fact]
    public async Task HlogInspect_AppliesOffsetAfterSorting_WhenSampleIsInvoked()
    {
        var rootDir = Path.Combine(Path.GetTempPath(), $"hlog_inspect_offset_{Guid.NewGuid():N}");
        var pluginId = "inspect-offset";
        var timestamp = DateTimeOffset.UtcNow;

        try
        {
            await SeedDirectLogAsync(
                rootDir,
                pluginId,
                new LogMessage
                {
                    Level = LogPipeLevel.Info,
                    Message = "first payload",
                    Source = "OrdersService",
                    Service = "api-gateway",
                    Timestamp = timestamp,
                });

            await SeedDirectLogAsync(
                rootDir,
                pluginId,
                new LogMessage
                {
                    Level = LogPipeLevel.Info,
                    Message = "second payload",
                    Source = "OrdersService",
                    Service = "api-gateway",
                    Timestamp = timestamp.AddMilliseconds(1),
                });

            await SeedDirectLogAsync(
                rootDir,
                pluginId,
                new LogMessage
                {
                    Level = LogPipeLevel.Info,
                    Message = "third payload",
                    Source = "OrdersService",
                    Service = "api-gateway",
                    Timestamp = timestamp.AddMilliseconds(2),
                });

            var result = await RunHlogInspectAsync(rootDir, pluginId, "json", offset: 1, limit: 1);

            Assert.True(result.ExitCode == 0, $"HlogInspect failed with exit code {result.ExitCode}.{Environment.NewLine}STDERR:{Environment.NewLine}{result.StandardError}{Environment.NewLine}STDOUT:{Environment.NewLine}{result.StandardOutput}");
            Assert.DoesNotContain("first payload", result.StandardOutput, StringComparison.Ordinal);
            Assert.Contains("second payload", result.StandardOutput, StringComparison.Ordinal);
            Assert.DoesNotContain("third payload", result.StandardOutput, StringComparison.Ordinal);
            Assert.Contains("offset 1", result.StandardError, StringComparison.Ordinal);
        }
        finally
        {
            if (Directory.Exists(rootDir))
                Directory.Delete(rootDir, recursive: true);
        }
    }

    [Fact]
    public async Task HlogInspect_FiltersByAfterTimestamp_WhenSampleIsInvoked()
    {
        var rootDir = Path.Combine(Path.GetTempPath(), $"hlog_inspect_after_{Guid.NewGuid():N}");
        var pluginId = "inspect-after";
        var timestamp = DateTimeOffset.UtcNow;
        var middleTimestamp = timestamp.AddMilliseconds(1);
        var normalizedMiddleTimestamp = DateTimeOffset.FromUnixTimeMilliseconds(middleTimestamp.ToUnixTimeMilliseconds());

        try
        {
            await SeedDirectLogAsync(
                rootDir,
                pluginId,
                new LogMessage
                {
                    Level = LogPipeLevel.Info,
                    Message = "first payload",
                    Source = "OrdersService",
                    Service = "api-gateway",
                    Timestamp = timestamp,
                });

            await SeedDirectLogAsync(
                rootDir,
                pluginId,
                new LogMessage
                {
                    Level = LogPipeLevel.Info,
                    Message = "second payload",
                    Source = "OrdersService",
                    Service = "api-gateway",
                    Timestamp = middleTimestamp,
                });

            await SeedDirectLogAsync(
                rootDir,
                pluginId,
                new LogMessage
                {
                    Level = LogPipeLevel.Info,
                    Message = "third payload",
                    Source = "OrdersService",
                    Service = "api-gateway",
                    Timestamp = timestamp.AddMilliseconds(2),
                });

            var result = await RunHlogInspectAsync(rootDir, pluginId, "json", afterTimestamp: middleTimestamp.ToString("O"));

            Assert.True(result.ExitCode == 0, $"HlogInspect failed with exit code {result.ExitCode}.{Environment.NewLine}STDERR:{Environment.NewLine}{result.StandardError}{Environment.NewLine}STDOUT:{Environment.NewLine}{result.StandardOutput}");
            Assert.DoesNotContain("first payload", result.StandardOutput, StringComparison.Ordinal);
            Assert.DoesNotContain("second payload", result.StandardOutput, StringComparison.Ordinal);
            Assert.Contains("third payload", result.StandardOutput, StringComparison.Ordinal);
            Assert.Contains($"afterTimestamp={normalizedMiddleTimestamp:O}", result.StandardError, StringComparison.Ordinal);
        }
        finally
        {
            if (Directory.Exists(rootDir))
                Directory.Delete(rootDir, recursive: true);
        }
    }

    [Fact]
    public async Task HlogInspect_FiltersByBeforeTimestamp_WhenSampleIsInvoked()
    {
        var rootDir = Path.Combine(Path.GetTempPath(), $"hlog_inspect_before_{Guid.NewGuid():N}");
        var pluginId = "inspect-before";
        var timestamp = DateTimeOffset.UtcNow;
        var middleTimestamp = timestamp.AddMilliseconds(1);
        var normalizedMiddleTimestamp = DateTimeOffset.FromUnixTimeMilliseconds(middleTimestamp.ToUnixTimeMilliseconds());

        try
        {
            await SeedDirectLogAsync(
                rootDir,
                pluginId,
                new LogMessage
                {
                    Level = LogPipeLevel.Info,
                    Message = "first payload",
                    Source = "OrdersService",
                    Service = "api-gateway",
                    Timestamp = timestamp,
                });

            await SeedDirectLogAsync(
                rootDir,
                pluginId,
                new LogMessage
                {
                    Level = LogPipeLevel.Info,
                    Message = "second payload",
                    Source = "OrdersService",
                    Service = "api-gateway",
                    Timestamp = middleTimestamp,
                });

            await SeedDirectLogAsync(
                rootDir,
                pluginId,
                new LogMessage
                {
                    Level = LogPipeLevel.Info,
                    Message = "third payload",
                    Source = "OrdersService",
                    Service = "api-gateway",
                    Timestamp = timestamp.AddMilliseconds(2),
                });

            var result = await RunHlogInspectAsync(rootDir, pluginId, "json", beforeTimestamp: middleTimestamp.ToString("O"));

            Assert.True(result.ExitCode == 0, $"HlogInspect failed with exit code {result.ExitCode}.{Environment.NewLine}STDERR:{Environment.NewLine}{result.StandardError}{Environment.NewLine}STDOUT:{Environment.NewLine}{result.StandardOutput}");
            Assert.Contains("first payload", result.StandardOutput, StringComparison.Ordinal);
            Assert.DoesNotContain("second payload", result.StandardOutput, StringComparison.Ordinal);
            Assert.DoesNotContain("third payload", result.StandardOutput, StringComparison.Ordinal);
            Assert.Contains($"beforeTimestamp={normalizedMiddleTimestamp:O}", result.StandardError, StringComparison.Ordinal);
        }
        finally
        {
            if (Directory.Exists(rootDir))
                Directory.Delete(rootDir, recursive: true);
        }
    }

    [Fact]
    public async Task HlogInspect_EmitsPageMetadataInJson_WhenPageMetaIsEnabled()
    {
        var rootDir = Path.Combine(Path.GetTempPath(), $"hlog_inspect_meta_{Guid.NewGuid():N}");
        var pluginId = "inspect-meta";
        var timestamp = DateTimeOffset.UtcNow;

        try
        {
            await SeedDirectLogAsync(
                rootDir,
                pluginId,
                new LogMessage
                {
                    Level = LogPipeLevel.Info,
                    Message = "first payload",
                    Source = "OrdersService",
                    Service = "api-gateway",
                    Timestamp = timestamp,
                });

            await SeedDirectLogAsync(
                rootDir,
                pluginId,
                new LogMessage
                {
                    Level = LogPipeLevel.Info,
                    Message = "second payload",
                    Source = "OrdersService",
                    Service = "api-gateway",
                    Timestamp = timestamp.AddMilliseconds(1),
                });

            await SeedDirectLogAsync(
                rootDir,
                pluginId,
                new LogMessage
                {
                    Level = LogPipeLevel.Info,
                    Message = "third payload",
                    Source = "OrdersService",
                    Service = "api-gateway",
                    Timestamp = timestamp.AddMilliseconds(2),
                });

            var result = await RunHlogInspectAsync(rootDir, pluginId, "json", offset: 1, limit: 1, pageMeta: true);

            Assert.True(result.ExitCode == 0, $"HlogInspect failed with exit code {result.ExitCode}.{Environment.NewLine}STDERR:{Environment.NewLine}{result.StandardError}{Environment.NewLine}STDOUT:{Environment.NewLine}{result.StandardOutput}");

            using var document = JsonDocument.Parse(result.StandardOutput);
            var root = document.RootElement;
            var meta = root.GetProperty("Meta");
            Assert.Equal(3, meta.GetProperty("MatchedCount").GetInt32());
            Assert.Equal(1, meta.GetProperty("ReturnedCount").GetInt32());
            Assert.Equal(1, meta.GetProperty("Offset").GetInt32());
            Assert.Equal(1, meta.GetProperty("Limit").GetInt32());
            Assert.True(meta.GetProperty("HasMore").GetBoolean());

            var records = root.GetProperty("Records");
            Assert.Equal(1, records.GetArrayLength());
            Assert.Contains("second payload", records[0].GetProperty("Message").GetString(), StringComparison.Ordinal);
            Assert.Contains("pageMeta=True", result.StandardError, StringComparison.Ordinal);
        }
        finally
        {
            if (Directory.Exists(rootDir))
                Directory.Delete(rootDir, recursive: true);
        }
    }

    [Fact]
    public async Task HlogInspect_EmitsCountsOnlySummary_WhenCountsOnlyIsEnabled()
    {
        var rootDir = Path.Combine(Path.GetTempPath(), $"hlog_inspect_counts_{Guid.NewGuid():N}");
        var pluginId = "inspect-counts";
        var timestamp = DateTimeOffset.UtcNow;

        try
        {
            await SeedDirectLogAsync(
                rootDir,
                pluginId,
                new LogMessage
                {
                    Level = LogPipeLevel.Warn,
                    Message = "first payload",
                    Source = "OrdersService",
                    Service = "api-gateway",
                    Timestamp = timestamp,
                });

            await SeedDirectLogAsync(
                rootDir,
                pluginId,
                new LogMessage
                {
                    Level = LogPipeLevel.Error,
                    Message = "second payload",
                    Source = "OrdersService",
                    Service = "api-gateway",
                    Timestamp = timestamp.AddMilliseconds(1),
                });

            await SeedDirectLogAsync(
                rootDir,
                pluginId,
                new LogMessage
                {
                    Level = LogPipeLevel.Error,
                    Message = "third payload",
                    Source = "OrdersService",
                    Service = "api-gateway",
                    Timestamp = timestamp.AddMilliseconds(2),
                });

            var result = await RunHlogInspectAsync(rootDir, pluginId, "json", offset: 1, limit: 1, countsOnly: true);

            Assert.True(result.ExitCode == 0, $"HlogInspect failed with exit code {result.ExitCode}.{Environment.NewLine}STDERR:{Environment.NewLine}{result.StandardError}{Environment.NewLine}STDOUT:{Environment.NewLine}{result.StandardOutput}");

            using var document = JsonDocument.Parse(result.StandardOutput);
            var root = document.RootElement;
            var meta = root.GetProperty("Meta");
            Assert.Equal(3, meta.GetProperty("MatchedCount").GetInt32());
            Assert.Equal(1, meta.GetProperty("ReturnedCount").GetInt32());
            Assert.True(meta.GetProperty("HasMore").GetBoolean());

            var matchedLevels = root.GetProperty("MatchedLevels");
            Assert.Equal(0, matchedLevels.GetProperty("Info").GetInt32());
            Assert.Equal(1, matchedLevels.GetProperty("Warn").GetInt32());
            Assert.Equal(2, matchedLevels.GetProperty("Error").GetInt32());

            var returnedLevels = root.GetProperty("ReturnedLevels");
            Assert.Equal(0, returnedLevels.GetProperty("Warn").GetInt32());
            Assert.Equal(1, returnedLevels.GetProperty("Error").GetInt32());
            Assert.Contains("countsOnly=True", result.StandardError, StringComparison.Ordinal);
            Assert.DoesNotContain("first payload", result.StandardOutput, StringComparison.Ordinal);
        }
        finally
        {
            if (Directory.Exists(rootDir))
                Directory.Delete(rootDir, recursive: true);
        }
    }

    [Theory]
    [InlineData("table")]
    [InlineData("ndjson")]
    public async Task HlogInspect_RejectsPageMetaForNonJsonFormats_WhenSampleIsInvoked(string format)
    {
        var rootDir = Path.Combine(Path.GetTempPath(), $"hlog_inspect_pagemeta_invalid_{Guid.NewGuid():N}");
        var pluginId = "inspect-pagemeta-invalid";

        try
        {
            var result = await RunHlogInspectAsync(rootDir, pluginId, format, pageMeta: true);

            Assert.NotEqual(0, result.ExitCode);
            Assert.Contains("'--page-meta' is supported only with '--format json'.", result.StandardError, StringComparison.Ordinal);
        }
        finally
        {
            if (Directory.Exists(rootDir))
                Directory.Delete(rootDir, recursive: true);
        }
    }

    private static async Task SeedDirectLogAsync(string rootDir, string pluginId, LogMessage message)
    {
        var pluginDir = Path.Combine(rootDir, pluginId);
        var options = CreateDirectOptions(rootDir, pluginId);

        await using (var writer = new LogPipeWriter(NullLogger<LogPipeWriter>.Instance, options))
        {
            await writer.StartAsync(CancellationToken.None);
            await writer.WriteAsync(message);
            await writer.StopAsync(CancellationToken.None);
        }

        if (!TryOpenClient(pluginDir, out var client))
            return;

        using (client)
        {
            Assert.NotEmpty(Directory.GetFiles(pluginDir, "*.hlog"));
        }
    }

    private static async Task<InspectRunResult> RunHlogInspectAsync(string rootDir, string pluginId, string format, string? source = null, string? service = null, string? traceId = null, string? correlationId = null, string? contains = null, string? containsField = null, string? containsMode = null, string? sort = null, int? offset = null, int? limit = null, string? afterTimestamp = null, string? beforeTimestamp = null, bool pageMeta = false, bool countsOnly = false, string? fields = null, string? outputPath = null, bool appendOutput = false, int? splitOutputSizeBytes = null, string? splitOutputTemplate = null, int? splitOutputStartIndex = null, string? templateNumberStyle = null, string? manifestOutputPath = null, string? manifestFormat = null, bool manifestIncludeTotals = false, bool manifestIncludeWindow = false, bool manifestIncludeLevelCounts = false, bool manifestIncludeMatchCounts = false, bool manifestIncludeOutputHash = false, bool manifestIncludeFilters = false, bool manifestIncludeCommand = false, string? summaryOutputPath = null, string? summaryFormat = null, string? summaryGroupBy = null, int? summaryGroupLimit = null, string? summaryGroupOrder = null, string? summaryIncludeRecords = null, int? summaryRecordCount = null, string? summaryRecordFields = null, string? summaryRecordsFormat = null, string? summaryRecordsOutputPath = null, string? summaryRecordsOutputTemplate = null, string? level = null, string? levelIn = null, bool useDashedIdentifierAliases = false)
    {
        var repoRoot = FindRepoRoot();
        var inspectProject = Path.Combine(repoRoot, "samples", "HlogInspect", "HlogInspect.csproj");
        var filterArgs = string.Empty;
        if (!string.IsNullOrWhiteSpace(source))
            filterArgs += $" --source \"{source}\"";
        if (!string.IsNullOrWhiteSpace(service))
            filterArgs += $" --service \"{service}\"";
        if (!string.IsNullOrWhiteSpace(traceId))
            filterArgs += useDashedIdentifierAliases
                ? $" --trace-id \"{traceId}\""
                : $" --traceId \"{traceId}\"";
        if (!string.IsNullOrWhiteSpace(correlationId))
            filterArgs += useDashedIdentifierAliases
                ? $" --correlation-id \"{correlationId}\""
                : $" --correlationId \"{correlationId}\"";
        if (!string.IsNullOrWhiteSpace(level))
            filterArgs += $" --level \"{level}\"";
        if (!string.IsNullOrWhiteSpace(levelIn))
            filterArgs += $" --level-in \"{levelIn}\"";
        if (!string.IsNullOrWhiteSpace(contains))
            filterArgs += $" --contains \"{contains}\"";
        if (!string.IsNullOrWhiteSpace(containsField))
            filterArgs += $" --contains-field \"{containsField}\"";
        if (!string.IsNullOrWhiteSpace(containsMode))
            filterArgs += $" --contains-mode \"{containsMode}\"";
        if (!string.IsNullOrWhiteSpace(sort))
            filterArgs += $" --sort \"{sort}\"";
        if (offset.HasValue)
            filterArgs += $" --offset {offset.Value}";
        if (limit.HasValue)
            filterArgs += $" --limit {limit.Value}";
        if (!string.IsNullOrWhiteSpace(afterTimestamp))
            filterArgs += $" --after-timestamp \"{afterTimestamp}\"";
        if (!string.IsNullOrWhiteSpace(beforeTimestamp))
            filterArgs += $" --before-timestamp \"{beforeTimestamp}\"";
        if (pageMeta)
            filterArgs += " --page-meta";
        if (countsOnly)
            filterArgs += " --counts-only";
        if (!string.IsNullOrWhiteSpace(fields))
            filterArgs += $" --fields \"{fields}\"";
        if (!string.IsNullOrWhiteSpace(outputPath))
            filterArgs += $" --output \"{outputPath}\"";
        if (appendOutput)
            filterArgs += " --append-output";
        if (splitOutputSizeBytes.HasValue)
            filterArgs += $" --split-output-size {splitOutputSizeBytes.Value}";
        if (!string.IsNullOrWhiteSpace(splitOutputTemplate))
            filterArgs += $" --split-output-template \"{splitOutputTemplate}\"";
        if (splitOutputStartIndex.HasValue)
            filterArgs += $" --split-output-start-index {splitOutputStartIndex.Value}";
        if (!string.IsNullOrWhiteSpace(templateNumberStyle))
            filterArgs += $" --template-number-style \"{templateNumberStyle}\"";
        if (!string.IsNullOrWhiteSpace(manifestOutputPath))
            filterArgs += $" --manifest-output \"{manifestOutputPath}\"";
        if (!string.IsNullOrWhiteSpace(manifestFormat))
            filterArgs += $" --manifest-format \"{manifestFormat}\"";
        if (manifestIncludeTotals)
            filterArgs += " --manifest-include-totals";
        if (manifestIncludeWindow)
            filterArgs += " --manifest-include-window";
        if (manifestIncludeLevelCounts)
            filterArgs += " --manifest-include-level-counts";
        if (manifestIncludeMatchCounts)
            filterArgs += " --manifest-include-match-counts";
        if (manifestIncludeOutputHash)
            filterArgs += " --manifest-include-output-hash";
        if (manifestIncludeFilters)
            filterArgs += " --manifest-include-filters";
        if (manifestIncludeCommand)
            filterArgs += " --manifest-include-command";
        if (!string.IsNullOrWhiteSpace(summaryOutputPath))
            filterArgs += $" --summary-output \"{summaryOutputPath}\"";
        if (!string.IsNullOrWhiteSpace(summaryFormat))
            filterArgs += $" --summary-format \"{summaryFormat}\"";
        if (!string.IsNullOrWhiteSpace(summaryGroupBy))
            filterArgs += $" --summary-group-by \"{summaryGroupBy}\"";
        if (summaryGroupLimit.HasValue)
            filterArgs += $" --summary-group-limit {summaryGroupLimit.Value}";
        if (!string.IsNullOrWhiteSpace(summaryGroupOrder))
            filterArgs += $" --summary-group-order \"{summaryGroupOrder}\"";
        if (!string.IsNullOrWhiteSpace(summaryIncludeRecords))
            filterArgs += $" --summary-include-records \"{summaryIncludeRecords}\"";
        if (summaryRecordCount.HasValue)
            filterArgs += $" --summary-record-count {summaryRecordCount.Value}";
        if (!string.IsNullOrWhiteSpace(summaryRecordFields))
            filterArgs += $" --summary-record-fields \"{summaryRecordFields}\"";
        if (!string.IsNullOrWhiteSpace(summaryRecordsFormat))
            filterArgs += $" --summary-records-format \"{summaryRecordsFormat}\"";
        if (!string.IsNullOrWhiteSpace(summaryRecordsOutputPath))
            filterArgs += $" --summary-records-output \"{summaryRecordsOutputPath}\"";
        if (!string.IsNullOrWhiteSpace(summaryRecordsOutputTemplate))
            filterArgs += $" --summary-records-template \"{summaryRecordsOutputTemplate}\"";
        var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = "dotnet",
                Arguments = $"run --project \"{inspectProject}\" --no-restore -- --base-dir \"{rootDir}\" --plugin-id {pluginId} --minutes 60 --format {format}{filterArgs}",
                WorkingDirectory = repoRoot,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
            }
        };

        process.Start();
        var stdout = await process.StandardOutput.ReadToEndAsync();
        var stderr = await process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync(CancellationToken.None);

        return new InspectRunResult(process.ExitCode, stdout, stderr);
    }

    [Fact]
    public async Task HlogInspect_ShowsFieldAliasesInHelp_WhenHelpIsRequested()
    {
        var result = await RunHlogInspectRawAsync("--help");

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("Aliases: --trace-id = --traceId, --correlation-id = --correlationId.", result.StandardOutput, StringComparison.Ordinal);
        Assert.Contains("Field aliases for --fields and --summary-record-fields: trace-id = traceId, correlation-id = correlationId, payload-json = payloadJson.", result.StandardOutput, StringComparison.Ordinal);
        Assert.Equal(string.Empty, result.StandardError);
    }

    [Fact]
    public async Task HlogInspect_ReturnsHelpHint_WhenUnknownOptionIsProvided()
    {
        var result = await RunHlogInspectRawAsync("--bogus");

        Assert.Equal(1, result.ExitCode);
        Assert.Contains("Unknown option: --bogus. Use '--help' to see supported options.", result.StandardError, StringComparison.Ordinal);
    }

    [Fact]
    public async Task HlogInspect_ReturnsHelpHint_WhenOptionValueIsMissing()
    {
        var result = await RunHlogInspectRawAsync("--source");

        Assert.Equal(1, result.ExitCode);
        Assert.Contains("Missing value for --source. Use '--help' to see supported options.", result.StandardError, StringComparison.Ordinal);
    }

    [Fact]
    public async Task HlogInspect_ReturnsHelpHint_WhenInvocationContextIsMissing()
    {
        var result = await RunHlogInspectRawAsync(string.Empty);

        Assert.Equal(1, result.ExitCode);
        Assert.Contains("Provide '--log-dir' or '--plugin-id' together with '--base-dir' (or HOBIARY_PLUGIN_ID/HOBIARY_DIRECT_LOG_DIR). Use '--help' to see supported options.", result.StandardError, StringComparison.Ordinal);
    }

    private static async Task<InspectRunResult> RunHlogInspectRawAsync(string arguments)
    {
        var repoRoot = FindRepoRoot();
        var inspectProject = Path.Combine(repoRoot, "samples", "HlogInspect", "HlogInspect.csproj");
        var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = "dotnet",
                Arguments = $"run --project \"{inspectProject}\" --no-restore -- {arguments}",
                WorkingDirectory = repoRoot,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
            }
        };

        process.Start();
        var stdout = await process.StandardOutput.ReadToEndAsync();
        var stderr = await process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync(CancellationToken.None);

        return new InspectRunResult(process.ExitCode, stdout, stderr);
    }

    private static Microsoft.Extensions.Options.IOptions<InputPluginOptions> CreateDirectOptions(string rootDir, string pluginId)
        => Options.Create(new InputPluginOptions
        {
            Transport = LogPipeTransport.Direct,
            PluginId = pluginId,
            BaseLogDirectory = rootDir,
        });

    private static bool TryOpenClient(string pluginDir, [NotNullWhen(true)] out HobiaryLogClient? client)
    {
        var opened = HobiaryLogClient.TryCreate(pluginDir, out client, out var error);
        Assert.True(opened && client is not null, $"Native log readback failed: {error}");
        return true;
    }

    private static List<LogQueryRecord> QueryWindow(HobiaryLogClient client, DateTimeOffset fromAnchor, DateTimeOffset toAnchor, int limit)
        => client.QueryRange(
            fromAnchor.AddMinutes(-1).ToUnixTimeMilliseconds() * 1000L,
            toAnchor.AddMinutes(1).ToUnixTimeMilliseconds() * 1000L,
            limit: limit);

    private static string GetPayloadText(LogQueryRecord record)
        => Encoding.UTF8.GetString(record.Payload ?? []);

    private static string FindRepoRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "HobiaryLogPipe.slnx")))
                return current.FullName;

            current = current.Parent;
        }

        throw new InvalidOperationException("Unable to locate HobiaryLogPipe repository root.");
    }

    private sealed record InspectRunResult(int ExitCode, string StandardOutput, string StandardError);
}