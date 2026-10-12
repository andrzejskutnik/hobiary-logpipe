using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using HobiaryLogPipe.HobiaryLog;

try
{
    var options = InspectCliOptions.Parse(args);
    var logDirectory = options.ResolveLogDirectory();

    if (!HobiaryLogClient.TryCreate(logDirectory, out var client, out var error) || client is null)
    {
        Console.Error.WriteLine($"Unable to open HobiaryLogDB directory '{logDirectory}': {error}");
        Environment.ExitCode = 1;
        return;
    }

    using (client)
    {
        var fromUs = options.From.ToUnixTimeMilliseconds() * 1000L;
        var toUs = options.To.ToUnixTimeMilliseconds() * 1000L;
        var records = client.QueryRange(fromUs, toUs, options.Level, int.MaxValue);
        var matchedViews = OrderByDirection(
                records
                    .Select(ToInspectRecordView)
                    .Where(view => MatchesFilters(view, options.Source, options.Service, options.TraceId, options.CorrelationId, options.LevelIn, options.Contains, options.ContainsField, options.ContainsMode, options.AfterTimestamp, options.BeforeTimestamp)),
                options.Sort)
            .ToArray();
        var filteredViews = matchedViews
            .Skip(options.Offset)
            .Take(options.Limit)
            .ToArray();
        var matchedCounts = CountByLevel(matchedViews);
        var returnedCounts = CountByLevel(filteredViews);
        var pageMetadata = new PageMetadata(
            matchedViews.Length,
            filteredViews.Length,
            options.Offset,
            options.Limit,
            options.Offset + filteredViews.Length < matchedViews.Length,
            options.Sort.ToString().ToLowerInvariant(),
            options.From,
            options.To,
            options.AfterTimestamp,
            options.BeforeTimestamp);

        Console.Error.WriteLine($"Inspecting '{logDirectory}' from {options.From:O} to {options.To:O}, level >= {(options.Level?.ToString() ?? "Trace")}, levelIn={(options.LevelIn ?? "*")}, limit {options.Limit}, offset {options.Offset}, sort={options.Sort.ToString().ToLowerInvariant()}, source={(options.Source ?? "*")}, service={(options.Service ?? "*")}, traceId={(options.TraceId ?? "*")}, correlationId={(options.CorrelationId ?? "*")}, contains={(options.Contains ?? "*")}, containsField={options.ContainsField.ToString().ToLowerInvariant()}, containsMode={options.ContainsMode.ToString().ToLowerInvariant()}, afterTimestamp={(options.AfterTimestamp?.ToString("O") ?? "*")}, beforeTimestamp={(options.BeforeTimestamp?.ToString("O") ?? "*")}, pageMeta={options.PageMeta}, countsOnly={options.CountsOnly}, fields={(options.Fields.Count == 0 ? "*" : string.Join(",", options.Fields.Select(GetFieldName)))}, output={(options.OutputPath ?? "stdout")}, appendOutput={options.AppendOutput}, splitOutputSize={(options.SplitOutputSizeBytes?.ToString() ?? "*")}, splitOutputTemplate={(options.SplitOutputTemplate ?? "*")}, splitOutputStartIndex={options.SplitOutputStartIndex}, templateNumberStyle={options.TemplateNumberStyle.ToString().ToLowerInvariant()}, manifestOutput={(options.ManifestOutputPath ?? "*")}, manifestFormat={options.ManifestOutputFormat.ToString().ToLowerInvariant()}, manifestIncludeTotals={options.ManifestIncludeTotals}, manifestIncludeWindow={options.ManifestIncludeWindow}, manifestIncludeLevelCounts={options.ManifestIncludeLevelCounts}, manifestIncludeMatchCounts={options.ManifestIncludeMatchCounts}, manifestIncludeOutputHash={options.ManifestIncludeOutputHash}, manifestIncludeFilters={options.ManifestIncludeFilters}, manifestIncludeCommand={options.ManifestIncludeCommand}, summaryOutput={(options.SummaryOutputPath ?? "*")}, summaryFormat={options.SummaryOutputFormat.ToString().ToLowerInvariant()}, summaryGroupBy={(options.SummaryGroupBy.HasValue ? GetSummaryGroupByName(options.SummaryGroupBy.Value) : "*")}, summaryGroupLimit={(options.SummaryGroupLimit?.ToString() ?? "*")}, summaryGroupOrder={(options.SummaryGroupOrder?.ToString().ToLowerInvariant() ?? "*")}, summaryIncludeRecords={(options.SummaryIncludeRecords?.ToString().ToLowerInvariant() ?? "*")}, summaryRecordCount={options.SummaryRecordCount}, summaryRecordFields={(options.SummaryRecordFields.Count == 0 ? "*" : string.Join(",", options.SummaryRecordFields.Select(GetFieldName)))}, summaryRecordsFormat={(options.SummaryRecordsFormat?.ToString().ToLowerInvariant() ?? "*")}, summaryRecordsOutput={(options.SummaryRecordsOutputPath ?? "*")}, summaryRecordsTemplate={(options.SummaryRecordsOutputTemplate ?? "*")}.");
        Console.Error.WriteLine($"Read {filteredViews.Length} record(s). Level counters in selected window: trace={returnedCounts[0]}, debug={returnedCounts[1]}, info={returnedCounts[2]}, warn={returnedCounts[3]}, error={returnedCounts[4]}, fatal={returnedCounts[5]}.");

        var manifestTotalsContext = new ManifestTotalsContext(
            pageMetadata,
            options.Level?.ToString() ?? "Trace",
            options.Source,
            options.Service,
            options.TraceId,
            options.CorrelationId,
            options.LevelIn,
            options.Contains,
            options.ContainsField,
            options.ContainsMode,
            Environment.CommandLine,
            ToLevelCounters(matchedCounts),
            ToLevelCounters(returnedCounts));

        WriteOutput(options.Format, filteredViews, pageMetadata, options.PageMeta, options.CountsOnly, matchedCounts, returnedCounts, options.Fields, options.OutputPath, options.AppendOutput, options.SplitOutputSizeBytes, options.SplitOutputTemplate, options.SplitOutputStartIndex, options.TemplateNumberStyle, options.ManifestOutputPath, options.ManifestOutputFormat, options.ManifestIncludeTotals, options.ManifestIncludeWindow, options.ManifestIncludeLevelCounts, options.ManifestIncludeMatchCounts, options.ManifestIncludeOutputHash, options.ManifestIncludeFilters, options.ManifestIncludeCommand, manifestTotalsContext, options.SummaryOutputPath, options.SummaryOutputFormat, options.SummaryGroupBy, options.SummaryGroupLimit, options.SummaryGroupOrder, options.SummaryIncludeRecords, options.SummaryRecordCount, options.SummaryRecordFields, options.SummaryRecordsFormat, options.SummaryRecordsOutputPath, options.SummaryRecordsOutputTemplate);
    }
}
catch (ArgumentException ex)
{
    Console.Error.WriteLine(ex.Message);
    Environment.ExitCode = 1;
}

static bool MatchesFilters(InspectRecordView view, string? source, string? service, string? traceId, string? correlationId, string? levelIn, string? contains, ContainsField containsField, ContainsMode containsMode, DateTimeOffset? afterTimestamp, DateTimeOffset? beforeTimestamp)
{
    if (!MatchesAnyFilterToken(view.Source, source))
        return false;
    if (!MatchesAnyFilterToken(view.Service, service))
        return false;
    if (!MatchesAnyFilterToken(view.TraceId, traceId))
        return false;
    if (!MatchesAnyFilterToken(view.CorrelationId, correlationId))
        return false;
    if (!MatchesAnyFilterToken(view.Level, levelIn))
        return false;

    if (!string.IsNullOrWhiteSpace(contains))
    {
        var searchTerms = contains
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(term => !string.IsNullOrWhiteSpace(term))
            .ToArray();
        var matches = containsMode switch
        {
            ContainsMode.All => searchTerms.All(search =>
            {
                var messageMatch = (containsField == ContainsField.Any || containsField == ContainsField.Message)
                    && view.Message?.Contains(search, StringComparison.OrdinalIgnoreCase) == true;
                var payloadMatch = (containsField == ContainsField.Any || containsField == ContainsField.Payload)
                    && view.PayloadJson?.Contains(search, StringComparison.OrdinalIgnoreCase) == true;
                return messageMatch || payloadMatch;
            }),
            _ => searchTerms.Any(search =>
            {
                var messageMatch = (containsField == ContainsField.Any || containsField == ContainsField.Message)
                    && view.Message?.Contains(search, StringComparison.OrdinalIgnoreCase) == true;
                var payloadMatch = (containsField == ContainsField.Any || containsField == ContainsField.Payload)
                    && view.PayloadJson?.Contains(search, StringComparison.OrdinalIgnoreCase) == true;
                return messageMatch || payloadMatch;
            }),
        };

        if (!matches)
            return false;
    }

    if (afterTimestamp.HasValue && view.Timestamp <= afterTimestamp.Value)
        return false;
    if (beforeTimestamp.HasValue && view.Timestamp >= beforeTimestamp.Value)
        return false;

    return true;
}

static bool MatchesAnyFilterToken(string? candidate, string? filter)
{
    if (string.IsNullOrWhiteSpace(filter))
        return true;

    if (string.IsNullOrWhiteSpace(candidate))
        return false;

    var tokens = filter
        .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
        .Where(token => !string.IsNullOrWhiteSpace(token));

    return tokens.Any(token => string.Equals(candidate, token, StringComparison.OrdinalIgnoreCase));
}

static IEnumerable<InspectRecordView> OrderByDirection(IEnumerable<InspectRecordView> views, SortDirection direction)
    => direction == SortDirection.Desc
        ? views.OrderByDescending(view => view.Timestamp)
        : views.OrderBy(view => view.Timestamp);

static int[] CountByLevel(IEnumerable<InspectRecordView> views)
{
    var counts = new int[6];
    foreach (var view in views)
    {
        var index = view.Level switch
        {
            "Trace" => 0,
            "Debug" => 1,
            "Info" => 2,
            "Warn" => 3,
            "Error" => 4,
            "Fatal" => 5,
            _ => -1,
        };

        if (index >= 0)
            counts[index]++;
    }

    return counts;
}

static InspectRecordView ToInspectRecordView(LogQueryRecord record)
{
    var rawPayload = record.Payload is { Length: > 0 }
        ? Encoding.UTF8.GetString(record.Payload)
        : string.Empty;

    if (TryParsePayload(rawPayload, out var payloadView))
    {
        return new InspectRecordView(
            Timestamp: DateTimeOffset.FromUnixTimeMilliseconds(record.TimestampUs / 1000L),
            Level: record.Level.ToString(),
            Source: payloadView.Source ?? record.Source,
            Service: payloadView.Service ?? record.Service,
            Message: payloadView.Message ?? rawPayload,
            TraceId: record.TraceId,
            CorrelationId: record.CorrelationId,
            PayloadJson: rawPayload);
    }

    return new InspectRecordView(
        Timestamp: DateTimeOffset.FromUnixTimeMilliseconds(record.TimestampUs / 1000L),
        Level: record.Level.ToString(),
        Source: record.Source,
        Service: record.Service,
        Message: string.IsNullOrWhiteSpace(rawPayload) ? null : rawPayload,
        TraceId: record.TraceId,
        CorrelationId: record.CorrelationId,
        PayloadJson: string.IsNullOrWhiteSpace(rawPayload) ? null : rawPayload);
}

static bool TryParsePayload(string payload, out PayloadView payloadView)
{
    payloadView = new PayloadView(null, null, null);
    if (string.IsNullOrWhiteSpace(payload) || payload[0] != '{')
        return false;

    try
    {
        using var document = JsonDocument.Parse(payload);
        var root = document.RootElement;
        payloadView = new PayloadView(
            ReadString(root, "source"),
            ReadString(root, "service"),
            ReadString(root, "message"));
        return true;
    }
    catch (JsonException)
    {
        return false;
    }
}

