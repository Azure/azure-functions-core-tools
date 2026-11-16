using System.Text.RegularExpressions;

namespace Func.Performance;

internal sealed record CommandCase(string Name, string[] Arguments, string OutputPattern, int ExitCode = 0)
{
    internal static IReadOnlyList<CommandCase> All { get; } =
    [
        new("version", ["version"], @"\A\d+\.\d+\.\d+(?:-[0-9A-Za-z.-]+)?(?:\+[0-9A-Za-z.-]+)?\s*\z"),
    ];

    internal void Validate(int exitCode, string output, string error)
    {
        if (exitCode != ExitCode)
        {
            throw new InvalidOperationException($"func {Name} exited with {exitCode}, expected {ExitCode}. stderr: {error}");
        }

        if (!Regex.IsMatch(output, OutputPattern, RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1)))
        {
            throw new InvalidOperationException($"func {Name} produced unexpected stdout: {output}. stderr: {error}");
        }
    }
}
