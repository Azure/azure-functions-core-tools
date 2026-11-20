using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text.Json;
using BenchmarkDotNet.Attributes;

namespace Azure.Functions.Cli.Performance.Startup;

// BenchmarkDotNet's generated assembly must construct the workload and access its parameters.
public class StartupBenchmark
{
    private readonly StartupRequestReader _reader = new();
    private StartupRequest? _request;
    private StartupFixture? _fixture;
    private StartupProcess? _process;
    private int _index;

    [Params("--help", "version")]
    public string Command { get; set; } = string.Empty;

    [ParamsSource(nameof(SeriesValues))]
    public int Series { get; set; }

    public IEnumerable<int> SeriesValues
    {
        get
        {
            StartupRequest request = _reader.Read();
            for (int index = 1; index <= request.Series; index++)
            {
                yield return index;
            }
        }
    }

    [GlobalSetup]
    public void PrepareFixture()
    {
        _request = _reader.Read();
        _fixture = new StartupFixture(Path.Combine(_request.TemporaryStatePath, $"{Series}-{Command.TrimStart('-')}"));
    }

    [IterationSetup]
    public void PrepareProcess()
    {
        StartupRequest request = _request ?? throw new InvalidOperationException("Request was not loaded.");
        StartupFixture fixture = _fixture ?? throw new InvalidOperationException("Fixture was not prepared.");
        _process = new StartupProcess(request.Executable, [Command], fixture.DirectoryPath, fixture.EnvironmentVariables,
            checked(request.TimeoutSeconds * 1000));
    }

    [Benchmark]
    public void LaunchToExit() => (_process ?? throw new InvalidOperationException("Process was not prepared.")).StartAndWait();

    [IterationCleanup]
    public void QualifyAndDispose()
    {
        StartupProcess process = _process ?? throw new InvalidOperationException("Process was not prepared.");
        try
        {
            if (!process.HasStarted)
            {
                // Preserve a launch exception from the workload; no iteration can be qualified.
                return;
            }

            StartupObservation observation = process.Capture();
            StartupRequest request = _request ?? throw new InvalidOperationException("Request was not loaded.");
            bool valid = observation.Status == "exited" && observation.ExitCode == 0 && string.IsNullOrWhiteSpace(observation.Stderr);
            valid &= Command == "version"
                ? observation.Stdout.Trim() == request.Version
                : observation.Stdout.Contains("Azure Functions CLI", StringComparison.Ordinal) &&
                    observation.Stdout.Contains("Usage", StringComparison.Ordinal) && observation.Stdout.Contains(request.Version, StringComparison.Ordinal);
            int index = ++_index;
            var sample = new
            {
                command = Command,
                series = Series,
                index,
                kind = index == 1 ? "first-in-series" : "subsequent",
                innerElapsedMs = observation.ElapsedMs,
                processId = observation.ProcessId,
                status = valid ? "valid" : "invalid",
                processStatus = observation.Status,
                exitCode = observation.ExitCode,
                stdout = observation.Stdout,
                stderr = observation.Stderr,
            };
            string destination = Path.Combine(request.SampleDirectory,
                $"{Series.ToString(CultureInfo.InvariantCulture)}-{Command.TrimStart('-')}-{index.ToString(CultureInfo.InvariantCulture)}.json");
            using var file = new FileStream(destination, FileMode.CreateNew, FileAccess.Write);
            JsonSerializer.Serialize(file, sample);
            if (!valid)
            {
                throw new InvalidOperationException($"Invalid '{Command}' sample in series {Series}. See captured output in the report.");
            }
        }
        finally
        {
            process.Dispose();
            _process = null;
        }
    }

    [GlobalCleanup]
    public void RemoveFixture()
    {
        _process?.Dispose();
        if (_fixture is { } fixture && Directory.Exists(fixture.DirectoryPath))
        {
            Directory.Delete(fixture.DirectoryPath, recursive: true);
        }
    }
}

internal sealed record StartupRequest(string Executable, string Version, string TemporaryStatePath, string SampleDirectory,
    int Series, int SamplesPerSeries, int TimeoutSeconds);

internal class StartupRequestReader
{
    internal virtual StartupRequest Read()
    {
        string path = Environment.GetEnvironmentVariable("FUNC_STARTUP_REQUEST")
            ?? throw new InvalidOperationException("FUNC_STARTUP_REQUEST is required.");
        return JsonSerializer.Deserialize<StartupRequest>(File.ReadAllText(path))
            ?? throw new InvalidOperationException("Startup request must not be null.");
    }
}