static string? ReadString(JsonElement element, string propertyName)
    => element.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.String
        ? value.GetString()
        : null;

static void WriteOutput(OutputFormat format, InspectRecordView[] views, PageMetadata metadata, bool pageMeta, bool countsOnly, int[] matchedCounts, int[] returnedCounts, IReadOnlyList<InspectField> fields, string? outputPath, bool appendOutput, int? splitOutputSizeBytes, string? splitOutputTemplate, int splitOutputStartIndex, TemplateNumberStyle templateNumberStyle, string? manifestOutputPath, ManifestOutputFormat manifestOutputFormat, bool manifestIncludeTotals, bool manifestIncludeWindow, bool manifestIncludeLevelCounts, bool manifestIncludeMatchCounts, bool manifestIncludeOutputHash, bool manifestIncludeFilters, bool manifestIncludeCommand, ManifestTotalsContext manifestTotalsContext, string? summaryOutputPath, SummaryOutputFormat summaryOutputFormat, SummaryGroupByField? summaryGroupBy, int? summaryGroupLimit, SummaryGroupOrder? summaryGroupOrder, SummaryIncludeRecords? summaryIncludeRecords, int summaryRecordCount, IReadOnlyList<InspectField> summaryRecordFields, SummaryRecordsFormat? summaryRecordsFormat, string? summaryRecordsOutputPath, string? summaryRecordsOutputTemplate)
{
    var summary = new CountsOnlySummary(
        metadata,
        ToLevelCounters(matchedCounts),
        ToLevelCounters(returnedCounts));

    var sampleViews = GetSummarySampleViews(views, summaryIncludeRecords, summaryRecordCount) ?? [];
    var effectiveSummaryRecordsOutputPath = ResolveSummaryRecordsOutputPath(summaryOutputPath, summaryRecordsOutputPath, summaryRecordsOutputTemplate, summaryIncludeRecords, summaryRecordsFormat, sampleViews.Length, templateNumberStyle);

    if (!string.IsNullOrWhiteSpace(summaryOutputPath))
        WriteSummaryOutput(summary, views, summaryOutputPath, summaryOutputFormat, summaryGroupBy, summaryGroupLimit, summaryGroupOrder, summaryIncludeRecords, summaryRecordCount, summaryRecordFields, summaryRecordsFormat);

    if (!string.IsNullOrWhiteSpace(effectiveSummaryRecordsOutputPath))
        WriteSummaryRecordsOutput(views, effectiveSummaryRecordsOutputPath, summaryIncludeRecords, summaryRecordCount, summaryRecordFields, summaryRecordsFormat);

    if (splitOutputSizeBytes.HasValue)
    {
        var manifest = WriteSplitOutput(format, views, fields, outputPath!, splitOutputSizeBytes.Value, splitOutputTemplate, splitOutputStartIndex, templateNumberStyle, manifestIncludeOutputHash);
        if (!string.IsNullOrWhiteSpace(manifestOutputPath))
            WriteSplitManifest(manifest, manifestOutputPath, manifestOutputFormat, manifestIncludeTotals, manifestIncludeWindow, manifestIncludeLevelCounts, manifestIncludeMatchCounts, manifestIncludeFilters, manifestIncludeCommand, manifestTotalsContext);
        return;
    }

    using var fileWriter = CreateFileOutputWriter(outputPath, appendOutput, out var outputHadContent);
    var writer = (TextWriter?)fileWriter ?? Console.Out;

    if (countsOnly)
    {
        WriteCountsOnly(format, summary, writer);
        return;
    }

    var projectedViews = fields.Count == 0
        ? null
        : views.Select(view => ProjectView(view, fields)).ToArray();

    switch (format)
    {
        case OutputFormat.Json:
            if (pageMeta)
            {
                writer.WriteLine(JsonSerializer.Serialize(new { Meta = metadata, Records = (object?)projectedViews ?? views }, new JsonSerializerOptions { WriteIndented = true }));
                break;
            }

            writer.WriteLine(JsonSerializer.Serialize((object?)projectedViews ?? views, new JsonSerializerOptions { WriteIndented = true }));
            break;
        case OutputFormat.Ndjson:
            if (pageMeta)
                throw new ArgumentException("'--page-meta' is supported only with '--format json'.");

            foreach (var row in projectedViews ?? views.Cast<object>().ToArray())
                writer.WriteLine(JsonSerializer.Serialize(row));
            break;
        case OutputFormat.Csv:
            WriteCsv(views, fields, writer, appendOutput && outputHadContent);
            break;
        default:
            if (pageMeta)
                throw new ArgumentException("'--page-meta' is supported only with '--format json'.");

            WriteTable(views, writer);
            break;
    }

    fileWriter?.Flush();
    fileWriter?.Dispose();
    if (!string.IsNullOrWhiteSpace(manifestOutputPath) && !string.IsNullOrWhiteSpace(outputPath))
    {
        var manifest = CreateSingleOutputManifest(format, outputPath, views.Length, manifestIncludeOutputHash);
        WriteSplitManifest(manifest, manifestOutputPath, manifestOutputFormat, manifestIncludeTotals, manifestIncludeWindow, manifestIncludeLevelCounts, manifestIncludeMatchCounts, manifestIncludeFilters, manifestIncludeCommand, manifestTotalsContext);
    }
}

static string? ResolveSummaryRecordsOutputPath(string? summaryOutputPath, string? summaryRecordsOutputPath, string? summaryRecordsOutputTemplate, SummaryIncludeRecords? summaryIncludeRecords, SummaryRecordsFormat? summaryRecordsFormat, int actualRecordCount, TemplateNumberStyle templateNumberStyle)
{
    if (!string.IsNullOrWhiteSpace(summaryRecordsOutputPath))
        return summaryRecordsOutputPath;

    if (string.IsNullOrWhiteSpace(summaryRecordsOutputTemplate) || string.IsNullOrWhiteSpace(summaryOutputPath) || !summaryIncludeRecords.HasValue)
        return null;

    var summaryDirectory = Path.GetDirectoryName(summaryOutputPath) ?? string.Empty;
    var summaryName = Path.GetFileNameWithoutExtension(summaryOutputPath);
    var effectiveRecordsFormat = summaryRecordsFormat ?? SummaryRecordsFormat.Json;
    var extension = GetSummaryRecordsFileExtension(effectiveRecordsFormat);
    var resolvedTemplate = summaryRecordsOutputTemplate
        .Replace("{name}", summaryName, StringComparison.Ordinal)
        .Replace("{mode}", summaryIncludeRecords.Value.ToString().ToLowerInvariant(), StringComparison.Ordinal)
        .Replace("{format}", effectiveRecordsFormat.ToString().ToLowerInvariant(), StringComparison.Ordinal)
        .Replace("{count}", FormatTemplateNumber(actualRecordCount, templateNumberStyle), StringComparison.Ordinal)
        .Replace("{ext}", extension, StringComparison.Ordinal);

    return Path.IsPathRooted(resolvedTemplate)
        ? resolvedTemplate
        : Path.Combine(summaryDirectory, resolvedTemplate);
}

static string GetSummaryRecordsFileExtension(SummaryRecordsFormat format)
    => format switch
    {
        SummaryRecordsFormat.Json => ".json",
        SummaryRecordsFormat.Table => ".txt",
        SummaryRecordsFormat.Ndjson => ".ndjson",
        SummaryRecordsFormat.Csv => ".csv",
        _ => throw new ArgumentOutOfRangeException(nameof(format), format, null),
    };

static string FormatTemplateNumber(int value, TemplateNumberStyle templateNumberStyle)
    => templateNumberStyle == TemplateNumberStyle.Plain
        ? value.ToString()
        : value.ToString("D4");

static SplitOutputManifest CreateSingleOutputManifest(OutputFormat format, string outputPath, int recordCount, bool includeOutputHash)
{
    var formatName = format.ToString().ToLowerInvariant();
    var parts = new[] { CreateSplitOutputPart(outputPath, recordCount, includeOutputHash) };
    return new SplitOutputManifest(formatName, Path.GetFileName(outputPath), null, parts, CreateOutputHashesSha256(parts));
}

static SplitOutputPart CreateSplitOutputPart(string path, int recordCount, bool includeOutputHash)
    => new(Path.GetFileName(path), recordCount, new FileInfo(path).Length, includeOutputHash ? ComputeSha256(path) : null);

static string ComputeSha256(string path)
{
    using var stream = File.OpenRead(path);
    using var sha256 = System.Security.Cryptography.SHA256.Create();
    return Convert.ToHexString(sha256.ComputeHash(stream)).ToLowerInvariant();
}

static SplitOutputManifest WriteSplitOutput(OutputFormat format, InspectRecordView[] views, IReadOnlyList<InspectField> fields, string outputPath, int splitOutputSizeBytes, string? splitOutputTemplate, int splitOutputStartIndex, TemplateNumberStyle templateNumberStyle, bool includeOutputHash)
{
    var selectedFields = fields.Count == 0
        ? InspectFieldDefinitions.All
        : fields;

    switch (format)
    {
        case OutputFormat.Csv:
            return WriteSplitCsv(views, selectedFields, outputPath, splitOutputSizeBytes, splitOutputTemplate, splitOutputStartIndex, templateNumberStyle, includeOutputHash);
        case OutputFormat.Ndjson:
            return WriteSplitNdjson(views, selectedFields, outputPath, splitOutputSizeBytes, splitOutputTemplate, splitOutputStartIndex, templateNumberStyle, includeOutputHash);
        default:
            throw new ArgumentException("'--split-output-size' is supported only with '--format csv' or '--format ndjson'.");
    }
}

static SplitOutputManifest WriteSplitCsv(InspectRecordView[] views, IReadOnlyList<InspectField> fields, string outputPath, int splitOutputSizeBytes, string? splitOutputTemplate, int splitOutputStartIndex, TemplateNumberStyle templateNumberStyle, bool includeOutputHash)
{
    var header = string.Join(',', fields.Select(GetFieldName));
    var headerBytes = GetUtf8ByteCount(header + Environment.NewLine);
    var partIndex = splitOutputStartIndex;
    var partPath = GetSplitOutputPath(outputPath, partIndex, splitOutputTemplate, templateNumberStyle);
    var writer = OpenSplitWriter(partPath);
    var writtenBytes = 0;
    var headerWritten = false;
    var rowCount = 0;
    var parts = new List<SplitOutputPart>();

    try
    {
        foreach (var view in views)
        {
            var line = string.Join(',', fields.Select(field => EscapeCsv(GetFieldValue(view, field))));
            var lineBytes = GetUtf8ByteCount(line + Environment.NewLine);

            if (!headerWritten)
            {
                writer.WriteLine(header);
                writtenBytes = headerBytes;
                headerWritten = true;
            }

            if (writtenBytes > headerBytes && writtenBytes + lineBytes > splitOutputSizeBytes)
            {
                writer.Dispose();
                parts.Add(CreateSplitOutputPart(partPath, rowCount, includeOutputHash));
                partIndex++;
                partPath = GetSplitOutputPath(outputPath, partIndex, splitOutputTemplate, templateNumberStyle);
                writer = OpenSplitWriter(partPath);
                writer.WriteLine(header);
                writtenBytes = headerBytes;
                rowCount = 0;
            }

            writer.WriteLine(line);
            writtenBytes += lineBytes;
            rowCount++;
        }

        if (!headerWritten)
            writer.WriteLine(header);
    }
    finally
    {
        writer.Dispose();
    }

    parts.Add(CreateSplitOutputPart(partPath, rowCount, includeOutputHash));
    var manifestParts = parts.ToArray();
    return new SplitOutputManifest("csv", Path.GetFileName(outputPath), splitOutputSizeBytes, manifestParts, CreateOutputHashesSha256(manifestParts));
}

