// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

// Based off of: https://github.com/dotnet/sdk/blob/e793aa4709d28cd783712df40413448250e26fea/test/Microsoft.NET.TestFramework/Commands/SdkCommandSpec.cs
using System.Diagnostics;
using Azure.Functions.Cli.Abstractions;

namespace Func.TestFramework
{
    public class CommandInfo
    {
        public required string FileName { get; set; }

        public List<string> Arguments { get; set; } = new List<string>();

        public Dictionary<string, string> Environment { get; set; } = new Dictionary<string, string>();

        public List<string> EnvironmentToRemove { get; } = new List<string>();

        public required string WorkingDirectory { get; set; }

        public string? TestName { get; set; }

        public Command ToCommand(bool doNotEscapeArguments = false)
        {
            var process = new Process()
            {
                StartInfo = ToProcessStartInfo()
            };

            return new Command(process, trimTrailingNewlines: true);
        }

        public ProcessStartInfo ToProcessStartInfo()
        {
            var psi = new ProcessStartInfo
            {
                FileName = FileName,
                Arguments = string.Join(" ", Arguments),
                UseShellExecute = false
            };

            foreach (var kvp in Environment)
            {
                psi.Environment[kvp.Key] = kvp.Value;
            }

            foreach (var envToRemove in EnvironmentToRemove)
            {
                psi.Environment.Remove(envToRemove);
            }

            if (WorkingDirectory is not null)
            {
                psi.WorkingDirectory = WorkingDirectory;
            }

            return psi;
        }
    }
}
