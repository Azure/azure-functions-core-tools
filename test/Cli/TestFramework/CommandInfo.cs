// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

// Based off of: https://github.com/dotnet/sdk/blob/e793aa4709d28cd783712df40413448250e26fea/test/Microsoft.NET.TestFramework/Commands/SdkCommandSpec.cs
using System.Diagnostics;
using Azure.Functions.Cli.Abstractions;

namespace Azure.Functions.Cli.TestFramework
{
    public class CommandInfo
    {
        public required string FileName { get; set; }

        public List<string> Arguments { get; set; } = [];

        public Dictionary<string, string> Environment { get; set; } = [];

        public List<string> EnvironmentToRemove { get; } = [];

        public required string WorkingDirectory { get; set; }

        public string? TestName { get; set; }

        public Command ToCommand()
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

            foreach (KeyValuePair<string, string> kvp in Environment)
            {
                psi.Environment[kvp.Key] = kvp.Value;
            }

            foreach (string envToRemove in EnvironmentToRemove)
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