static SplitOutputManifest WriteSplitNdjson(InspectRecordView[] views, IReadOnlyList<InspectField> fields, string outputPath, int splitOutputSizeBytes, string? splitOutputTemplate, int splitOutputStartIndex, TemplateNumberStyle templateNumberStyle, bool includeOutputHash)
{
    var partIndex = splitOutputStartIndex;
    var partPath = GetSplitOutputPath(outputPath, partIndex, splitOutputTemplate, templateNumberStyle);
    var writer = OpenSplitWriter(partPath);
    var writtenBytes = 0;
    var rowCount = 0;
    var parts = new List<SplitOutputPart>();

    try
    {
        foreach (var view in views)
        {
            object row = fields.Count == 0
                ? view
                : ProjectView(view, fields);
            var line = JsonSerializer.Serialize(row);
            var lineBytes = GetUtf8ByteCount(line + Environment.NewLine);

            if (writtenBytes > 0 && writtenBytes + lineBytes > splitOutputSizeBytes)
            {
                writer.Dispose();
                parts.Add(CreateSplitOutputPart(partPath, rowCount, includeOutputHash));
                partIndex++;
                partPath = GetSplitOutputPath(outputPath, partIndex, splitOutputTemplate, templateNumberStyle);
                writer = OpenSplitWriter(partPath);
                writtenBytes = 0;
                rowCount = 0;
            }

            writer.WriteLine(line);
            writtenBytes += lineBytes;
            rowCount++;
        }
    }
    finally
    {
        writer.Dispose();
    }

    parts.Add(CreateSplitOutputPart(partPath, rowCount, includeOutputHash));
    var manifestParts = parts.ToArray();
    return new SplitOutputManifest("ndjson", Path.GetFileName(outputPath), splitOutputSizeBytes, manifestParts, CreateOutputHashesSha256(manifestParts));
}

static void WriteSplitManifest(SplitOutputManifest manifest, string manifestOutputPath, ManifestOutputFormat manifestOutputFormat, bool manifestIncludeTotals, bool manifestIncludeWindow, bool manifestIncludeLevelCounts, bool manifestIncludeMatchCounts, bool manifestIncludeFilters, bool manifestIncludeCommand, ManifestTotalsContext manifestTotalsContext)
{
    var parentDirectory = Path.GetDirectoryName(manifestOutputPath);
    if (!string.IsNullOrWhiteSpace(parentDirectory))
        Directory.CreateDirectory(parentDirectory);

    var content = manifestOutputFormat switch
    {
        ManifestOutputFormat.Json => CreateManifestJson(manifest, manifestIncludeFilters, manifestIncludeCommand, manifestTotalsContext),
        ManifestOutputFormat.Ndjson => CreateManifestNdjson(manifest, manifestIncludeTotals, manifestIncludeWindow, manifestIncludeLevelCounts, manifestIncludeMatchCounts, manifestIncludeFilters, manifestIncludeCommand, manifestTotalsContext),
        _ => throw new ArgumentOutOfRangeException(nameof(manifestOutputFormat), manifestOutputFormat, null),
    };

    File.WriteAllText(manifestOutputPath, content, new UTF8Encoding(false));
}

static string CreateManifestJson(SplitOutputManifest manifest, bool manifestIncludeFilters, bool manifestIncludeCommand, ManifestTotalsContext manifestTotalsContext)
{
    var payload = new Dictionary<string, object?>(StringComparer.Ordinal)
    {
        [nameof(manifest.Format)] = manifest.Format,
        [nameof(manifest.OutputFile)] = manifest.OutputFile,
        [nameof(manifest.SplitOutputSizeBytes)] = manifest.SplitOutputSizeBytes,
        [nameof(manifest.Parts)] = manifest.Parts,
        [nameof(manifest.OutputHashesSha256)] = manifest.OutputHashesSha256,
    };

    if (manifestIncludeFilters)
        payload["Filters"] = CreateManifestFilters(manifestTotalsContext);
    if (manifestIncludeCommand)
        payload["CommandLine"] = manifestTotalsContext.CommandLine;

    return JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true });
}

static string CreateManifestNdjson(SplitOutputManifest manifest, bool manifestIncludeTotals, bool manifestIncludeWindow, bool manifestIncludeLevelCounts, bool manifestIncludeMatchCounts, bool manifestIncludeFilters, bool manifestIncludeCommand, ManifestTotalsContext manifestTotalsContext)
{
    var outputHashesSha256 = manifest.OutputHashesSha256;
    var lines = manifest.Parts
        .Select(part => JsonSerializer.Serialize(new
        {
            Type = "part",
            manifest.Format,
            manifest.OutputFile,
            manifest.SplitOutputSizeBytes,
            Part = part,
        }))
        .ToList();

    if (manifestIncludeTotals)
    {
        object totalsLine = manifestIncludeWindow || manifestIncludeFilters || manifestIncludeCommand
            ? new
            {
                Type = "totals",
                manifest.Format,
                manifest.OutputFile,
                manifest.SplitOutputSizeBytes,
                PartCount = manifest.Parts.Count,
                RecordCount = manifest.Parts.Sum(part => part.RecordCount),
                SizeBytes = manifest.Parts.Sum(part => part.SizeBytes),
                OutputHashesSha256 = outputHashesSha256,
                Window = manifestIncludeWindow ? manifestTotalsContext.Window : null,
                Filters = manifestIncludeWindow || manifestIncludeFilters ? CreateManifestFilters(manifestTotalsContext) : null,
                CommandLine = manifestIncludeCommand ? manifestTotalsContext.CommandLine : null,
                LevelCounts = manifestIncludeLevelCounts ? manifestTotalsContext.ReturnedLevels : null,
                MatchedLevelCounts = manifestIncludeMatchCounts ? manifestTotalsContext.MatchedLevels : null,
                ReturnedLevelCounts = manifestIncludeMatchCounts ? manifestTotalsContext.ReturnedLevels : null,
            }
            : new
            {
                Type = "totals",
                manifest.Format,
                manifest.OutputFile,
                manifest.SplitOutputSizeBytes,
                PartCount = manifest.Parts.Count,
                RecordCount = manifest.Parts.Sum(part => part.RecordCount),
                SizeBytes = manifest.Parts.Sum(part => part.SizeBytes),
                OutputHashesSha256 = outputHashesSha256,
                LevelCounts = manifestIncludeLevelCounts ? manifestTotalsContext.ReturnedLevels : null,
                MatchedLevelCounts = manifestIncludeMatchCounts ? manifestTotalsContext.MatchedLevels : null,
                ReturnedLevelCounts = manifestIncludeMatchCounts ? manifestTotalsContext.ReturnedLevels : null,
            };

        lines.Add(JsonSerializer.Serialize(totalsLine));
    }

    return string.Join(Environment.NewLine, lines);
}

static object CreateManifestFilters(ManifestTotalsContext manifestTotalsContext)
    => new
    {
        manifestTotalsContext.Level,
        manifestTotalsContext.Source,
        manifestTotalsContext.Service,
        manifestTotalsContext.TraceId,
        manifestTotalsContext.CorrelationId,
        manifestTotalsContext.Contains,
        ContainsField = manifestTotalsContext.ContainsField.ToString().ToLowerInvariant(),
        ContainsMode = manifestTotalsContext.ContainsMode.ToString().ToLowerInvariant(),
    };

static IReadOnlyDictionary<string, string>? CreateOutputHashesSha256(IReadOnlyList<SplitOutputPart> parts)
{
    var hashedParts = parts
        .Where(part => !string.IsNullOrWhiteSpace(part.OutputHashSha256))
        .ToDictionary(part => part.FileName, part => part.OutputHashSha256!, StringComparer.Ordinal);

    return hashedParts.Count == 0 ? null : hashedParts;
}

static void WriteSummaryOutput(CountsOnlySummary summary, InspectRecordView[] views, string summaryOutputPath, SummaryOutputFormat summaryOutputFormat, SummaryGroupByField? summaryGroupBy, int? summaryGroupLimit, SummaryGroupOrder? summaryGroupOrder, SummaryIncludeRecords? summaryIncludeRecords, int summaryRecordCount, IReadOnlyList<InspectField> summaryRecordFields, SummaryRecordsFormat? summaryRecordsFormat)
{
    var parentDirectory = Path.GetDirectoryName(summaryOutputPath);
    if (!string.IsNullOrWhiteSpace(parentDirectory))
        Directory.CreateDirectory(parentDirectory);

    var content = summaryOutputFormat switch
    {
        SummaryOutputFormat.Json => CreateSummaryJson(summary, views, summaryGroupBy, summaryGroupLimit, summaryGroupOrder, summaryIncludeRecords, summaryRecordCount, summaryRecordFields, summaryRecordsFormat),
        SummaryOutputFormat.Table => CreateSummaryTable(summary, views, summaryGroupBy, summaryGroupLimit, summaryGroupOrder, summaryIncludeRecords, summaryRecordCount, summaryRecordFields, summaryRecordsFormat),
        _ => throw new ArgumentOutOfRangeException(nameof(summaryOutputFormat), summaryOutputFormat, null),
    };

    File.WriteAllText(summaryOutputPath, content, new UTF8Encoding(false));
}

static void WriteSummaryRecordsOutput(InspectRecordView[] views, string summaryRecordsOutputPath, SummaryIncludeRecords? summaryIncludeRecords, int summaryRecordCount, IReadOnlyList<InspectField> summaryRecordFields, SummaryRecordsFormat? summaryRecordsFormat)
{
    var sampleViews = GetSummarySampleViews(views, summaryIncludeRecords, summaryRecordCount) ?? [];
    var parentDirectory = Path.GetDirectoryName(summaryRecordsOutputPath);
    if (!string.IsNullOrWhiteSpace(parentDirectory))
        Directory.CreateDirectory(parentDirectory);

    var effectiveRecordsFormat = summaryRecordsFormat ?? SummaryRecordsFormat.Json;
    var content = effectiveRecordsFormat switch
    {
        SummaryRecordsFormat.Json => JsonSerializer.Serialize(CreateSummaryRecordObjects(sampleViews, summaryRecordFields), new JsonSerializerOptions { WriteIndented = true }),
        SummaryRecordsFormat.Table => CreateSummaryTableBlock(sampleViews, summaryIncludeRecords, summaryRecordFields),
        SummaryRecordsFormat.Ndjson => CreateSummaryNdjsonBlock(sampleViews, summaryRecordFields),
        SummaryRecordsFormat.Csv => CreateSummaryCsvBlock(sampleViews, summaryRecordFields),
        _ => throw new ArgumentOutOfRangeException(nameof(summaryRecordsFormat), summaryRecordsFormat, null),
    };

    File.WriteAllText(summaryRecordsOutputPath, content, new UTF8Encoding(false));
}

static string CreateSummaryJson(CountsOnlySummary summary, InspectRecordView[] views, SummaryGroupByField? summaryGroupBy, int? summaryGroupLimit, SummaryGroupOrder? summaryGroupOrder, SummaryIncludeRecords? summaryIncludeRecords, int summaryRecordCount, IReadOnlyList<InspectField> summaryRecordFields, SummaryRecordsFormat? summaryRecordsFormat)
{
    var sampleViews = GetSummarySampleViews(views, summaryIncludeRecords, summaryRecordCount);
    var groups = summaryGroupBy.HasValue ? CreateSummaryGroups(views, summaryGroupBy.Value, summaryGroupOrder, summaryGroupLimit) : null;
    if (sampleViews is null && groups is null)
        return JsonSerializer.Serialize(summary, new JsonSerializerOptions { WriteIndented = true });

    var effectiveRecordsFormat = summaryRecordsFormat ?? SummaryRecordsFormat.Json;
    var payload = new Dictionary<string, object?>(StringComparer.Ordinal)
    {
        [nameof(summary.Meta)] = summary.Meta,
        [nameof(summary.MatchedLevels)] = summary.MatchedLevels,
        [nameof(summary.ReturnedLevels)] = summary.ReturnedLevels,
    };

    if (groups is not null)
    {
        payload["GroupBy"] = GetSummaryGroupByName(summaryGroupBy!.Value);
        payload["GroupLimit"] = summaryGroupLimit;
        payload["GroupOrder"] = summaryGroupOrder?.ToString().ToLowerInvariant();
        payload["Groups"] = groups;
    }

    if (sampleViews is not null)
    {
        payload[effectiveRecordsFormat switch
        {
            SummaryRecordsFormat.Json => "Records",
            SummaryRecordsFormat.Table => "RecordsTable",
            SummaryRecordsFormat.Ndjson => "RecordsNdjson",
            SummaryRecordsFormat.Csv => "RecordsCsv",
            _ => throw new ArgumentOutOfRangeException(nameof(summaryRecordsFormat), summaryRecordsFormat, null),
        }] = effectiveRecordsFormat switch
        {
            SummaryRecordsFormat.Json => CreateSummaryRecordObjects(sampleViews, summaryRecordFields),
            SummaryRecordsFormat.Table => CreateSummaryTableBlock(sampleViews, summaryIncludeRecords, summaryRecordFields),
            SummaryRecordsFormat.Ndjson => CreateSummaryNdjsonBlock(sampleViews, summaryRecordFields),
            SummaryRecordsFormat.Csv => CreateSummaryCsvBlock(sampleViews, summaryRecordFields),
            _ => throw new ArgumentOutOfRangeException(nameof(summaryRecordsFormat), summaryRecordsFormat, null),
        };
    }

    return JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true });
}

