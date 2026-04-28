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

            // Scaffold go.mod, main.go, and .funcignore from embedded templates.
            // The SDK version is pinned inside go.mod.template so that contributors
            // bumping the SDK only touch template files (no C# change required) and
            // `go mod tidy` resolves the pinned version deterministically.
            var goModContent = (await StaticResources.GoMod).Replace("{ModuleName}", moduleName);
            await FileSystemHelpers.WriteFileIfNotExists("go.mod", goModContent);
            await FileSystemHelpers.WriteFileIfNotExists("main.go", await StaticResources.MainGo);
            await FileSystemHelpers.WriteFileIfNotExists(Constants.FuncIgnoreFile, await StaticResources.FuncIgnore);

            if (!skipGoModTidy)
            {
                await RunGoCommandAsync("mod tidy", null, throwOnFailure: false);
            }
            else
            {
                ColoredConsole.WriteLine(AdditionalInfoColor("Skipped \"go mod tidy\". You must run \"go mod tidy\" manually."));
            }
        }

        private static async Task RunGoCommandAsync(string arguments, string errorMessage, bool throwOnFailure = true)
        {
            var exe = new Executable("go", arguments);
            var stderr = new StringBuilder();
            var exitCode = await exe.RunAsync(
                l =>
                {
                    if (GlobalCoreToolsSettings.IsVerbose)
                    {
                        ColoredConsole.WriteLine(VerboseColor(l));
                    }
                },
                e =>
                {
                    stderr.AppendLine(e);
                    if (GlobalCoreToolsSettings.IsVerbose)
                    {
                        ColoredConsole.WriteLine(VerboseColor(e));
                    }
                });

            if (exitCode != 0)
            {
                var stderrOutput = stderr.ToString().Trim();
                if (throwOnFailure)
                {
                    var detail = string.IsNullOrEmpty(stderrOutput) ? string.Empty : $" {stderrOutput}";
                    throw new CliException($"{errorMessage}{detail}");
                }

                ColoredConsole.WriteLine(WarningColor($"Warning: 'go {arguments}' exited with a non-zero code. You may need to run it manually."));
                if (!string.IsNullOrEmpty(stderrOutput))
                {
                    ColoredConsole.WriteLine(WarningColor(stderrOutput));
                }
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
                        return new WorkerLanguageVersionInfo(WorkerRuntime.Go, match.Groups[1].Value, goExe);
                    }
                }
            }
            catch (Exception ex)
            {
                if (GlobalCoreToolsSettings.IsVerbose)
                {
                    ColoredConsole.WriteLine(VerboseColor($"Unable to detect Go version: {ex.Message}"));
                }
            }

            return null;
        }
    }
}
