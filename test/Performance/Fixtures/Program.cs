using System;
using System.IO;
using System.Threading;

namespace Azure.Functions.Cli.Performance.Startup.Fixture;

internal static class Program
{
    private static int Main(string[] args)
    {
        Console.OutputEncoding = System.Text.Encoding.UTF8;
        string scenario = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "scenario.txt"));
        if (Path.GetFileName(Environment.CurrentDirectory) == "preflight")
        {
            if (scenario == "missing-after-preflight")
            {
                string executable = Environment.ProcessPath ?? throw new InvalidOperationException("Fixture process path is unavailable.");
                File.Move(executable, executable + ".moved");
            }

            Console.WriteLine("5.0.0");
            return 0;
        }

        switch (scenario)
        {
            case "timeout":
                Console.Write($"ready:{Environment.ProcessId}");
                Console.Out.Flush();
                Thread.Sleep(TimeSpan.FromSeconds(120));
                return 0;
            case "nonzero":
                Console.Error.Write("expected-nonzero");
                return 23;
            case "stderr":
                Console.Error.Write("unexpected-stderr");
                break;
            case "wrong-output":
                Console.WriteLine("not-the-requested-command-output");
                return 0;
            default:
                throw new InvalidOperationException($"Unknown fixture scenario: {scenario}");
        }

        Console.WriteLine(args is ["version"] ? "5.0.0" : "Azure Functions CLI 5.0.0 Usage");
        return 0;
    }
}