static string CreateSummaryTable(CountsOnlySummary summary, InspectRecordView[] views, SummaryGroupByField? summaryGroupBy, int? summaryGroupLimit, SummaryGroupOrder? summaryGroupOrder, SummaryIncludeRecords? summaryIncludeRecords, int summaryRecordCount, IReadOnlyList<InspectField> summaryRecordFields, SummaryRecordsFormat? summaryRecordsFormat)
{
    var builder = new StringBuilder();
    builder.AppendLine($"matchedCount={summary.Meta.MatchedCount} returnedCount={summary.Meta.ReturnedCount} hasMore={summary.Meta.HasMore.ToString().ToLowerInvariant()} offset={summary.Meta.Offset} limit={summary.Meta.Limit}");
    builder.AppendLine($"sort={summary.Meta.Sort} from={summary.Meta.From:O} to={summary.Meta.To:O} afterTimestamp={(summary.Meta.AfterTimestamp?.ToString("O") ?? "*")} beforeTimestamp={(summary.Meta.BeforeTimestamp?.ToString("O") ?? "*")}");
    builder.AppendLine($"matchedLevels trace={summary.MatchedLevels.Trace} debug={summary.MatchedLevels.Debug} info={summary.MatchedLevels.Info} warn={summary.MatchedLevels.Warn} error={summary.MatchedLevels.Error} fatal={summary.MatchedLevels.Fatal}");
    builder.Append($"returnedLevels trace={summary.ReturnedLevels.Trace} debug={summary.ReturnedLevels.Debug} info={summary.ReturnedLevels.Info} warn={summary.ReturnedLevels.Warn} error={summary.ReturnedLevels.Error} fatal={summary.ReturnedLevels.Fatal}");

    if (summaryGroupBy.HasValue)
    {
        var groups = CreateSummaryGroups(views, summaryGroupBy.Value, summaryGroupOrder, summaryGroupLimit);
        builder.AppendLine();
        builder.AppendLine();
        builder.AppendLine($"groupBy={GetSummaryGroupByName(summaryGroupBy.Value)} count={groups.Length} order={(summaryGroupOrder?.ToString().ToLowerInvariant() ?? "default")} limit={(summaryGroupLimit?.ToString() ?? "*")}");
        foreach (var group in groups)
            builder.AppendLine($"group key={group.Key} count={group.Count}");
        if (GetSummarySampleViews(views, summaryIncludeRecords, summaryRecordCount) is { Length: > 0 })
            builder.AppendLine();
    }

    var sampleViews = GetSummarySampleViews(views, summaryIncludeRecords, summaryRecordCount);
    if (sampleViews is { Length: > 0 })
    {
        var effectiveRecordsFormat = summaryRecordsFormat ?? SummaryRecordsFormat.Table;
        builder.AppendLine();
        builder.AppendLine();
        builder.AppendLine($"records={summaryIncludeRecords?.ToString().ToLowerInvariant()} count={sampleViews.Length} format={effectiveRecordsFormat.ToString().ToLowerInvariant()}");
        if (effectiveRecordsFormat == SummaryRecordsFormat.Json)
        {
            builder.AppendLine(JsonSerializer.Serialize(CreateSummaryRecordObjects(sampleViews, summaryRecordFields), new JsonSerializerOptions { WriteIndented = true }));
        }
        else if (effectiveRecordsFormat == SummaryRecordsFormat.Ndjson)
        {
            builder.AppendLine(CreateSummaryNdjsonBlock(sampleViews, summaryRecordFields));
        }
        else if (effectiveRecordsFormat == SummaryRecordsFormat.Csv)
        {
            builder.AppendLine(CreateSummaryCsvBlock(sampleViews, summaryRecordFields));
        }
        else
        {
            foreach (var view in sampleViews)
                builder.AppendLine(FormatSummaryRecordLine(view, summaryRecordFields));
        }
    }

    return builder.ToString();
}

static object[] CreateSummaryRecordObjects(InspectRecordView[] sampleViews, IReadOnlyList<InspectField> summaryRecordFields)
    => summaryRecordFields.Count == 0
        ? sampleViews.Cast<object>().ToArray()
        : sampleViews.Select(view => (object)ProjectView(view, summaryRecordFields)).ToArray();

static InspectRecordView[]? GetSummarySampleViews(InspectRecordView[] views, SummaryIncludeRecords? summaryIncludeRecords, int summaryRecordCount)
    => summaryIncludeRecords switch
    {
        SummaryIncludeRecords.First when views.Length > 0 => views.Take(summaryRecordCount).ToArray(),
        SummaryIncludeRecords.Last when views.Length > 0 => views.Skip(Math.Max(0, views.Length - summaryRecordCount)).ToArray(),
        _ => null,
    };

static string CreateSummaryTableBlock(InspectRecordView[] sampleViews, SummaryIncludeRecords? summaryIncludeRecords, IReadOnlyList<InspectField> summaryRecordFields)
{
    var builder = new StringBuilder();
    builder.AppendLine($"records={summaryIncludeRecords?.ToString().ToLowerInvariant()} count={sampleViews.Length} format=table");
    foreach (var view in sampleViews)
        builder.AppendLine(FormatSummaryRecordLine(view, summaryRecordFields));

    return builder.ToString().TrimEnd();
}

static string CreateSummaryNdjsonBlock(InspectRecordView[] sampleViews, IReadOnlyList<InspectField> summaryRecordFields)
{
    var projectedObjects = CreateSummaryRecordObjects(sampleViews, summaryRecordFields);
    return string.Join(Environment.NewLine, projectedObjects.Select(record => JsonSerializer.Serialize(record)));
}

static string CreateSummaryCsvBlock(InspectRecordView[] sampleViews, IReadOnlyList<InspectField> summaryRecordFields)
{
    var fields = summaryRecordFields.Count == 0
        ? InspectFieldDefinitions.All
        : summaryRecordFields;

    var builder = new StringBuilder();
    builder.AppendLine(string.Join(',', fields.Select(GetFieldName)));
    foreach (var view in sampleViews)
        builder.AppendLine(string.Join(',', fields.Select(field => EscapeCsv(GetFieldValue(view, field)))));

    return builder.ToString().TrimEnd();
}

static SummaryGroupEntry[] CreateSummaryGroups(InspectRecordView[] views, SummaryGroupByField summaryGroupBy, SummaryGroupOrder? summaryGroupOrder, int? summaryGroupLimit)
{
    var groups = summaryGroupBy switch
    {
        SummaryGroupByField.Level =>
            views.GroupBy(view => view.Level, StringComparer.OrdinalIgnoreCase)
                .Select(group => new SummaryGroupEntry(group.Key, group.Count(), GetLevelOrder(group.Key)))
                .ToArray(),
        SummaryGroupByField.Source =>
            views.GroupBy(view => view.Source ?? "<null>", StringComparer.OrdinalIgnoreCase)
                .Select(group => new SummaryGroupEntry(group.Key, group.Count()))
                .ToArray(),
        SummaryGroupByField.Service =>
            views.GroupBy(view => view.Service ?? "<null>", StringComparer.OrdinalIgnoreCase)
                .Select(group => new SummaryGroupEntry(group.Key, group.Count()))
                .ToArray(),
        SummaryGroupByField.TraceId =>
            views.GroupBy(view => view.TraceId ?? "<null>", StringComparer.OrdinalIgnoreCase)
                .Select(group => new SummaryGroupEntry(group.Key, group.Count()))
                .ToArray(),
        SummaryGroupByField.CorrelationId =>
            views.GroupBy(view => view.CorrelationId ?? "<null>", StringComparer.OrdinalIgnoreCase)
                .Select(group => new SummaryGroupEntry(group.Key, group.Count()))
                .ToArray(),
        _ => throw new ArgumentOutOfRangeException(nameof(summaryGroupBy), summaryGroupBy, null),
    };

    IOrderedEnumerable<SummaryGroupEntry> orderedGroups = summaryGroupOrder switch
    {
        SummaryGroupOrder.Count => groups
            .OrderByDescending(entry => entry.Count)
            .ThenBy(entry => summaryGroupBy == SummaryGroupByField.Level ? entry.SortOrder : int.MaxValue)
            .ThenBy(entry => entry.Key, StringComparer.OrdinalIgnoreCase),
        SummaryGroupOrder.Key => groups
            .OrderBy(entry => summaryGroupBy == SummaryGroupByField.Level ? entry.SortOrder : int.MaxValue)
            .ThenBy(entry => entry.Key, StringComparer.OrdinalIgnoreCase),
        _ when summaryGroupBy == SummaryGroupByField.Level => groups
            .OrderBy(entry => entry.SortOrder)
            .ThenBy(entry => entry.Key, StringComparer.OrdinalIgnoreCase),
        _ => groups
            .OrderByDescending(entry => entry.Count)
            .ThenBy(entry => entry.Key, StringComparer.OrdinalIgnoreCase),
    };

    var limitedGroups = summaryGroupLimit.HasValue
        ? orderedGroups.Take(summaryGroupLimit.Value)
        : orderedGroups;

    return summaryGroupBy == SummaryGroupByField.Level
        ? limitedGroups.Select(entry => new SummaryGroupEntry(entry.Key, entry.Count)).ToArray()
        : limitedGroups.ToArray();
}

static int GetLevelOrder(string level)
    => level switch
    {
        "Trace" => 0,
        "Debug" => 1,
        "Info" => 2,
        "Warn" => 3,
        "Error" => 4,
        "Fatal" => 5,
        _ => int.MaxValue,
    };

static string GetSummaryGroupByName(SummaryGroupByField summaryGroupBy)
    => summaryGroupBy switch
    {
        SummaryGroupByField.Level => "level",
        SummaryGroupByField.Source => "source",
        SummaryGroupByField.Service => "service",
        SummaryGroupByField.TraceId => "traceId",
        SummaryGroupByField.CorrelationId => "correlationId",
        _ => throw new ArgumentOutOfRangeException(nameof(summaryGroupBy), summaryGroupBy, null),
    };

static string FormatSummaryRecordLine(InspectRecordView view, IReadOnlyList<InspectField> summaryRecordFields)
{
    if (summaryRecordFields.Count == 0)
        return $"[{view.Timestamp:O}] {view.Level,-5} source={view.Source ?? "<null>"} service={view.Service ?? "<null>"} message={view.Message ?? "<empty>"}";

    var pairs = summaryRecordFields
        .Select(field => $"{GetFieldName(field)}={FormatSummaryFieldValue(GetFieldValue(view, field))}");
    return string.Join(' ', pairs);
}

static string FormatSummaryFieldValue(object? value)
    => value switch
    {
        null => "<null>",
        DateTimeOffset timestamp => timestamp.ToString("O"),
        _ => value.ToString() ?? string.Empty,
    };

