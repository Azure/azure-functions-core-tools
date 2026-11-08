// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.CommandLine;
using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using Azure.Functions.Cli.Bundles;
using Azure.Functions.Cli.Commands;
using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Configuration;
using Azure.Functions.Cli.Console;
using Azure.Functions.Cli.Projects;
using Azure.Functions.Cli.Projects.FileWriting;
using Azure.Functions.Cli.Templates.Engine;
using Azure.Functions.Cli.Tests.Common;
using Azure.Functions.Cli.Workers;
using Azure.Functions.Cli.Workloads;
using Azure.Functions.Cli.Workloads.Storage;
using Microsoft.TemplateEngine.Abstractions;
using Microsoft.TemplateEngine.Abstractions.Installer;
using Microsoft.TemplateEngine.Abstractions.TemplatePackage;
using Microsoft.TemplateEngine.Edge.Settings;
using NSubstitute;
using Xunit.Abstractions;

namespace Azure.Functions.Cli.Tests.Commands;

public class InitCommandTemplateTests(ITestOutputHelper output)
{
    private const string ProjectTemplateIdentity = "Microsoft.AzureFunctions.ProjectTemplate.CSharp.Isolated.3.x";
    private static readonly Guid _restoreAction = new("210D431B-A78B-4D2F-B762-4ED3E3EA9025");
    private readonly CliProcessTestRunner _processRunner = new(output);

