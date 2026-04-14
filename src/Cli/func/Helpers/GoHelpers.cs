// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Text;
using System.Text.RegularExpressions;
using Azure.Functions.Cli.Common;
using Colors.Net;
using static Azure.Functions.Cli.Common.OutputTheme;

namespace Azure.Functions.Cli.Helpers
{
    public static class GoHelpers
    {
        private const int MinimumGoMajorVersion = 1;
        private const int MinimumGoMinorVersion = 24;

        public static async Task<WorkerLanguageVersionInfo> GetEnvironmentGoVersion()
        {
            return await GetVersion("go");
        }

        public static void AssertGoVersion(WorkerLanguageVersionInfo goVersion)
        {
            if (goVersion?.Version == null)
            {
                throw new CliException(
                    $"Could not find a Go installation. Go {MinimumGoMajorVersion}.{MinimumGoMinorVersion} or later is required. " +
                    "Please install Go from https://go.dev/dl/");
            }

            if (GlobalCoreToolsSettings.IsVerbose)
            {
                ColoredConsole.WriteLine(VerboseColor($"Found Go version {goVersion.Version} ({goVersion.ExecutablePath})."));
            }

            if (goVersion.Major == null || goVersion.Minor == null)
            {
                throw new CliException(
                    $"Unable to parse Go version '{goVersion.Version}'. " +
                    $"Go {MinimumGoMajorVersion}.{MinimumGoMinorVersion} or later is required.");
            }

            // Accept any major version > 1, or major == 1 with minor >= 24
            if (goVersion.Major > MinimumGoMajorVersion)
            {
                return;
            }

            if (goVersion.Major == MinimumGoMajorVersion && goVersion.Minor >= MinimumGoMinorVersion)
            {
                return;
            }

            throw new CliException(
                $"Go version {goVersion.Version} is not supported. " +
                $"Go {MinimumGoMajorVersion}.{MinimumGoMinorVersion} or later is required. " +
                "Please update Go from https://go.dev/dl/");
        }

        public static async Task SetupProject(string moduleName, bool skipGoModTidy)
        {
            var goVersion = await GetEnvironmentGoVersion();
            AssertGoVersion(goVersion);

            // Initialize: Run go mod init
            var modInitExe = new Executable("go", $"mod init {moduleName}");
            var modInitExitCode = await modInitExe.RunAsync(
                l => ColoredConsole.WriteLine(l),
                e => ColoredConsole.Error.WriteLine(ErrorColor(e)));
            if (modInitExitCode != 0)
            {
                throw new CliException($"Failed to initialize Go module. 'go mod init {moduleName}' exited with code {modInitExitCode}.");
            }

            // Fetch the Azure Functions Go worker dependency
            var goGetExe = new Executable("go", "get github.com/azure/azure-functions-golang-worker");
            var goGetExitCode = await goGetExe.RunAsync(
                l => ColoredConsole.WriteLine(l),
                e => ColoredConsole.Error.WriteLine(ErrorColor(e)));
            if (goGetExitCode != 0)
            {
                throw new CliException("Failed to add Azure Functions Go worker dependency. 'go get' exited with a non-zero code.");
            }

            if (!skipGoModTidy)
            {
                var tidyExe = new Executable("go", "mod tidy");
                var tidyExitCode = await tidyExe.RunAsync(
                    l => ColoredConsole.WriteLine(l),
                    e => ColoredConsole.Error.WriteLine(ErrorColor(e)));
                if (tidyExitCode != 0)
                {
                    ColoredConsole.WriteLine(WarningColor("Warning: 'go mod tidy' exited with a non-zero code. You may need to run it manually."));
                }
            }
            else
            {
                ColoredConsole.WriteLine(AdditionalInfoColor("Skipped \"go mod tidy\". You must run \"go mod tidy\" manually."));
            }
        }

        private static async Task<WorkerLanguageVersionInfo> GetVersion(string goExe)
        {
            try
            {
                var exe = new Executable(goExe, "version");
                var sb = new StringBuilder();
                var exitCode = await exe.RunAsync(l => sb.AppendLine(l), e => sb.AppendLine(e));

                if (exitCode == 0)
                {
                    var output = sb.ToString().Trim();

                    // Parse "go version go1.24.2 linux/amd64" format
                    var match = Regex.Match(output, @"go(\d+\.\d+(?:\.\d+)?)");
                    if (match.Success)
                    {
                        return new WorkerLanguageVersionInfo(WorkerRuntime.Native, match.Groups[1].Value, goExe);
                    }
                }
            }
            catch (Exception)
            {
                // Go is not installed or not on PATH
            }

            return null;
        }
    }
}