static StreamWriter OpenSplitWriter(string splitPath)
{
    var parentDirectory = Path.GetDirectoryName(splitPath);
    if (!string.IsNullOrWhiteSpace(parentDirectory))
        Directory.CreateDirectory(parentDirectory);

    return new StreamWriter(splitPath, append: false, new UTF8Encoding(false));
}

static string GetSplitOutputPath(string outputPath, int partIndex, string? splitOutputTemplate, TemplateNumberStyle templateNumberStyle)
{
    if (!string.IsNullOrWhiteSpace(splitOutputTemplate))
        return ResolveSplitOutputTemplate(outputPath, splitOutputTemplate, partIndex, templateNumberStyle);

    var directory = Path.GetDirectoryName(outputPath);
    var fileNameWithoutExtension = Path.GetFileNameWithoutExtension(outputPath);
    var extension = Path.GetExtension(outputPath);
    var formattedPartIndex = FormatTemplateNumber(partIndex, templateNumberStyle);
    var splitFileName = string.IsNullOrEmpty(extension)
        ? $"{fileNameWithoutExtension}.part{formattedPartIndex}"
        : $"{fileNameWithoutExtension}.part{formattedPartIndex}{extension}";

    return string.IsNullOrWhiteSpace(directory)
        ? splitFileName
        : Path.Combine(directory, splitFileName);
}

static string ResolveSplitOutputTemplate(string outputPath, string splitOutputTemplate, int partIndex, TemplateNumberStyle templateNumberStyle)
{
    var outputDirectory = Path.GetDirectoryName(outputPath);
    var fileNameWithoutExtension = Path.GetFileNameWithoutExtension(outputPath);
    var extension = Path.GetExtension(outputPath);
    var resolvedTemplate = splitOutputTemplate
        .Replace("{name}", fileNameWithoutExtension, StringComparison.Ordinal)
        .Replace("{part}", FormatTemplateNumber(partIndex, templateNumberStyle), StringComparison.Ordinal)
        .Replace("{ext}", extension, StringComparison.Ordinal);

    return Path.IsPathRooted(resolvedTemplate) || string.IsNullOrWhiteSpace(outputDirectory)
        ? resolvedTemplate
        : Path.Combine(outputDirectory, resolvedTemplate);
}

static int GetUtf8ByteCount(string text)
    => Encoding.UTF8.GetByteCount(text);

static StreamWriter? CreateFileOutputWriter(string? outputPath, bool appendOutput, out bool outputHadContent)
{
    outputHadContent = false;

    if (string.IsNullOrWhiteSpace(outputPath))
        return null;

    var parentDirectory = Path.GetDirectoryName(outputPath);
    if (!string.IsNullOrWhiteSpace(parentDirectory))
        Directory.CreateDirectory(parentDirectory);

    outputHadContent = File.Exists(outputPath) && new FileInfo(outputPath).Length > 0;

    return new StreamWriter(outputPath, append: appendOutput, new UTF8Encoding(false));
}

static Dictionary<string, object?> ProjectView(InspectRecordView view, IReadOnlyList<InspectField> fields)
{
    var projected = new Dictionary<string, object?>(fields.Count, StringComparer.OrdinalIgnoreCase);
    foreach (var field in fields)
        projected[GetFieldName(field)] = GetFieldValue(view, field);

    return projected;
}

static void WriteCsv(InspectRecordView[] views, IReadOnlyList<InspectField> fields, TextWriter writer, bool skipHeader)
{
    var selectedFields = fields.Count == 0
        ? InspectFieldDefinitions.All
        : fields;

    if (!skipHeader)
        writer.WriteLine(string.Join(',', selectedFields.Select(GetFieldName)));

    foreach (var view in views)
    {
        var values = selectedFields
            .Select(field => EscapeCsv(GetFieldValue(view, field)))
            .ToArray();
        writer.WriteLine(string.Join(',', values));
    }
}

static string EscapeCsv(object? value)
{
    var text = value switch
    {
        null => string.Empty,
        DateTimeOffset timestamp => timestamp.ToString("O"),
        _ => value.ToString() ?? string.Empty,
    };

    if (text.IndexOfAny([',', '"', '\r', '\n']) >= 0)
        return $"\"{text.Replace("\"", "\"\"")}\"";

    return text;
}

static void WriteCountsOnly(OutputFormat format, CountsOnlySummary summary, TextWriter writer)
{
    switch (format)
    {
        case OutputFormat.Json:
            writer.WriteLine(JsonSerializer.Serialize(summary, new JsonSerializerOptions { WriteIndented = true }));
            break;
        case OutputFormat.Ndjson:
            writer.WriteLine(JsonSerializer.Serialize(summary));
            break;
        default:
            writer.WriteLine($"matchedCount={summary.Meta.MatchedCount} returnedCount={summary.Meta.ReturnedCount} hasMore={summary.Meta.HasMore.ToString().ToLowerInvariant()} offset={summary.Meta.Offset} limit={summary.Meta.Limit}");
            writer.WriteLine($"matchedLevels trace={summary.MatchedLevels.Trace} debug={summary.MatchedLevels.Debug} info={summary.MatchedLevels.Info} warn={summary.MatchedLevels.Warn} error={summary.MatchedLevels.Error} fatal={summary.MatchedLevels.Fatal}");
            writer.WriteLine($"returnedLevels trace={summary.ReturnedLevels.Trace} debug={summary.ReturnedLevels.Debug} info={summary.ReturnedLevels.Info} warn={summary.ReturnedLevels.Warn} error={summary.ReturnedLevels.Error} fatal={summary.ReturnedLevels.Fatal}");
            break;
    }
}

static LevelCounters ToLevelCounters(int[] counts)
    => new(counts[0], counts[1], counts[2], counts[3], counts[4], counts[5]);

static string GetFieldName(InspectField field)
    => field switch
    {
        InspectField.Timestamp => "timestamp",
        InspectField.Level => "level",
        InspectField.Source => "source",
        InspectField.Service => "service",
        InspectField.Message => "message",
        InspectField.TraceId => "traceId",
        InspectField.CorrelationId => "correlationId",
        InspectField.PayloadJson => "payloadJson",
        _ => throw new ArgumentOutOfRangeException(nameof(field), field, null),
    };

static object? GetFieldValue(InspectRecordView view, InspectField field)
    => field switch
    {
        InspectField.Timestamp => view.Timestamp,
        InspectField.Level => view.Level,
        InspectField.Source => view.Source,
        InspectField.Service => view.Service,
        InspectField.Message => view.Message,
        InspectField.TraceId => view.TraceId,
        InspectField.CorrelationId => view.CorrelationId,
        InspectField.PayloadJson => view.PayloadJson,
        _ => throw new ArgumentOutOfRangeException(nameof(field), field, null),
    };

static void WriteTable(InspectRecordView[] views, TextWriter writer)
{
    if (views.Length == 0)
    {
        writer.WriteLine("No records found for the selected window.");
        return;
    }

    foreach (var view in views)
    {
        writer.WriteLine($"[{view.Timestamp:O}] {view.Level,-5} source={view.Source ?? "<null>"} service={view.Service ?? "<null>"}");
        writer.WriteLine($"  message={view.Message ?? "<empty>"}");

        if (!string.IsNullOrWhiteSpace(view.TraceId) || !string.IsNullOrWhiteSpace(view.CorrelationId))
            writer.WriteLine($"  traceId={view.TraceId ?? "<null>"} correlationId={view.CorrelationId ?? "<null>"}");
    }
}

internal sealed record InspectRecordView(
    DateTimeOffset Timestamp,
    string Level,
    string? Source,
    string? Service,
    string? Message,
    string? TraceId,
    string? CorrelationId,
    string? PayloadJson);

internal sealed record PageMetadata(
    int MatchedCount,
    int ReturnedCount,
    int Offset,
    int Limit,
    bool HasMore,
    string Sort,
    DateTimeOffset From,
    DateTimeOffset To,
    DateTimeOffset? AfterTimestamp,
    DateTimeOffset? BeforeTimestamp);

internal sealed record LevelCounters(int Trace, int Debug, int Info, int Warn, int Error, int Fatal);

internal sealed record CountsOnlySummary(PageMetadata Meta, LevelCounters MatchedLevels, LevelCounters ReturnedLevels);

internal sealed record PayloadView(string? Source, string? Service, string? Message);

internal sealed record ManifestTotalsContext(
    PageMetadata Window,
    string Level,
    string? Source,
    string? Service,
    string? TraceId,
    string? CorrelationId,
    string? LevelIn,
    string? Contains,
    ContainsField ContainsField,
    ContainsMode ContainsMode,
    string CommandLine,
    LevelCounters MatchedLevels,
    LevelCounters ReturnedLevels);

internal sealed record SplitOutputManifest(string Format, string OutputFile, int? SplitOutputSizeBytes, IReadOnlyList<SplitOutputPart> Parts, IReadOnlyDictionary<string, string>? OutputHashesSha256);

internal sealed record SplitOutputPart(string FileName, int RecordCount, long SizeBytes, string? OutputHashSha256);

internal sealed record SummaryGroupEntry(string Key, int Count, int SortOrder = 0);

internal enum SummaryGroupOrder
{
    Count,
    Key,
}

internal enum ManifestOutputFormat
{
    Json,
    Ndjson,
}

internal enum SummaryOutputFormat
{
    Json,
    Table,
}

internal enum SummaryRecordsFormat
{
    Json,
    Table,
    Ndjson,
    Csv,
}

internal enum SummaryIncludeRecords
{
    First,
    Last,
}

internal enum SummaryGroupByField
{
    Level,
    Source,
    Service,
    TraceId,
    CorrelationId,
}

internal enum TemplateNumberStyle
{
    Padded,
    Plain,
}

internal enum OutputFormat
{
    Table,
    Json,
    Ndjson,
    Csv,
}

internal enum InspectField
{
    Timestamp,
    Level,
    Source,
    Service,
    Message,
    TraceId,
    CorrelationId,
    PayloadJson,
}

internal static class InspectFieldDefinitions
{
    public static readonly InspectField[] All =
    [
        InspectField.Timestamp,
        InspectField.Level,
        InspectField.Source,
        InspectField.Service,
        InspectField.Message,
        InspectField.TraceId,
        InspectField.CorrelationId,
        InspectField.PayloadJson,
    ];
}

internal enum SortDirection
{
    Asc,
    Desc,
}