    [WindowsX64TemplateSourceFact]
    public async Task Init_CSharpNet10_UsesTemplateEngineAndPublishesConfigAfterRestore()
    {
        string root = Path.Combine(Path.GetTempPath(), $"func-init-template-{Guid.NewGuid():N}");
        string target = Path.Combine(root, "project");
        string home = Path.Combine(root, "home");
        Directory.CreateDirectory(root);

        try
        {
            TemplateEngineContext registrationContext = new(WorkingDirectory.FromExplicit(target));
            using (TemplateEngineSession registration = new(registrationContext, Path.Combine(home, "templates")))
            {
                IReadOnlyList<InstallResult> installed = await registration.PackageManager
                    .GetBuiltInManagedProvider(InstallationScope.Global)
                    .InstallAsync(
                        [new InstallRequest(PackagePath("project"))],
                        CancellationToken.None);
                Assert.True(Assert.Single(installed).Success, installed[0].ErrorMessage);
            }

            bool restoreObserved = false;

            var stack = Substitute.For<IProjectStack, IProjectTemplateTasks>();
            stack.Stack.Returns("dotnet");
            stack.DisplayName.Returns(".NET");
            stack.WorkerRuntimeAliases.Returns([]);
            stack.SupportedLanguages.Returns(["C#", "F#"]);
            stack.SupportedLanguageAliases.Returns(new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase)
            {
                ["C#"] = ["csharp"],
                ["F#"] = ["fsharp"]
            });
            var tasks = (IProjectTemplateTasks)stack;
            tasks.RequiresExecutionAsync(
                    Arg.Any<WorkingDirectory>(), Arg.Any<Guid>(), Arg.Any<IReadOnlyDictionary<string, string>>(), Arg.Any<CancellationToken>())
                .Returns(call =>
                {
                    Guid actionId = call.Arg<Guid>();
                    if (actionId != _restoreAction || call.Arg<IReadOnlyDictionary<string, string>>().Count != 0)
                    {
                        throw new NotSupportedException($"Unexpected project template action '{actionId}'.");
                    }

                    return Task.FromResult(true);
                });
            tasks.ExecuteAsync(
                    Arg.Any<WorkingDirectory>(), Arg.Any<Guid>(), Arg.Any<IReadOnlyDictionary<string, string>>(), Arg.Any<CancellationToken>())
                .Returns(call =>
                {
                    Assert.Equal(_restoreAction, call.Arg<Guid>());
                    Assert.True(File.Exists(Path.Combine(target, "MyApp.csproj")));
                    Assert.False(File.Exists(Path.Combine(target, ".func", "config.json")));
                    restoreObserved = true;
                    return Task.CompletedTask;
                });

            var projectResolver = Substitute.For<IFunctionsProjectResolver>();
            projectResolver.ResolveProjectAsync(Arg.Any<ProjectResolutionContext>(), Arg.Any<CancellationToken>())
                .Returns(call => Task.FromResult<ProjectResolutionResult>(
                    new ProjectResolutionResult.Resolved(new TestProject(call.Arg<ProjectResolutionContext>().WorkingDirectory), "resolved")));
            var localSettings = Substitute.For<ILocalSettingsProvider>();
            localSettings.Get(Arg.Any<DirectoryInfo>()).Returns(LocalSettingsSnapshot.Empty);
            var bundleReader = Substitute.For<IHostJsonBundleSectionReader>();
            bundleReader.ReadAsync(Arg.Any<DirectoryInfo>(), Arg.Any<CancellationToken>()).Returns((HostJsonBundleSection?)null);
            var bundles = Substitute.For<IInstalledBundleWorkloads>();
            bundles.ListInstalledAsync(Arg.Any<CancellationToken>()).Returns([]);
            var interaction = Substitute.For<IInteractionService>();
            interaction.IsInteractive.Returns(false);
            var initializer = new TemplateProjectInitializer();
            var templaterFactory = new TemplaterFactory(
                new CliConfigurationPathsOptions(home),
                new InstalledProjectStackCatalog([stack]),
                new PhysicalFileSystem());
            var command = new InitCommand(
                interaction,
                Substitute.For<IWorkloadHintRenderer>(),
                localSettings,
                projectResolver,
                bundleReader,
                new InstalledBundleScanner(bundles),
                new InstalledProjectStackCatalog([stack]),
                templaterFactory,
                new ProjectFileWriter(new WindowsProjectFileSystem()),
                [initializer]);
            RootCommand rootCommand = [command];

            int exitCode = await rootCommand.Parse(["init", target, "--stack", "dotnet", "--language", "C#", "--name", "MyApp"])
                .InvokeAsync();

            Assert.Equal(0, exitCode);
            Assert.True(restoreObserved);
            Assert.False(initializer.WasInvoked);
            Assert.True(File.Exists(Path.Combine(target, "MyApp.csproj")));
            Assert.True(File.Exists(Path.Combine(target, "host.json")));
            using var configuration = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(target, ".func", "config.json")));
            Assert.Equal("dotnet", configuration.RootElement.GetProperty("stack").GetProperty("runtime").GetString());
            Assert.Equal("c#", configuration.RootElement.GetProperty("stack").GetProperty("language").GetString());
            await tasks.Received(1).ExecuteAsync(
                Arg.Any<WorkingDirectory>(), _restoreAction, Arg.Any<IReadOnlyDictionary<string, string>>(), Arg.Any<CancellationToken>());
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [WindowsX64TemplateSourceFact]
    [Trait("Category", "Integration")]
    public async Task FuncCommands_InitAndNew_UseRegisteredPackagesAndBuildProject()
    {
        string root = Path.Combine(Path.GetTempPath(), $"func-dotnet-template-journey-{Guid.NewGuid():N}");
        string target = Path.Combine(root, "nested", "project");
        string home = Path.Combine(root, "home");
        string workloadsHome = Path.Combine(root, "workloads");
        Directory.CreateDirectory(root);
        using CancellationTokenSource cancellation = new(TimeSpan.FromMinutes(8));
        try
        {
            string nugetPackages = GetBuildMetadata("NuGetPackageRoot");
            Dictionary<string, string> environment = new(StringComparer.OrdinalIgnoreCase)
            {
                ["DOTNET_GENERATE_ASPNET_CERTIFICATE"] = "false",
                ["NUGET_PACKAGES"] = nugetPackages,
                ["RestoreSources"] = nugetPackages,
                ["NuGetAudit"] = "false"
            };

            string workloadPackage = await PackDotNetWorkloadAsync(root, nugetPackages, cancellation.Token);
            CliProcessTestRunner.CliResult install = await _processRunner.RunAsync(
                root,
                home,
                workloadsHome,
                root,
                ["workload", "install", workloadPackage, "--allow-untrusted"],
                cancellation.Token,
                environment);
            Assert.True(install.ExitCode == 0, $"Installing the packaged .NET workload failed ({install.ExitCode}). stdout: {install.StandardOutput}{Environment.NewLine}stderr: {install.StandardError}");
            (string Identifier, string Version, string Hash)[] registeredPackages =
                await ReadRegisteredTemplatePackagesAsync(home, cancellation.Token);
            StageHostWorkload(workloadsHome);

            CliProcessTestRunner.CliResult init = await _processRunner.RunAsync(
                root,
                home,
                workloadsHome,
                root,
                ["init", target, "--stack", "dotnet", "--language", "c#", "--name", "MyApp"],
                cancellation.Token,
                environment);
            Assert.True(init.ExitCode == 0, $"func init failed ({init.ExitCode}). stdout: {init.StandardOutput}{Environment.NewLine}stderr: {init.StandardError}");
            Assert.Equal(registeredPackages, await ReadRegisteredTemplatePackagesAsync(home, cancellation.Token));
            Assert.True(File.Exists(Path.Combine(target, "MyApp.csproj")));
            Assert.True(File.Exists(Path.Combine(target, ".func", "config.json")));
            await ReportProjectInputEvidenceAsync(root, target, cancellation.Token);

            CliProcessTestRunner.CliResult create = await _processRunner.RunAsync(
                root,
                home,
                workloadsHome,
                root,
                ["new", target, "--template", "Azure.Function.CSharp.Isolated.HttpTrigger.3.x", "--name", "HttpExample", "--non-interactive"],
                cancellation.Token,
                environment);
            Assert.True(create.ExitCode == 0, $"func new failed ({create.ExitCode}). stdout: {create.StandardOutput}{Environment.NewLine}stderr: {create.StandardError}");
            Assert.Equal(registeredPackages, await ReadRegisteredTemplatePackagesAsync(home, cancellation.Token));
            Assert.True(File.Exists(Path.Combine(target, "HttpExample.cs")));

            (int ExitCode, string StandardOutput, string StandardError) build = await RunDotnetBuildAsync(
                target,
                Path.Combine(root, CliProcessTestRunner.StateDirectoryName, "dotnet_cli_home"),
                cancellation.Token);
            Assert.True(build.ExitCode == 0, $"dotnet build failed ({build.ExitCode}). stdout: {build.StandardOutput}{Environment.NewLine}stderr: {build.StandardError}");

            await RunHostAndInvokeHttpAsync(root, target, home, workloadsHome, environment, cancellation.Token);
            Assert.Equal(registeredPackages, await ReadRegisteredTemplatePackagesAsync(home, cancellation.Token));
        }
        finally
        {
            await DeleteDirectoryAsync(root);
        }
    }

    private async Task RunHostAndInvokeHttpAsync(
        string root,
        string target,
        string home,
        string workloadsHome,
        IReadOnlyDictionary<string, string> environment,
        CancellationToken cancellationToken)
    {
        int port;
        using (var listener = new TcpListener(IPAddress.Loopback, 0))
        {
            listener.Start();
            port = ((IPEndPoint)listener.LocalEndpoint).Port;
        }

        var hostEnvironment = environment.ToDictionary(
            pair => pair.Key,
            pair => pair.Value,
            StringComparer.OrdinalIgnoreCase);
        hostEnvironment["AzureWebJobsStorage"] = string.Empty;
        hostEnvironment["AzureWebJobsSecretStorageType"] = "files";
        var startInfo = _processRunner.CreateStartInfo(
            root,
            home,
            workloadsHome,
            target,
            ["start", "--no-build", "--port", port.ToString(CultureInfo.InvariantCulture)],
            hostEnvironment);
        using Process process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Could not start the Functions Host CLI process.");
        process.StandardInput.Close();
        Task<string> standardOutput = process.StandardOutput.ReadToEndAsync();
        Task<string> standardError = process.StandardError.ReadToEndAsync();
        string secretDirectory = Path.Combine(
            startInfo.Environment["TEMP"] ?? throw new InvalidOperationException("The CLI test process has no isolated TEMP directory."),
            "secrets", "functions", "secrets");
        bool invoked = false;
        string? lastHttpFailure = null;

        try
        {
            using HttpClient client = new(new HttpClientHandler { UseProxy = false }) { Timeout = TimeSpan.FromSeconds(2) };
            DateTime deadline = DateTime.UtcNow.AddSeconds(30);
            while (DateTime.UtcNow < deadline && !cancellationToken.IsCancellationRequested)
            {
                if (process.HasExited)
                {
                    break;
                }

                string? key = TryReadFunctionKey(secretDirectory);
                string requestUri = $"http://127.0.0.1:{port}/api/HttpExample";
                if (key is not null)
                {
                    requestUri += $"?code={Uri.EscapeDataString(key)}";
                }

                try
                {
                    using HttpResponseMessage response = await client.GetAsync(requestUri, cancellationToken);
                    if (response.IsSuccessStatusCode)
                    {
                        string body = await response.Content.ReadAsStringAsync(cancellationToken);
                        Assert.Contains("Welcome to Azure Functions!", body, StringComparison.Ordinal);
                        invoked = true;
                        break;
                    }

                    lastHttpFailure = $"{(int)response.StatusCode} {response.StatusCode}";
                }
                catch (HttpRequestException exception)
                {
                    lastHttpFailure = exception.Message;
                }
                catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
                {
                    lastHttpFailure = "HTTP request timed out";
                }

                await Task.Delay(TimeSpan.FromMilliseconds(250), cancellationToken);
            }
        }
        finally
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
                await process.WaitForExitAsync(CancellationToken.None);
            }

            await Task.WhenAll(standardOutput, standardError);
            output.WriteLine("Functions Host stdout: {0}", await standardOutput);
            output.WriteLine("Functions Host stderr: {0}", await standardError);
            output.WriteLine("Functions Host secret files: {0}",
                Directory.Exists(secretDirectory)
                    ? string.Join(", ", Directory.EnumerateFiles(secretDirectory, "*.json", SearchOption.AllDirectories)
                        .Select(file => Path.GetRelativePath(secretDirectory, file)))
                    : "<none>");
        }

        Assert.True(invoked, $"The generated HTTP function was not invoked successfully. Last HTTP response: {lastHttpFailure ?? "<none>"}.");
    }

    private static string? TryReadFunctionKey(string secretDirectory)
    {
        if (!Directory.Exists(secretDirectory))
        {
            return null;
        }

        foreach (string file in Directory.EnumerateFiles(secretDirectory, "*.json", SearchOption.AllDirectories)
            .Where(file => string.Equals(Path.GetFileName(file), "HttpExample.json", StringComparison.OrdinalIgnoreCase)
                || string.Equals(Path.GetFileName(file), "host.json", StringComparison.OrdinalIgnoreCase)))
        {
            using var document = JsonDocument.Parse(File.ReadAllText(file));
            JsonElement root = document.RootElement;
            if (root.TryGetProperty("keys", out JsonElement keys) && keys.ValueKind == JsonValueKind.Array)
            {
                foreach (JsonElement entry in keys.EnumerateArray())
                {
                    if (entry.TryGetProperty("value", out JsonElement value)
                        && value.ValueKind == JsonValueKind.String
                        && !string.IsNullOrWhiteSpace(value.GetString()))
                    {
                        return value.GetString();
                    }
                }
            }

            if (root.TryGetProperty("masterKey", out JsonElement masterKey)
                && masterKey.ValueKind == JsonValueKind.String
                && !string.IsNullOrWhiteSpace(masterKey.GetString()))
            {
                return masterKey.GetString();
            }
        }

        return null;
    }

    private static async Task DeleteDirectoryAsync(string path)
    {
        for (int attempt = 0; ; attempt++)
        {
            try
            {
                if (Directory.Exists(path))
                {
                    Directory.Delete(path, recursive: true);
                }

                return;
            }
            catch (IOException) when (attempt < 5)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(200 * (attempt + 1)));
            }
            catch (UnauthorizedAccessException) when (attempt < 5)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(200 * (attempt + 1)));
            }
        }
    }

    private static string GetBuildMetadata(string key)
    {
        string? value = typeof(InitCommandTemplateTests).Assembly
            .GetCustomAttributes<AssemblyMetadataAttribute>()
            .SingleOrDefault(attribute => string.Equals(attribute.Key, key, StringComparison.Ordinal))
            ?.Value;
        return !string.IsNullOrWhiteSpace(value)
            ? value
            : throw new InvalidOperationException($"The test build did not provide the '{key}' property.");
    }

    private static async Task<string> PackDotNetWorkloadAsync(
        string root,
        string nugetPackages,
        CancellationToken cancellationToken)
    {
        string repositoryRoot = GetBuildMetadata("RepositoryRoot");
        string projectPath = Path.Combine(repositoryRoot, "src", "Workloads", "Stacks", "DotNet", "Workloads.DotNet.csproj");
        string outputDirectory = Path.Combine(root, "dotnet-workload-package");
        Directory.CreateDirectory(outputDirectory);

        ProcessStartInfo startInfo = new()
        {
            FileName = Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") ?? "dotnet",
            WorkingDirectory = repositoryRoot,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        startInfo.ArgumentList.Add("pack");
        startInfo.ArgumentList.Add(projectPath);
        startInfo.ArgumentList.Add("--configuration");
        startInfo.ArgumentList.Add("debug");
        startInfo.ArgumentList.Add("--output");
        startInfo.ArgumentList.Add(outputDirectory);
        ConfigureDotnetEnvironment(startInfo, root, nugetPackages);

        using Process process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Could not start dotnet pack for the .NET workload.");
        Task<string> standardOutput = process.StandardOutput.ReadToEndAsync(cancellationToken);
        Task<string> standardError = process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken);
        await Task.WhenAll(standardOutput, standardError);
        string stdout = await standardOutput;
        string stderr = await standardError;
        Assert.True(process.ExitCode == 0, $"Packing the .NET workload failed. stdout: {stdout}{Environment.NewLine}stderr: {stderr}");

        string[] packages = [.. Directory.EnumerateFiles(outputDirectory, "*.nupkg", SearchOption.TopDirectoryOnly)];
        return Assert.Single(packages);
    }

    private static void ConfigureDotnetEnvironment(ProcessStartInfo startInfo, string root, string nugetPackages)
    {
        startInfo.Environment.Clear();
        foreach (string key in new[]
        {
            "PATH", "SystemRoot", "WINDIR", "ProgramFiles", "ProgramFiles(x86)", "ProgramW6432",
            "DOTNET_ROOT", "DOTNET_ROOT_X64", "DOTNET_ROOT_X86", "DOTNET_ROOT_ARM64", "DOTNET_ROOT(x86)"
        })
        {
            if (Environment.GetEnvironmentVariable(key) is { } value)
            {
                startInfo.Environment[key] = value;
            }
        }

        string stateRoot = Path.Combine(root, CliProcessTestRunner.StateDirectoryName, "dotnet-pack");
        Directory.CreateDirectory(stateRoot);
        string temporaryRoot = Path.Combine(stateRoot, "temp");
        string home = Path.Combine(stateRoot, "home");
        string appData = Path.Combine(stateRoot, "appdata");
        string localAppData = Path.Combine(stateRoot, "localappdata");
        Directory.CreateDirectory(temporaryRoot);
        Directory.CreateDirectory(home);
        Directory.CreateDirectory(appData);
        Directory.CreateDirectory(localAppData);
        startInfo.Environment["HOME"] = home;
        startInfo.Environment["USERPROFILE"] = home;
        startInfo.Environment["APPDATA"] = appData;
        startInfo.Environment["LOCALAPPDATA"] = localAppData;
        startInfo.Environment["DOTNET_CLI_HOME"] = home;
        startInfo.Environment["DOTNET_GENERATE_ASPNET_CERTIFICATE"] = "false";
        startInfo.Environment["DOTNET_CLI_TELEMETRY_OPTOUT"] = "1";
        startInfo.Environment["DOTNET_SKIP_FIRST_TIME_EXPERIENCE"] = "1";
        startInfo.Environment["DOTNET_NOLOGO"] = "1";
        startInfo.Environment["NUGET_PACKAGES"] = nugetPackages;
        startInfo.Environment["RestoreSources"] = nugetPackages;
        startInfo.Environment["NuGetAudit"] = "false";
        startInfo.Environment["TEMP"] = temporaryRoot;
        startInfo.Environment["TMP"] = temporaryRoot;
    }

    private static void StageHostWorkload(string workloadsHome)
    {
        WorkloadPathsOptions paths = new(workloadsHome);
        WorkloadRegistry registry = JsonSerializer.Deserialize(
            File.ReadAllText(paths.WorkloadRegistryPath),
            WorkloadJsonContext.Default.WorkloadRegistry)
            ?? throw new InvalidDataException("The workload installer did not create its workload registry.");
        registry.Workloads.Add(CreateHostWorkload(workloadsHome));
        File.WriteAllText(paths.WorkloadRegistryPath, JsonSerializer.Serialize(registry, WorkloadJsonContext.Default.WorkloadRegistry));
    }

    private static WorkloadEntry CreateHostWorkload(string workloadsHome)
    {
        const string packageVersion = "4.1054.200";
        string hostOutputRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "Workloads.Host"));
        string hostOutput = new[] { "debug", "debug_win-x64" }
            .Select(configuration => Path.Combine(hostOutputRoot, configuration))
            .FirstOrDefault(path => File.Exists(Path.Combine(path, "Azure.Functions.Cli.Workloads.Host.exe")))
            ?? throw new DirectoryNotFoundException($"The built Host workload output was not found under '{hostOutputRoot}'.");
        string installDirectory = new WorkloadPathsOptions(workloadsHome).GetInstallDirectory(HostWorkloadPackage.CurrentPackageId, packageVersion);
        string contentRoot = Path.Combine(installDirectory, "tools", "any");
        Directory.CreateDirectory(contentRoot);
        foreach (string file in Directory.EnumerateFiles(hostOutput, "*", SearchOption.AllDirectories)
            .Where(file => !string.Equals(Path.GetExtension(file), ".pdb", StringComparison.OrdinalIgnoreCase)))
        {
            string destination = Path.Combine(contentRoot, Path.GetRelativePath(hostOutput, file));
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            File.Copy(file, destination, overwrite: true);
        }

        return new WorkloadEntry
        {
            PackageId = HostWorkloadPackage.CurrentPackageId,
            PackageVersion = packageVersion,
            Kind = WorkloadKind.Content,
            Aliases = ["host"],
            DisplayName = "Azure Functions host",
            Description = "Isolated local Host workload for the template journey."
        };
    }

    private static async Task<(int ExitCode, string StandardOutput, string StandardError)> RunDotnetBuildAsync(
        string target,
        string dotnetCliHome,
        CancellationToken cancellationToken)
    {
        ProcessStartInfo startInfo = new()
        {
            FileName = "dotnet",
            WorkingDirectory = target,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        startInfo.ArgumentList.Add("build");
        startInfo.ArgumentList.Add(Path.Combine(target, "MyApp.csproj"));
        startInfo.ArgumentList.Add("--no-restore");
        ConfigureDotnetEnvironment(startInfo, Path.GetDirectoryName(dotnetCliHome)!, GetBuildMetadata("NuGetPackageRoot"));
        startInfo.Environment["DOTNET_CLI_HOME"] = dotnetCliHome;

        using Process process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Could not start dotnet build for the generated project.");
        Task<string> standardOutput = process.StandardOutput.ReadToEndAsync(cancellationToken);
        Task<string> standardError = process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken);
        await Task.WhenAll(standardOutput, standardError);
        return (process.ExitCode, await standardOutput, await standardError);
    }

    private async Task ReportProjectInputEvidenceAsync(string root, string target, CancellationToken cancellationToken)
    {
        ProcessStartInfo startInfo = new()
        {
            FileName = "dotnet",
            WorkingDirectory = target,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        startInfo.ArgumentList.Add("msbuild");
        startInfo.ArgumentList.Add(Path.Combine(target, "MyApp.csproj"));
        startInfo.ArgumentList.Add("-getProperty:MSBuildAllProjects,TargetFramework,RootNamespace,OutputType,AzureFunctionsVersion");
        startInfo.ArgumentList.Add("-getItem:PackageReference");
        ConfigureDotnetEnvironment(startInfo, root, GetBuildMetadata("NuGetPackageRoot"));

        using Process process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Could not start MSBuild to capture the representative project inputs.");
        Task<string> standardOutput = process.StandardOutput.ReadToEndAsync(cancellationToken);
        Task<string> standardError = process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken);
        await Task.WhenAll(standardOutput, standardError);
        string stdout = await standardOutput;
        string stderr = await standardError;
        Assert.True(process.ExitCode == 0, $"MSBuild project-input evaluation failed. stdout: {stdout}{Environment.NewLine}stderr: {stderr}");

        using var document = JsonDocument.Parse(stdout);
        string reported = document.RootElement.GetProperty("Properties").GetProperty("MSBuildAllProjects").GetString() ?? string.Empty;
        string[] reportedPaths = [.. reported.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)];
        string[] inputs = [.. reportedPaths
            .Select(path => Path.GetFullPath(path, target))
            .Order(StringComparer.OrdinalIgnoreCase)];
        string projectPath = Path.GetFullPath(Path.Combine(target, "MyApp.csproj"));
        bool rootWasReported = inputs.Contains(projectPath, StringComparer.OrdinalIgnoreCase);
        if (!rootWasReported)
        {
            inputs = [.. inputs, projectPath];
        }

        Assert.Equal(inputs.Length, inputs.Distinct(StringComparer.OrdinalIgnoreCase).Count());
        int projectFiles = inputs.Count(path => Path.GetExtension(path).Equals(".csproj", StringComparison.OrdinalIgnoreCase));
        int propertyFiles = inputs.Count(path => Path.GetExtension(path).Equals(".props", StringComparison.OrdinalIgnoreCase));
        int targetFiles = inputs.Count(path => Path.GetExtension(path).Equals(".targets", StringComparison.OrdinalIgnoreCase));
        int otherFiles = inputs.Length - projectFiles - propertyFiles - targetFiles;
        output.WriteLine(
            "Representative .NET evaluated inputs: {0} total ({1} .csproj, {2} .props, {3} .targets, {4} other); MSBuild reported {5}; root project included after known-root completion: {6}; rejection rule: empty, over 256, or missing root after completion.",
            inputs.Length, projectFiles, propertyFiles, targetFiles, otherFiles,
            reportedPaths.Length, !rootWasReported || inputs.Contains(projectPath, StringComparer.OrdinalIgnoreCase));
        Assert.InRange(inputs.Length, 1, 256);
        Assert.Contains(projectPath, inputs, StringComparer.OrdinalIgnoreCase);
    }

    private static string PackagePath(string name)
        => Path.Combine(AppContext.BaseDirectory, "Templates", "Fixtures", "DotNet", $"{name}.nupkg");

    private static async Task<(string Identifier, string Version, string Hash)[]> ReadRegisteredTemplatePackagesAsync(
        string home,
        CancellationToken cancellationToken)
    {
        TemplateEngineContext context = new(WorkingDirectory.FromExplicit(home));
        using TemplateEngineSession session = new(context, Path.Combine(home, "templates"));
        IReadOnlyList<ITemplatePackage> installed = await session.PackageManager.GetTemplatePackagesAsync(
            force: false,
            cancellationToken);
        IManagedTemplatePackage[] packages = [.. installed.OfType<IManagedTemplatePackage>()];
        Assert.Equal(2, packages.Length);
        Assert.Contains(packages, package =>
            package.Identifier == "Microsoft.Azure.Functions.Worker.ProjectTemplates" && package.Version == "4.0.5708");
        Assert.Contains(packages, package =>
            package.Identifier == "Microsoft.Azure.Functions.Worker.ItemTemplates" && package.Version == "4.0.5708");

        var snapshot = new List<(string Identifier, string Version, string Hash)>(packages.Length);
        foreach (IManagedTemplatePackage package in packages.OrderBy(package => package.Identifier, StringComparer.Ordinal))
        {
            await using FileStream stream = File.OpenRead(package.MountPointUri);
            string hash = Convert.ToHexStringLower(await SHA256.HashDataAsync(stream, cancellationToken));
            snapshot.Add((
                package.Identifier,
                package.Version ?? throw new InvalidDataException("A registered template package has no version."),
                hash));
        }

        return [.. snapshot];
    }

    private sealed class TemplateProjectInitializer : IProjectInitializer
    {
        public string Stack => "dotnet";

        public IReadOnlyList<string> SupportedLanguages => ["C#", "F#"];

        public IReadOnlyDictionary<string, IReadOnlyList<string>> SupportedLanguageAliases { get; } =
            new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase)
            {
                ["C#"] = ["csharp"],
                ["F#"] = ["fsharp"]
            };

        public bool WasInvoked { get; private set; }

        public IReadOnlyList<Option> GetInitOptions(IInitOptionRegistry registry) => [];

        public ProjectTemplateRequest GetTemplateRequest(InitContext context, ParseResult parseResult)
            => new(ProjectTemplateIdentity, new Dictionary<string, string> { ["Framework"] = "net10.0" });

        public Task InitializeAsync(InitContext context, ParseResult parseResult, CancellationToken cancellationToken = default)
        {
            WasInvoked = true;
            return Task.CompletedTask;
        }
    }

    private sealed class TestProject(WorkingDirectory workingDirectory) : FunctionsProject
    {
        public override WorkingDirectory WorkingDirectory { get; } = workingDirectory;

        public override string StackName => "dotnet";

        public override string StackDisplayName => ".NET";

        public override bool SupportsExtensionBundles => true;

        public override FunctionsWorkerReference WorkerReference => FunctionsWorkerReference.FromWorkload("dotnet-isolated");
    }
}
