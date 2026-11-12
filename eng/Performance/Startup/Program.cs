using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.Json;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Engines;
using BenchmarkDotNet.Exporters.Json;
using BenchmarkDotNet.Jobs;
using BenchmarkDotNet.Reports;
using BenchmarkDotNet.Running;

namespace Azure.Functions.Cli.Performance.Startup;

internal static class Program
{
    private static int Main(string[] args)
    {
        if (args.Length != 2)
        {
            throw new ArgumentException("Expected startup request path and BenchmarkDotNet artifacts directory.");
        }

        Environment.SetEnvironmentVariable("FUNC_STARTUP_REQUEST", Path.GetFullPath(args[0]));
        if (RuntimeInformation.ProcessArchitecture != RuntimeInformation.OSArchitecture)
        {
            throw new InvalidOperationException("BenchmarkDotNet must run with native process architecture.");
        }

        var reader = new StartupRequestReader();
        StartupRequest request = reader.Read();
        Job job = Job.Default.WithStrategy(RunStrategy.Monitoring).WithLaunchCount(1).WithWarmupCount(0)
            .WithIterationCount(request.SamplesPerSeries).WithInvocationCount(1).WithUnrollFactor(1)
            .DontEnforcePowerPlan()
            .WithEnvironmentVariable("FUNC_STARTUP_REQUEST", Path.GetFullPath(args[0]));
        ManualConfig config = ManualConfig.Create(DefaultConfig.Instance).AddJob(job).AddExporter(JsonExporter.Full)
            .WithArtifactsPath(Path.GetFullPath(args[1]));
        Summary summary = BenchmarkRunner.Run<StartupBenchmark>(config);
        var measurements = new List<object>();
        bool successful = summary.Reports.Length == request.Series * 2;
        foreach (BenchmarkReport report in summary.Reports)
        {
            successful &= report.Success;
            string command = (string)report.BenchmarkCase.Parameters["Command"];
            int series = (int)report.BenchmarkCase.Parameters["Series"];
            Measurement[] actual = [.. report.AllMeasurements.Where(measurement =>
                measurement.IterationMode == IterationMode.Workload && measurement.IterationStage == IterationStage.Actual)];
            successful &= actual.Length == request.SamplesPerSeries && actual.All(measurement => measurement.Operations == 1);
            foreach (Measurement measurement in actual)
            {
                measurements.Add(new
                {
                    command,
                    series,
                    index = measurement.IterationIndex,
                    operations = measurement.Operations,
                    elapsedMs = measurement.Nanoseconds / 1e6,
                });
            }
        }

        string output = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(args[0]))!, "framework-measurements.json");
        using var file = new FileStream(output, FileMode.CreateNew, FileAccess.Write);
        JsonSerializer.Serialize(file, new
        {
            completed = successful,
            framework = $"BenchmarkDotNet {typeof(BenchmarkRunner).Assembly.GetName().Version?.ToString(3)}",
            strategy = RunStrategy.Monitoring.ToString(),
            harnessArchitecture = RuntimeInformation.ProcessArchitecture.ToString(),
            runtimeVersion = Environment.Version.ToString(),
            measurements,
        });
        return successful ? 0 : 1;
    }
}