internal sealed record InspectCliOptions(
    string? LogDirectory,
    string? BaseDirectory,
    string? PluginId,
    DateTimeOffset From,
    DateTimeOffset To,
    int Limit,
    int Offset,
    HlogLevel? Level,
    string? Source,
    string? Service,
    string? TraceId,
    string? CorrelationId,
    string? LevelIn,
    string? Contains,
    ContainsField ContainsField,
    ContainsMode ContainsMode,
    DateTimeOffset? AfterTimestamp,
    DateTimeOffset? BeforeTimestamp,
    bool PageMeta,
    bool CountsOnly,
    IReadOnlyList<InspectField> Fields,
    string? OutputPath,
    bool AppendOutput,
    int? SplitOutputSizeBytes,
    string? SplitOutputTemplate,
    int SplitOutputStartIndex,
    TemplateNumberStyle TemplateNumberStyle,
    string? ManifestOutputPath,
    ManifestOutputFormat ManifestOutputFormat,
    bool ManifestIncludeTotals,
    bool ManifestIncludeWindow,
    bool ManifestIncludeLevelCounts,
    bool ManifestIncludeMatchCounts,
    bool ManifestIncludeOutputHash,
    bool ManifestIncludeFilters,
    bool ManifestIncludeCommand,
    string? SummaryOutputPath,
    SummaryOutputFormat SummaryOutputFormat,
    SummaryGroupByField? SummaryGroupBy,
    int? SummaryGroupLimit,
    SummaryGroupOrder? SummaryGroupOrder,
    SummaryIncludeRecords? SummaryIncludeRecords,
    int SummaryRecordCount,
    IReadOnlyList<InspectField> SummaryRecordFields,
    SummaryRecordsFormat? SummaryRecordsFormat,
    string? SummaryRecordsOutputPath,
    string? SummaryRecordsOutputTemplate,
    SortDirection Sort,
    OutputFormat Format)
{
    public static InspectCliOptions Parse(string[] args)
    {
        string? logDirectory = null;
        string? baseDirectory = null;
        string? pluginId = null;
        var minutes = 60;
        DateTimeOffset? from = null;
        DateTimeOffset? to = null;
        var limit = 50;
        var offset = 0;
        HlogLevel? level = null;
        string? source = null;
        string? service = null;
        string? traceId = null;
        string? correlationId = null;
        string? levelIn = null;
        string? contains = null;
        var containsField = ContainsField.Any;
        var containsMode = ContainsMode.Any;
        DateTimeOffset? afterTimestamp = null;
        DateTimeOffset? beforeTimestamp = null;
        var pageMeta = false;
        var countsOnly = false;
        IReadOnlyList<InspectField> fields = [];
        string? outputPath = null;
        var appendOutput = false;
        int? splitOutputSizeBytes = null;
        string? splitOutputTemplate = null;
        var splitOutputStartIndex = 1;
        var templateNumberStyle = TemplateNumberStyle.Padded;
        string? manifestOutputPath = null;
        var manifestOutputFormat = ManifestOutputFormat.Json;
        var manifestIncludeTotals = false;
        var manifestIncludeWindow = false;
        var manifestIncludeLevelCounts = false;
        var manifestIncludeMatchCounts = false;
        var manifestIncludeOutputHash = false;
        var manifestIncludeFilters = false;
        var manifestIncludeCommand = false;
        string? summaryOutputPath = null;
        var summaryOutputFormat = SummaryOutputFormat.Json;
        SummaryGroupByField? summaryGroupBy = null;
        int? summaryGroupLimit = null;
        SummaryGroupOrder? summaryGroupOrder = null;
        SummaryIncludeRecords? summaryIncludeRecords = null;
        var summaryRecordCount = 1;
        IReadOnlyList<InspectField> summaryRecordFields = [];
        SummaryRecordsFormat? summaryRecordsFormat = null;
        string? summaryRecordsOutputPath = null;
        string? summaryRecordsOutputTemplate = null;
        var sort = SortDirection.Asc;
        var format = OutputFormat.Table;

        for (var index = 0; index < args.Length; index++)
        {
            var arg = args[index];
            switch (arg)
            {
                case "--log-dir":
                    logDirectory = ReadValue(args, ref index, arg);
                    break;
                case "--base-dir":
                    baseDirectory = ReadValue(args, ref index, arg);
                    break;
                case "--plugin-id":
                    pluginId = ReadValue(args, ref index, arg);
                    break;
                case "--minutes":
                    minutes = ParsePositiveInt(ReadValue(args, ref index, arg), arg);
                    break;
                case "--from":
                    from = ParseTimestamp(ReadValue(args, ref index, arg), arg);
                    break;
                case "--to":
                    to = ParseTimestamp(ReadValue(args, ref index, arg), arg);
                    break;
                case "--limit":
                    limit = ParsePositiveInt(ReadValue(args, ref index, arg), arg);
                    break;
                case "--offset":
                    offset = ParseNonNegativeInt(ReadValue(args, ref index, arg), arg);
                    break;
                case "--level":
                    level = ParseLevel(ReadValue(args, ref index, arg), arg);
                    break;
                case "--source":
                    source = ReadValue(args, ref index, arg);
                    break;
                case "--service":
                    service = ReadValue(args, ref index, arg);
                    break;
                case "--traceId":
                case "--trace-id":
                    traceId = ReadValue(args, ref index, arg);
                    break;
                case "--correlationId":
                case "--correlation-id":
                    correlationId = ReadValue(args, ref index, arg);
                    break;
                case "--level-in":
                    levelIn = ReadValue(args, ref index, arg);
                    break;
                case "--contains":
                    contains = ReadValue(args, ref index, arg);
                    break;
                case "--contains-field":
                    containsField = ParseContainsField(ReadValue(args, ref index, arg), arg);
                    break;
                case "--contains-mode":
                    containsMode = ParseContainsMode(ReadValue(args, ref index, arg), arg);
                    break;
                case "--after-timestamp":
                    afterTimestamp = NormalizeToMilliseconds(ParseTimestamp(ReadValue(args, ref index, arg), arg));
                    break;
                case "--before-timestamp":
                    beforeTimestamp = NormalizeToMilliseconds(ParseTimestamp(ReadValue(args, ref index, arg), arg));
                    break;
                case "--page-meta":
                    pageMeta = true;
                    break;
                case "--counts-only":
                    countsOnly = true;
                    break;
                case "--fields":
                    fields = ParseFields(ReadValue(args, ref index, arg), arg);
                    break;
                case "--output":
                    outputPath = ReadValue(args, ref index, arg);
                    break;
                case "--append-output":
                    appendOutput = true;
                    break;
                case "--split-output-size":
                    splitOutputSizeBytes = ParsePositiveInt(ReadValue(args, ref index, arg), arg);
                    break;
                case "--split-output-template":
                    splitOutputTemplate = ReadValue(args, ref index, arg);
                    break;
                case "--split-output-start-index":
                    splitOutputStartIndex = ParsePositiveInt(ReadValue(args, ref index, arg), arg);
                    break;
                case "--template-number-style":
                    templateNumberStyle = ParseTemplateNumberStyle(ReadValue(args, ref index, arg), arg);
                    break;
                case "--manifest-output":
                    manifestOutputPath = ReadValue(args, ref index, arg);
                    break;
                case "--manifest-format":
                    manifestOutputFormat = ParseManifestFormat(ReadValue(args, ref index, arg), arg);
                    break;
                case "--manifest-include-totals":
                    manifestIncludeTotals = true;
                    break;
                case "--manifest-include-window":
                    manifestIncludeWindow = true;
                    break;
                case "--manifest-include-level-counts":
                    manifestIncludeLevelCounts = true;
                    break;
                case "--manifest-include-match-counts":
                    manifestIncludeMatchCounts = true;
                    break;
                case "--manifest-include-output-hash":
                    manifestIncludeOutputHash = true;
                    break;
                case "--manifest-include-filters":
                    manifestIncludeFilters = true;
                    break;
                case "--manifest-include-command":
                    manifestIncludeCommand = true;
                    break;
                case "--summary-output":
                    summaryOutputPath = ReadValue(args, ref index, arg);
                    break;
                case "--summary-format":
                    summaryOutputFormat = ParseSummaryFormat(ReadValue(args, ref index, arg), arg);
                    break;
                case "--summary-group-by":
                    summaryGroupBy = ParseSummaryGroupBy(ReadValue(args, ref index, arg), arg);
                    break;
                case "--summary-group-limit":
                    summaryGroupLimit = ParsePositiveInt(ReadValue(args, ref index, arg), arg);
                    break;
                case "--summary-group-order":
                    summaryGroupOrder = ParseSummaryGroupOrder(ReadValue(args, ref index, arg), arg);
                    break;
                case "--summary-include-records":
                    summaryIncludeRecords = ParseSummaryIncludeRecords(ReadValue(args, ref index, arg), arg);
                    break;
                case "--summary-record-count":
                    summaryRecordCount = ParsePositiveInt(ReadValue(args, ref index, arg), arg);
                    break;
                case "--summary-record-fields":
                    summaryRecordFields = ParseFields(ReadValue(args, ref index, arg), arg);
                    break;
                case "--summary-records-format":
                    summaryRecordsFormat = ParseSummaryRecordsFormat(ReadValue(args, ref index, arg), arg);
                    break;
                case "--summary-records-output":
                    summaryRecordsOutputPath = ReadValue(args, ref index, arg);
                    break;
                case "--summary-records-template":
                    summaryRecordsOutputTemplate = ReadValue(args, ref index, arg);
                    break;
                case "--sort":
                    sort = ParseSort(ReadValue(args, ref index, arg), arg);
                    break;
                case "--format":
                    format = ParseFormat(ReadValue(args, ref index, arg), arg);
                    break;
                case "--help":
                case "-h":
                    PrintUsage();
                    Environment.Exit(0);
                    break;
                default:
                    throw new ArgumentException($"Unknown option: {arg}. Use '--help' to see supported options.");
            }
        }

        var resolvedTo = to ?? DateTimeOffset.UtcNow;
        var resolvedFrom = from ?? resolvedTo.AddMinutes(-minutes);
        traceId = ParseIdentifierFilter(traceId, "--traceId", allowGuid: false);
        correlationId = ParseIdentifierFilter(correlationId, "--correlationId", allowGuid: true);
        var levelTokens = string.IsNullOrWhiteSpace(levelIn)
            ? []
            : levelIn
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Where(token => !string.IsNullOrWhiteSpace(token))
                .ToArray();
        var containsTerms = string.IsNullOrWhiteSpace(contains)
            ? []
            : contains
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Where(term => !string.IsNullOrWhiteSpace(term))
                .ToArray();
        if (resolvedFrom > resolvedTo)
            throw new ArgumentException("'--from' must be earlier than or equal to '--to'.");
        if (afterTimestamp.HasValue && beforeTimestamp.HasValue && afterTimestamp > beforeTimestamp)
            throw new ArgumentException("'--after-timestamp' must be earlier than or equal to '--before-timestamp'.");
        if (!string.IsNullOrWhiteSpace(levelIn) && levelTokens.Length == 0)
            throw new ArgumentException("'--level-in' requires at least one non-empty level token.");
        foreach (var levelToken in levelTokens)
            ParseLevel(levelToken, "--level-in");
        if (!string.IsNullOrWhiteSpace(contains) && containsTerms.Length == 0)
            throw new ArgumentException("'--contains' requires at least one non-empty token.");
        if (string.IsNullOrWhiteSpace(contains) && containsMode != ContainsMode.Any)
            throw new ArgumentException("'--contains-mode' requires '--contains'.");
        if (pageMeta && format != OutputFormat.Json)
            throw new ArgumentException("'--page-meta' is supported only with '--format json'.");
        if (pageMeta && countsOnly)
            throw new ArgumentException("'--counts-only' cannot be combined with '--page-meta'.");
        if (countsOnly && fields.Count > 0)
            throw new ArgumentException("'--fields' cannot be combined with '--counts-only'.");
        if (format == OutputFormat.Table && fields.Count > 0)
            throw new ArgumentException("'--fields' is supported only with '--format json', '--format ndjson' or '--format csv'.");
        if (appendOutput && string.IsNullOrWhiteSpace(outputPath))
            throw new ArgumentException("'--append-output' requires '--output <path>'.");
        if (splitOutputSizeBytes.HasValue && string.IsNullOrWhiteSpace(outputPath))
            throw new ArgumentException("'--split-output-size' requires '--output <path>'.");
        if (splitOutputSizeBytes.HasValue && appendOutput)
            throw new ArgumentException("'--split-output-size' cannot be combined with '--append-output'.");
        if (splitOutputSizeBytes.HasValue && (pageMeta || countsOnly))
            throw new ArgumentException("'--split-output-size' cannot be combined with '--page-meta' or '--counts-only'.");
        if (!splitOutputSizeBytes.HasValue && !string.IsNullOrWhiteSpace(splitOutputTemplate))
            throw new ArgumentException("'--split-output-template' requires '--split-output-size'.");
        if (!splitOutputSizeBytes.HasValue && splitOutputStartIndex != 1)
            throw new ArgumentException("'--split-output-start-index' requires '--split-output-size'.");
        if (!string.IsNullOrWhiteSpace(splitOutputTemplate))
            ValidateTemplateTokens(splitOutputTemplate, new HashSet<string>(StringComparer.Ordinal) { "{name}", "{part}", "{ext}" }, "'--split-output-template'");
        if (!string.IsNullOrWhiteSpace(manifestOutputPath) && string.IsNullOrWhiteSpace(outputPath))
            throw new ArgumentException("'--manifest-output' requires '--output <path>'.");
        if (!string.IsNullOrWhiteSpace(manifestOutputPath) && appendOutput)
            throw new ArgumentException("'--manifest-output' cannot be combined with '--append-output'.");
        if (string.IsNullOrWhiteSpace(manifestOutputPath) && manifestOutputFormat != ManifestOutputFormat.Json)
            throw new ArgumentException("'--manifest-format' requires '--manifest-output'.");
        if (manifestIncludeTotals && string.IsNullOrWhiteSpace(manifestOutputPath))
            throw new ArgumentException("'--manifest-include-totals' requires '--manifest-output'.");
        if (manifestIncludeTotals && manifestOutputFormat != ManifestOutputFormat.Ndjson)
            throw new ArgumentException("'--manifest-include-totals' is supported only with '--manifest-format ndjson'.");
        if (manifestIncludeWindow && !manifestIncludeTotals)
            throw new ArgumentException("'--manifest-include-window' requires '--manifest-include-totals'.");
        if (manifestIncludeLevelCounts && !manifestIncludeTotals)
            throw new ArgumentException("'--manifest-include-level-counts' requires '--manifest-include-totals'.");
        if (manifestIncludeMatchCounts && !manifestIncludeTotals)
            throw new ArgumentException("'--manifest-include-match-counts' requires '--manifest-include-totals'.");
        if (manifestIncludeOutputHash && string.IsNullOrWhiteSpace(manifestOutputPath))
            throw new ArgumentException("'--manifest-include-output-hash' requires '--manifest-output'.");
        if (manifestIncludeFilters && string.IsNullOrWhiteSpace(manifestOutputPath))
            throw new ArgumentException("'--manifest-include-filters' requires '--manifest-output'.");
        if (manifestIncludeCommand && string.IsNullOrWhiteSpace(manifestOutputPath))
            throw new ArgumentException("'--manifest-include-command' requires '--manifest-output'.");
        if (manifestOutputFormat == ManifestOutputFormat.Ndjson && manifestIncludeFilters && !manifestIncludeTotals)
            throw new ArgumentException("'--manifest-include-filters' requires '--manifest-include-totals' when '--manifest-format ndjson' is used.");
        if (manifestOutputFormat == ManifestOutputFormat.Ndjson && manifestIncludeCommand && !manifestIncludeTotals)
            throw new ArgumentException("'--manifest-include-command' requires '--manifest-include-totals' when '--manifest-format ndjson' is used.");
        if (string.IsNullOrWhiteSpace(summaryOutputPath) && summaryOutputFormat != SummaryOutputFormat.Json)
            throw new ArgumentException("'--summary-format' requires '--summary-output'.");
        if (string.IsNullOrWhiteSpace(summaryOutputPath) && summaryGroupBy.HasValue)
            throw new ArgumentException("'--summary-group-by' requires '--summary-output'.");
        if (string.IsNullOrWhiteSpace(summaryOutputPath) && summaryGroupLimit.HasValue)
            throw new ArgumentException("'--summary-group-limit' requires '--summary-output'.");
        if (string.IsNullOrWhiteSpace(summaryOutputPath) && summaryGroupOrder.HasValue)
            throw new ArgumentException("'--summary-group-order' requires '--summary-output'.");
        if (!summaryGroupBy.HasValue && summaryGroupLimit.HasValue)
            throw new ArgumentException("'--summary-group-limit' requires '--summary-group-by'.");
        if (!summaryGroupBy.HasValue && summaryGroupOrder.HasValue)
            throw new ArgumentException("'--summary-group-order' requires '--summary-group-by'.");
        if (string.IsNullOrWhiteSpace(summaryOutputPath) && summaryIncludeRecords.HasValue)
            throw new ArgumentException("'--summary-include-records' requires '--summary-output'.");
        if (string.IsNullOrWhiteSpace(summaryOutputPath) && summaryRecordCount != 1)
            throw new ArgumentException("'--summary-record-count' requires '--summary-output'.");
        if (string.IsNullOrWhiteSpace(summaryOutputPath) && summaryRecordFields.Count > 0)
            throw new ArgumentException("'--summary-record-fields' requires '--summary-output'.");
        if (string.IsNullOrWhiteSpace(summaryOutputPath) && summaryRecordsFormat.HasValue)
            throw new ArgumentException("'--summary-records-format' requires '--summary-output'.");
        if (string.IsNullOrWhiteSpace(summaryOutputPath) && !string.IsNullOrWhiteSpace(summaryRecordsOutputPath))
            throw new ArgumentException("'--summary-records-output' requires '--summary-output'.");
        if (string.IsNullOrWhiteSpace(summaryOutputPath) && !string.IsNullOrWhiteSpace(summaryRecordsOutputTemplate))
            throw new ArgumentException("'--summary-records-template' requires '--summary-output'.");
        if (!summaryIncludeRecords.HasValue && summaryRecordCount != 1)
            throw new ArgumentException("'--summary-record-count' requires '--summary-include-records'.");
        if (!summaryIncludeRecords.HasValue && summaryRecordFields.Count > 0)
            throw new ArgumentException("'--summary-record-fields' requires '--summary-include-records'.");
        if (!summaryIncludeRecords.HasValue && summaryRecordsFormat.HasValue)
            throw new ArgumentException("'--summary-records-format' requires '--summary-include-records'.");
        if (!summaryIncludeRecords.HasValue && !string.IsNullOrWhiteSpace(summaryRecordsOutputPath))
            throw new ArgumentException("'--summary-records-output' requires '--summary-include-records'.");
        if (!summaryIncludeRecords.HasValue && !string.IsNullOrWhiteSpace(summaryRecordsOutputTemplate))
            throw new ArgumentException("'--summary-records-template' requires '--summary-include-records'.");
        if (!string.IsNullOrWhiteSpace(summaryRecordsOutputPath) && !string.IsNullOrWhiteSpace(summaryRecordsOutputTemplate))
            throw new ArgumentException("'--summary-records-template' cannot be combined with '--summary-records-output'.");
        if (!string.IsNullOrWhiteSpace(summaryRecordsOutputTemplate))
            ValidateTemplateTokens(summaryRecordsOutputTemplate, new HashSet<string>(StringComparer.Ordinal) { "{name}", "{mode}", "{format}", "{count}", "{ext}" }, "'--summary-records-template'");

        return new InspectCliOptions(logDirectory, baseDirectory, pluginId, resolvedFrom, resolvedTo, limit, offset, level, source, service, traceId, correlationId, levelIn, contains, containsField, containsMode, afterTimestamp, beforeTimestamp, pageMeta, countsOnly, fields, outputPath, appendOutput, splitOutputSizeBytes, splitOutputTemplate, splitOutputStartIndex, templateNumberStyle, manifestOutputPath, manifestOutputFormat, manifestIncludeTotals, manifestIncludeWindow, manifestIncludeLevelCounts, manifestIncludeMatchCounts, manifestIncludeOutputHash, manifestIncludeFilters, manifestIncludeCommand, summaryOutputPath, summaryOutputFormat, summaryGroupBy, summaryGroupLimit, summaryGroupOrder, summaryIncludeRecords, summaryRecordCount, summaryRecordFields, summaryRecordsFormat, summaryRecordsOutputPath, summaryRecordsOutputTemplate, sort, format);
    }

    public string ResolveLogDirectory()
    {
        if (!string.IsNullOrWhiteSpace(LogDirectory))
            return LogDirectory;

        var baseDir = BaseDirectory
            ?? Environment.GetEnvironmentVariable("HOBIARY_DIRECT_LOG_DIR")
            ?? Path.Combine("samples", "BrokerlessDemo", "demo-logs");
        var resolvedPluginId = PluginId
            ?? Environment.GetEnvironmentVariable("HOBIARY_PLUGIN_ID");

        if (string.IsNullOrWhiteSpace(resolvedPluginId))
            throw new ArgumentException("Provide '--log-dir' or '--plugin-id' together with '--base-dir' (or HOBIARY_PLUGIN_ID/HOBIARY_DIRECT_LOG_DIR). Use '--help' to see supported options.");

        return Path.Combine(baseDir, resolvedPluginId);
    }

    private static void ValidateTemplateTokens(string template, IReadOnlySet<string> allowedTokens, string option)
    {
        foreach (Match match in Regex.Matches(template, "\\{[^{}]+\\}"))
        {
            if (!allowedTokens.Contains(match.Value))
                throw new ArgumentException($"Invalid token '{match.Value}' for {option}. Allowed tokens: {string.Join(", ", allowedTokens)}.");
        }
    }

    private static string ReadValue(string[] args, ref int index, string option)
    {
        if (index + 1 >= args.Length)
            throw new ArgumentException($"Missing value for {option}. Use '--help' to see supported options.");

        index++;
        return args[index];
    }

    private static int ParsePositiveInt(string value, string option)
    {
        if (!int.TryParse(value, out var parsed) || parsed <= 0)
            throw new ArgumentException($"Invalid positive integer for {option}: '{value}'.");
        return parsed;
    }

    private static int ParseNonNegativeInt(string value, string option)
    {
        if (!int.TryParse(value, out var parsed) || parsed < 0)
            throw new ArgumentException($"Invalid non-negative integer for {option}: '{value}'.");
        return parsed;
    }

    private static DateTimeOffset ParseTimestamp(string value, string option)
    {
        if (!DateTimeOffset.TryParse(value, out var parsed))
            throw new ArgumentException($"Invalid timestamp for {option}: '{value}'. Use ISO-8601, for example 2026-09-24T12:30:00Z.");
        return parsed;
    }

    private static DateTimeOffset NormalizeToMilliseconds(DateTimeOffset value)
        => DateTimeOffset.FromUnixTimeMilliseconds(value.ToUnixTimeMilliseconds());

    private static HlogLevel ParseLevel(string value, string option)
    {
        if (!Enum.TryParse<HlogLevel>(value, ignoreCase: true, out var level))
            throw new ArgumentException($"Invalid level for {option}: '{value}'. Use Trace, Debug, Info, Warn, Error or Fatal. Use '--help' to see supported options.");
        return level;
    }

    private static string? ParseIdentifierFilter(string? value, string option, bool allowGuid)
    {
        if (value is null)
            return value;

        var tokens = value
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(token => !string.IsNullOrWhiteSpace(token))
            .Select(token => NormalizeIdentifierToken(token, option, allowGuid))
            .ToArray();

        if (tokens.Length == 0)
            throw new ArgumentException($"'{option}' requires at least one non-empty identifier token.");

        return string.Join(',', tokens);
    }

    private static string NormalizeIdentifierToken(string token, string option, bool allowGuid)
    {
        if (Regex.IsMatch(token, "^[0-9a-fA-F]{32}$", RegexOptions.CultureInvariant))
            return token.ToLowerInvariant();

        if (allowGuid && Guid.TryParse(token, out var guid))
            return guid.ToString("N");

        throw new ArgumentException(allowGuid
                ? $"Invalid identifier for {option}: '{token}'. Use 32 hexadecimal characters or a GUID. Use '--help' to see supported options."
                : $"Invalid identifier for {option}: '{token}'. Use 32 hexadecimal characters. Use '--help' to see supported options.");
    }

    private static ContainsField ParseContainsField(string value, string option)
    {
        if (!Enum.TryParse<ContainsField>(value, ignoreCase: true, out var containsField))
            throw new ArgumentException($"Invalid contains field for {option}: '{value}'. Use Any, Message or Payload. Use '--help' to see supported options.");
        return containsField;
    }

    private static ContainsMode ParseContainsMode(string value, string option)
    {
        if (!Enum.TryParse<ContainsMode>(value, ignoreCase: true, out var containsMode))
            throw new ArgumentException($"Invalid contains mode for {option}: '{value}'. Use Any or All. Use '--help' to see supported options.");
        return containsMode;
    }

    private static OutputFormat ParseFormat(string value, string option)
    {
        if (!Enum.TryParse<OutputFormat>(value, ignoreCase: true, out var format))
            throw new ArgumentException($"Invalid format for {option}: '{value}'. Use Table, Json, Ndjson or Csv. Use '--help' to see supported options.");
        return format;
    }

    private static IReadOnlyList<InspectField> ParseFields(string value, string option)
    {
        var tokens = value
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        if (tokens.Length == 0)
            throw new ArgumentException($"Missing field list for {option}.");

        var parsedFields = new List<InspectField>(tokens.Length);
        foreach (var token in tokens)
        {
            switch (token.ToLowerInvariant())
            {
                case "core":
                    AddFields(parsedFields, InspectFieldPresets.Core);
                    break;
                case "tracing":
                    AddFields(parsedFields, InspectFieldPresets.Tracing);
                    break;
                case "ops":
                    AddFields(parsedFields, InspectFieldPresets.Ops);
                    break;
                case "errors":
                    AddFields(parsedFields, InspectFieldPresets.Errors);
                    break;
                case "security":
                    AddFields(parsedFields, InspectFieldPresets.Security);
                    break;
                case "audit":
                    AddFields(parsedFields, InspectFieldPresets.Audit);
                    break;
                case "forensics":
                    AddFields(parsedFields, InspectFieldPresets.Forensics);
                    break;
                case "triage":
                    AddFields(parsedFields, InspectFieldPresets.Triage);
                    break;
                case "timestamp":
                    AddField(parsedFields, InspectField.Timestamp);
                    break;
                case "level":
                    AddField(parsedFields, InspectField.Level);
                    break;
                case "source":
                    AddField(parsedFields, InspectField.Source);
                    break;
                case "service":
                    AddField(parsedFields, InspectField.Service);
                    break;
                case "message":
                    AddField(parsedFields, InspectField.Message);
                    break;
                case "traceid":
                case "trace-id":
                    AddField(parsedFields, InspectField.TraceId);
                    break;
                case "correlationid":
                case "correlation-id":
                    AddField(parsedFields, InspectField.CorrelationId);
                    break;
                case "payloadjson":
                case "payload-json":
                    AddField(parsedFields, InspectField.PayloadJson);
                    break;
                default:
                    throw new ArgumentException($"Invalid field for {option}: '{token}'. Use core, tracing, ops, errors, security, audit, forensics, triage, timestamp, level, source, service, message, traceId/trace-id, correlationId/correlation-id or payloadJson/payload-json.");
            }
        }

        return parsedFields;
    }

    private static void AddFields(List<InspectField> parsedFields, IReadOnlyList<InspectField> fields)
    {
        foreach (var field in fields)
            AddField(parsedFields, field);
    }

    private static void AddField(List<InspectField> parsedFields, InspectField field)
    {
        if (!parsedFields.Contains(field))
            parsedFields.Add(field);
    }

    private static SortDirection ParseSort(string value, string option)
    {
        if (!Enum.TryParse<SortDirection>(value, ignoreCase: true, out var sort))
            throw new ArgumentException($"Invalid sort for {option}: '{value}'. Use Asc or Desc. Use '--help' to see supported options.");
        return sort;
    }

    private static SummaryOutputFormat ParseSummaryFormat(string value, string option)
    {
        if (!Enum.TryParse<SummaryOutputFormat>(value, ignoreCase: true, out var format))
            throw new ArgumentException($"Invalid summary format for {option}: '{value}'. Use Json or Table. Use '--help' to see supported options.");
        return format;
    }

    private static SummaryGroupByField ParseSummaryGroupBy(string value, string option)
    {
        value = value switch
        {
            "trace-id" => "TraceId",
            "correlation-id" => "CorrelationId",
            _ => value,
        };

        if (!Enum.TryParse<SummaryGroupByField>(value, ignoreCase: true, out var groupBy))
            throw new ArgumentException($"Invalid summary group field for {option}: '{value}'. Use Level, Source, Service, TraceId or CorrelationId (aliases: trace-id, correlation-id). Use '--help' to see supported options.");
        return groupBy;
    }

    private static SummaryGroupOrder ParseSummaryGroupOrder(string value, string option)
    {
        if (!Enum.TryParse<SummaryGroupOrder>(value, ignoreCase: true, out var summaryGroupOrder))
            throw new ArgumentException($"Invalid summary group order for {option}: '{value}'. Use Count or Key. Use '--help' to see supported options.");
        return summaryGroupOrder;
    }

    private static ManifestOutputFormat ParseManifestFormat(string value, string option)
    {
        if (!Enum.TryParse<ManifestOutputFormat>(value, ignoreCase: true, out var format))
            throw new ArgumentException($"Invalid manifest format for {option}: '{value}'. Use Json or Ndjson. Use '--help' to see supported options.");
        return format;
    }

    private static SummaryIncludeRecords ParseSummaryIncludeRecords(string value, string option)
    {
        if (!Enum.TryParse<SummaryIncludeRecords>(value, ignoreCase: true, out var includeRecords))
            throw new ArgumentException($"Invalid summary include mode for {option}: '{value}'. Use First or Last. Use '--help' to see supported options.");
        return includeRecords;
    }

    private static SummaryRecordsFormat ParseSummaryRecordsFormat(string value, string option)
    {
        if (!Enum.TryParse<SummaryRecordsFormat>(value, ignoreCase: true, out var format))
            throw new ArgumentException($"Invalid summary records format for {option}: '{value}'. Use Json, Table, Ndjson or Csv. Use '--help' to see supported options.");
        return format;
    }

    private static TemplateNumberStyle ParseTemplateNumberStyle(string value, string option)
    {
        if (!Enum.TryParse<TemplateNumberStyle>(value, ignoreCase: true, out var templateNumberStyle))
            throw new ArgumentException($"Invalid template number style for {option}: '{value}'. Use Padded or Plain. Use '--help' to see supported options.");
        return templateNumberStyle;
    }

    private static void PrintUsage()
    {
        Console.WriteLine("HlogInspect - local read-only viewer for HobiaryLogDB directories");
        Console.WriteLine("Usage:");
        Console.WriteLine("  dotnet run --project .\\samples\\HlogInspect\\HlogInspect.csproj -- --log-dir <path> [--minutes 60] [--limit 50] [--offset 0] [--level Warn] [--level-in Info,Warn,Error] [--source OrdersService,BillingService] [--service api-gw,worker] [--traceId 0123456789abcdef0123456789abcdef,fedcba9876543210fedcba9876543210] [--correlationId aabbccddeeff00112233445566778899,99887766554433221100ffeeddccbbaa] [--contains timeout|timeout,orders] [--contains-field any|message|payload] [--contains-mode any|all] [--after-timestamp 2026-09-24T12:30:00Z] [--before-timestamp 2026-09-24T12:45:00Z] [--page-meta] [--counts-only] [--fields core|tracing|ops|errors|security|audit|forensics|triage|timestamp,level,message] [--output .\\exports\\logs.json] [--append-output] [--split-output-size 1024] [--split-output-template chunks\\{name}-chunk-{part}{ext}] [--split-output-start-index 10] [--template-number-style padded|plain] [--manifest-output .\\exports\\logs.manifest.ndjson] [--manifest-format json|ndjson] [--manifest-include-totals] [--manifest-include-window] [--manifest-include-level-counts] [--manifest-include-match-counts] [--manifest-include-output-hash] [--manifest-include-filters] [--manifest-include-command] [--summary-output .\\exports\\logs.summary.txt] [--summary-format json|table] [--summary-group-by level|source|service|traceId|correlationId|trace-id|correlation-id] [--summary-group-limit 10] [--summary-group-order count|key] [--summary-include-records first|last] [--summary-record-count 3] [--summary-record-fields timestamp,message] [--summary-records-format json|table|ndjson|csv] [--summary-records-output .\\exports\\logs.records.json] [--summary-records-template {name}-{mode}-{count}{ext}] [--sort asc|desc] [--format table|json|ndjson|csv]");
        Console.WriteLine("  dotnet run --project .\\samples\\HlogInspect\\HlogInspect.csproj -- --base-dir <path> --plugin-id <id> [--from 2026-09-24T12:00:00Z] [--to 2026-09-24T13:00:00Z] [--offset 0] [--level-in Info,Warn,Error] [--source OrdersService,BillingService] [--service api-gw,worker] [--traceId 0123456789abcdef0123456789abcdef,fedcba9876543210fedcba9876543210] [--correlationId aabbccddeeff00112233445566778899,99887766554433221100ffeeddccbbaa] [--contains timeout|timeout,orders] [--contains-field any|message|payload] [--contains-mode any|all] [--after-timestamp 2026-09-24T12:30:00Z] [--before-timestamp 2026-09-24T12:45:00Z] [--page-meta] [--counts-only] [--fields core|tracing|ops|errors|security|audit|forensics|triage|timestamp,level,message] [--output .\\exports\\logs.json] [--append-output] [--split-output-size 1024] [--split-output-template chunks\\{name}-chunk-{part}{ext}] [--split-output-start-index 10] [--template-number-style padded|plain] [--manifest-output .\\exports\\logs.manifest.ndjson] [--manifest-format json|ndjson] [--manifest-include-totals] [--manifest-include-window] [--manifest-include-level-counts] [--manifest-include-match-counts] [--manifest-include-output-hash] [--manifest-include-filters] [--manifest-include-command] [--summary-output .\\exports\\logs.summary.txt] [--summary-format json|table] [--summary-group-by level|source|service|traceId|correlationId|trace-id|correlation-id] [--summary-group-limit 10] [--summary-group-order count|key] [--summary-include-records first|last] [--summary-record-count 3] [--summary-record-fields timestamp,message] [--summary-records-format json|table|ndjson|csv] [--summary-records-output .\\exports\\logs.records.json] [--summary-records-template {name}-{mode}-{count}{ext}] [--sort asc|desc] [--format table|json|ndjson|csv]");
        Console.WriteLine("Aliases: --trace-id = --traceId, --correlation-id = --correlationId.");
        Console.WriteLine("Field aliases for --fields and --summary-record-fields: trace-id = traceId, correlation-id = correlationId, payload-json = payloadJson.");
    }
}

