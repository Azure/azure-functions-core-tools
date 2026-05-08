// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Text;
using System.Text.RegularExpressions;
using Azure.Functions.Cli.Actions.LocalActions.PackAction;
using Azure.Functions.Cli.Common;
using Colors.Net;
using static Azure.Functions.Cli.Common.OutputTheme;

namespace Azure.Functions.Cli.Helpers
{
    internal static class GoHelpers
    {
        private const int MinimumGoMajorVersion = 1;
        private const int MinimumGoMinorVersion = 24;
        internal const string GoBinaryName = "app";
        internal const string GoBinDir = "bin";
        internal const string GoModFileName = "go.mod";

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

        /// <summary>
        /// Compiles the user's Go project into a binary named <see cref="GoBinaryName"/>
        /// under <c><see cref="GoBinDir"/>/</c> in <paramref name="workingDirectory"/>.
        /// The host resolves this binary via <c>defaultExecutablePath</c> in
        /// <c>workers/native/worker.config.json</c> (the host appends <c>.exe</c> on
        /// Windows automatically).
        /// </summary>
        public static async Task BuildProject(string workingDirectory = null)
        {
            workingDirectory ??= Environment.CurrentDirectory;
            var outputName = OperatingSystem.IsWindows() ? $"{GoBinaryName}.exe" : GoBinaryName;
            var outputPath = Path.Combine(GoBinDir, outputName);

            if (IsBinaryUpToDate(workingDirectory, outputPath))
            {
                ColoredConsole.WriteLine($"Go worker binary '{outputPath}' is up to date — skipping build.");
                return;
            }

            ColoredConsole.WriteLine($"Building Go worker binary '{outputPath}'...");

            // Ensure bin/ exists so `go build -o bin/app` doesn't fail.
            Directory.CreateDirectory(Path.Combine(workingDirectory, GoBinDir));

            var stderrBuffer = new StringBuilder();
            var exe = new Executable("go", $"build -o \"{outputPath}\" .", workingDirectory: workingDirectory);
            var exitCode = await exe.RunAsync(
                l => ColoredConsole.WriteLine(l),
                e =>
                {
                    stderrBuffer.AppendLine(e);
                    ColoredConsole.Error.WriteLine(ErrorColor(e));
                });

            if (exitCode != 0)
            {
                if (LooksLikeStaleModules(stderrBuffer.ToString()))
                {
                    ColoredConsole.WriteLine(AdditionalInfoColor("Hint: run \"go mod tidy\" to update module dependencies, then re-run \"func start\"."));
                }

                throw new CliException($"Go build failed with exit code {exitCode}. See output above for details.");
            }
        }

