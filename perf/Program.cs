using BenchmarkDotNet.Reports;
using BenchmarkDotNet.Running;
using System.Text.Json;

namespace Func.Performance;

internal sealed class Program
{
    public static int Main(string[] args)
    {
        if (args is ["--list-commands"])
        {
            Console.WriteLine(JsonSerializer.Serialize(CommandCase.All.Select(command => command.Name)));
            return 0;
        }

        Summary[] summaries = [.. BenchmarkSwitcher.FromAssembly(typeof(Program).Assembly).Run(args)];
        return summaries.Length == 0 || summaries.Any(summary =>
            summary.HasCriticalValidationErrors ||
            summary.Reports.Any(report => !report.Success)) ? 1 : 0;
    }
}
