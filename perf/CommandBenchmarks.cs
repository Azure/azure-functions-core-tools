using System.Diagnostics;
using BenchmarkDotNet.Attributes;

namespace Func.Performance;

// BenchmarkDotNet requires public benchmark types and parameter members for discovery.
public class CommandBenchmarks
{
    private readonly CommandRunner _runner = new(TimeSpan.FromSeconds(10));
    private readonly BenchmarkEnvironment _environment = new();
    private ProcessStartInfo? _startInfo;
    private CommandCase? _case;

    [ParamsSource(nameof(Commands))]
    public string Command { get; set; } = "";

    public IEnumerable<string> Commands
    {
        get
        {
            string selection = Environment.GetEnvironmentVariable("FUNC_PERF_COMMAND") ?? "all";
            string[] names = [.. CommandCase.All
                .Where(command => selection == "all" || command.Name == selection).Select(command => command.Name)];
            return names.Length != 0 ? names : throw new ArgumentException($"Unknown benchmark command '{selection}'.");
        }
    }

    [GlobalSetup]
    public async Task SetupAsync()
    {
        _case = CommandCase.All.Single(command => command.Name == Command);
        _startInfo = _environment.Prepare(_case);
        // Validate and warm the executable before BenchmarkDotNet's own warmup.
        await _runner.RunAsync(_startInfo, _case, CancellationToken.None);
    }

    [Benchmark]
    public Task ExecuteAsync() => _runner.RunAsync(
        _startInfo ?? throw new InvalidOperationException("Benchmark setup has not run."),
        _case ?? throw new InvalidOperationException("Benchmark setup has not run."),
        CancellationToken.None);

    [GlobalCleanup]
    public void Cleanup() => _environment.Dispose();
}
