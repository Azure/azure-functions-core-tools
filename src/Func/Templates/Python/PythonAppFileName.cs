// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

namespace Azure.Functions.Cli.Templates.Python;

internal static class PythonAppFileName
{
    public static string? GetDiagnostic(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Trim() != value || value.Contains('/') || value.Contains('\\')
            || value.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || value.Any(char.IsControl)
            || value.IndexOfAny(['<', '>', ':', '"', '|', '?', '*']) >= 0
            || !value.EndsWith(".py", StringComparison.OrdinalIgnoreCase) || value.Length <= ".py".Length
            || value.TrimEnd(' ', '.') != value)
        {
            return "Supply one project-relative Python filename ending in '.py', without directories, invalid characters or trailing spaces.";
        }

        string stem = value.Split('.')[0];
        return stem.Equals("CON", StringComparison.OrdinalIgnoreCase) || stem.Equals("PRN", StringComparison.OrdinalIgnoreCase)
            || stem.Equals("AUX", StringComparison.OrdinalIgnoreCase) || stem.Equals("NUL", StringComparison.OrdinalIgnoreCase)
            || (stem.Length == 4 && stem[3] is >= '1' and <= '9'
                && (stem.StartsWith("COM", StringComparison.OrdinalIgnoreCase) || stem.StartsWith("LPT", StringComparison.OrdinalIgnoreCase)))
            ? "Choose a Python filename that is not a reserved device name."
            : null;
    }
}