internal enum ContainsField
{
    Any,
    Message,
    Payload,
}

internal enum ContainsMode
{
    Any,
    All,
}

internal static class InspectFieldPresets
{
    public static readonly InspectField[] Core =
    [
        InspectField.Timestamp,
        InspectField.Level,
        InspectField.Source,
        InspectField.Service,
        InspectField.Message,
    ];

    public static readonly InspectField[] Tracing =
    [
        InspectField.Timestamp,
        InspectField.TraceId,
        InspectField.CorrelationId,
        InspectField.Source,
        InspectField.Service,
        InspectField.Message,
    ];

    public static readonly InspectField[] Ops =
    [
        InspectField.Timestamp,
        InspectField.Level,
        InspectField.Source,
        InspectField.Service,
        InspectField.Message,
        InspectField.TraceId,
        InspectField.CorrelationId,
    ];

    public static readonly InspectField[] Errors =
    [
        InspectField.Timestamp,
        InspectField.Level,
        InspectField.Source,
        InspectField.Service,
        InspectField.Message,
        InspectField.PayloadJson,
    ];

    public static readonly InspectField[] Security =
    [
        InspectField.Timestamp,
        InspectField.Source,
        InspectField.Service,
        InspectField.TraceId,
        InspectField.CorrelationId,
        InspectField.PayloadJson,
    ];

    public static readonly InspectField[] Audit =
    [
        InspectField.Timestamp,
        InspectField.Level,
        InspectField.Source,
        InspectField.Service,
        InspectField.Message,
        InspectField.TraceId,
        InspectField.CorrelationId,
        InspectField.PayloadJson,
    ];

    public static readonly InspectField[] Forensics =
    [
        InspectField.Timestamp,
        InspectField.Level,
        InspectField.Source,
        InspectField.Service,
        InspectField.TraceId,
        InspectField.CorrelationId,
        InspectField.PayloadJson,
    ];

    public static readonly InspectField[] Triage =
    [
        InspectField.Timestamp,
        InspectField.Level,
        InspectField.Source,
        InspectField.Message,
        InspectField.TraceId,
    ];
}