# HobiaryLogPipe performance harness

Standalone, dependency-free performance measurements for the adapter hot paths.

Run a short sample:

```powershell
dotnet run --project tools/HobiaryLogPipe.Performance/HobiaryLogPipe.Performance.csproj -- --iterations 100000 --warmup 10000 --batch-size 1000
```

The harness reports operations per second and per-operation p50/p95 latency for:

- `LogMessageProcessor`: JSON parsing, mapping, filtering, and enqueueing into `BatchWriter`.
- `BatchAckTracker`: batch threshold and timeout decision logic.

This is a measurement tool, not a pass/fail test. Compare results on the same machine, runtime, build configuration, and payload shape. The processor measurement writes to a temporary HobiaryLogDB directory and removes it after the run.