        private static bool LooksLikeStaleModules(string stderr)
        {
            if (string.IsNullOrEmpty(stderr))
            {
                return false;
            }

            return stderr.Contains("missing go.sum entry", StringComparison.OrdinalIgnoreCase)
                || stderr.Contains("no required module provides package", StringComparison.OrdinalIgnoreCase)
                || stderr.Contains("inconsistent vendoring", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Returns <c>true</c> when <paramref name="binaryName"/> exists in
        /// <paramref name="workingDirectory"/> and is newer than every project source file
        /// (including assets referenced by <c>//go:embed</c>) plus <c>go.mod</c>/<c>go.sum</c>
        /// in the same directory tree. Lets <c>func start</c> skip a redundant <c>go build</c>
        /// on warm reruns.
        /// </summary>
        /// <remarks>
        /// To stay correct for <c>//go:embed</c> directives without parsing source, this widens
        /// the freshness check to every regular file under <paramref name="workingDirectory"/>,
        /// then excludes things that don't affect the produced binary:
        /// <list type="bullet">
        ///   <item>The binary itself (and its <c>.exe</c> variant on Windows).</item>
        ///   <item>Hidden files/directories (<c>.git</c>, <c>.vscode</c>, etc.).</item>
        ///   <item><c>vendor/</c> — vendored deps' freshness is implied by <c>go.mod</c>/<c>go.sum</c>.</item>
        ///   <item><c>*_test.go</c> — test files are not compiled into the production binary.</item>
        /// </list>
        /// Any I/O failure conservatively returns <c>false</c> so the build runs.
        /// </remarks>
        internal static bool IsBinaryUpToDate(string workingDirectory, string binaryName)
        {
            var binaryPath = Path.Combine(workingDirectory, binaryName);
            if (!FileSystemHelpers.FileExists(binaryPath))
            {
                return false;
            }

            DateTime binaryWriteTime;
            try
            {
                binaryWriteTime = File.GetLastWriteTimeUtc(binaryPath);
            }
            catch (IOException)
            {
                return false;
            }

            string fullWorkingDir = Path.GetFullPath(workingDirectory);
            string binaryFullPath = Path.GetFullPath(binaryPath);
            string binaryExeFullPath = Path.GetFullPath(Path.Combine(workingDirectory, $"{GoBinaryName}.exe"));

            try
            {
                foreach (var source in Directory.EnumerateFiles(fullWorkingDir, "*", SearchOption.AllDirectories))
                {
                    if (ShouldIgnoreForFreshnessCheck(source, fullWorkingDir, binaryFullPath, binaryExeFullPath))
                    {
                        continue;
                    }

                    if (File.GetLastWriteTimeUtc(source) > binaryWriteTime)
                    {
                        return false;
                    }
                }
            }
            catch (IOException)
            {
                return false;
            }
            catch (UnauthorizedAccessException)
            {
                return false;
            }

            return true;
        }

        private static bool ShouldIgnoreForFreshnessCheck(string source, string workingDir, string binaryFullPath, string binaryExeFullPath)
        {
            // Skip the binary itself — it always post-dates its own sources.
            if (string.Equals(source, binaryFullPath, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(source, binaryExeFullPath, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            // Test files are not compiled into the production binary.
            if (source.EndsWith("_test.go", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            // Walk the relative path segments and skip vendor/ + hidden directories/files.
            string relative = Path.GetRelativePath(workingDir, source);
            string[] segments = relative.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

            // Skip files under the root-level bin/ directory (our build output).
            // Only check the first segment so nested dirs like pkg/bin/ aren't excluded.
            if (segments.Length > 0 && string.Equals(segments[0], GoBinDir, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            foreach (var segment in segments)
            {
                if (segment.Length == 0)
                {
                    continue;
                }

                if (segment[0] == '.')
                {
                    return true;
                }

                if (string.Equals(segment, "vendor", StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Verifies that the compiled Go worker binary exists in
        /// <paramref name="workingDirectory"/>. Throws a <see cref="CliException"/>
        /// with an actionable message when missing — used by <c>func start --no-build</c>.
        /// </summary>
        public static void AssertBinaryExists(string workingDirectory = null)
        {
            workingDirectory ??= Environment.CurrentDirectory;
            var binaryName = OperatingSystem.IsWindows() ? $"{GoBinaryName}.exe" : GoBinaryName;
            var binaryPath = Path.Combine(workingDirectory, GoBinDir, binaryName);

            if (!FileSystemHelpers.FileExists(binaryPath))
            {
                throw new CliException(
                    $"Could not find a built Go binary '{Path.Combine(GoBinDir, binaryName)}' in '{workingDirectory}'. " +
                    $"Run 'func start' without '--no-build' to compile, or run 'go build -o {Path.Combine(GoBinDir, binaryName)} .' manually.");
            }
        }

        /// <summary>
        /// Cross-compiles the user's Go project into a linux/amd64 binary named
        /// <see cref="GoBinaryName"/> in <paramref name="workingDirectory"/>. Used
        /// by <c>func pack</c> and <c>func publish</c> — the produced binary is the
        /// one that runs on the Linux Functions host, regardless of the dev OS.
        /// </summary>
        /// <remarks>
        /// TODO(arm64): Currently hardcoded to linux/amd64. Linux ARM64 Function Apps
        /// are not yet supported by Core Tools' Go pipeline. When the ARM resource
        /// model exposes the target instance architecture, this method should accept
        /// a target arch and the publish path should reject mismatches up front.
        /// </remarks>
        internal static async Task BuildForLinux(string workingDirectory = null)
        {
            workingDirectory ??= Environment.CurrentDirectory;

            var goVersion = await GetEnvironmentGoVersion();
            AssertGoVersion(goVersion);

            var outputPath = Path.Combine(GoBinDir, GoBinaryName);
            ColoredConsole.WriteLine($"Building Go worker binary '{outputPath}' for linux/amd64...");

            // Ensure bin/ exists so `go build -o bin/app` doesn't fail.
            Directory.CreateDirectory(Path.Combine(workingDirectory, GoBinDir));

            var env = new Dictionary<string, string>
            {
                ["CGO_ENABLED"] = "0",
                ["GOOS"] = "linux",
                ["GOARCH"] = "amd64",
            };

            var exe = new Executable("go", $"build -o \"{outputPath}\" .", workingDirectory: workingDirectory, environmentVariables: env);

            // Stream stderr inline (red) and let the thrown exception summarize on failure
            // rather than re-printing the captured output, so users don't see compiler errors twice.
            var exitCode = await exe.RunAsync(
                l => ColoredConsole.WriteLine(l),
                e => ColoredConsole.Error.WriteLine(ErrorColor(e)));

            if (exitCode != 0)
            {
                throw new CliException(
                    $"Go build for linux/amd64 failed with exit code {exitCode}. See output above for details.");
            }
        }

        /// <summary>
        /// Ensures the Go binary in <paramref name="workingDirectory"/> is a valid
        /// Linux x86_64 executable suitable for the Azure Functions Linux host.
        /// Validates the binary's structure by checking its ELF header format.
        /// Called by <c>func pack --no-build</c> to confirm the binary was properly
        /// cross-compiled before packaging for deployment.
        /// </summary>
        internal static void AssertLinuxAmd64Binary(string workingDirectory = null)
        {
            workingDirectory ??= Environment.CurrentDirectory;
            var binaryPath = Path.Combine(workingDirectory, GoBinDir, GoBinaryName);
            var displayPath = Path.Combine(GoBinDir, GoBinaryName);

            void ValidateBinaryExists()
            {
                if (!FileSystemHelpers.FileExists(binaryPath))
                {
                    throw new CliException(
                        $"Could not find a built Go binary '{displayPath}' in '{workingDirectory}'. " +
                        $"Run 'func pack' without '--no-build' to cross-compile, or run " +
                        $"'CGO_ENABLED=0 GOOS=linux GOARCH=amd64 go build -o {displayPath} .' manually.");
                }
            }

            byte[] ReadElfHeader()
            {
                // Read the first 20 bytes of the ELF header so we can validate magic, class, data, and machine.
                // Layout reference: https://refspecs.linuxfoundation.org/elf/gabi4+/ch4.eheader.html
                var header = new byte[20];
                using (var fs = new FileStream(binaryPath, FileMode.Open, FileAccess.Read))
                {
                    int read = fs.Read(header, 0, header.Length);
                    if (read < header.Length)
                    {
                        throw new CliException(
                            $"'{binaryPath}' is too small to be an ELF binary. " +
                            "Re-run 'func pack' without '--no-build' to produce a linux/amd64 binary.");
                    }
                }

                return header;
            }

            // ELF magic is the file signature that identifies an ELF (Executable and Linkable Format) binary
            void ValidateElfMagic(byte[] header)
            {
                // ELF magic: 0x7F 'E' 'L' 'F'
                bool isElf = header[0] == 0x7F && header[1] == (byte)'E' && header[2] == (byte)'L' && header[3] == (byte)'F';
                if (!isElf)
                {
                    throw new CliException(
                        $"'{binaryPath}' is not an ELF binary. Core Tools currently only supports linux/amd64 Go targets. " +
                        $"Re-run 'func pack' without '--no-build', or build with 'CGO_ENABLED=0 GOOS=linux GOARCH=amd64 go build -o {displayPath} .'");
                }
            }

            void ValidateElfFormat(byte[] header)
            {
                // EI_CLASS == 2 (64-bit), EI_DATA == 1 (little endian)
                if (header[4] != 2 || header[5] != 1)
                {
                    throw new CliException(
                        $"'{binaryPath}' is not a 64-bit little-endian ELF binary. Core Tools currently only supports linux/amd64 Go targets. " +
                        $"Re-run 'func pack' without '--no-build', or build with 'CGO_ENABLED=0 GOOS=linux GOARCH=amd64 go build -o {displayPath} .'");
                }
            }

            void ValidateElfMachine(byte[] header)
            {
                // e_machine is at offset 18, 2 bytes little-endian. 0x3E == EM_X86_64.
                ushort eMachine = (ushort)(header[18] | (header[19] << 8));
                if (eMachine != 0x3E)
                {
                    throw new CliException(
                        $"'{binaryPath}' targets machine 0x{eMachine:X4}, not x86_64. Core Tools currently only supports linux/amd64 Go targets. " +
                        $"Re-run 'func pack' without '--no-build', or build with 'CGO_ENABLED=0 GOOS=linux GOARCH=amd64 go build -o {displayPath} .'");
                }
            }

            // Execute validations in order
            ValidateBinaryExists();
            var header = ReadElfHeader();
            ValidateElfMagic(header);
            ValidateElfFormat(header);
            ValidateElfMachine(header);
        }

        /// <summary>
        /// Returns the explicit allowlist of files that should be included in the
        /// Go deployment zip. Centralizes the list so that <see cref="ZipHelper"/>
        /// (zip composition) and <c>func publish --list-included-files</c> stay in sync.
        /// </summary>
        internal static IEnumerable<string> GetPackFiles(string functionAppRoot)
        {
            functionAppRoot ??= Environment.CurrentDirectory;
            return new[]
            {
                Path.Combine(functionAppRoot, Constants.HostJsonFileName),
                Path.Combine(functionAppRoot, GoBinDir, GoBinaryName),
            };
        }

        /// <summary>
        /// Verifies the function app root has the files required to build/publish a Go app
        /// (<c>host.json</c> and <c>go.mod</c>) by routing through <see cref="PackValidationHelper"/>
        /// so <c>func pack</c> and <c>func publish</c> share the same validation flow and messages.
        /// Throws <see cref="CliException"/> on failure.
        /// </summary>
        internal static void AssertGoFunctionAppLayout(string functionAppRoot)
        {
            functionAppRoot ??= Environment.CurrentDirectory;

            var validations = new List<Action<string>>
            {
                dir => PackValidationHelper.RunRequiredFilesValidation(dir, new[] { GoModFileName }, "Validate go.mod"),
            };
            PackValidationHelper.RunValidations(functionAppRoot, validations);
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
