// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using AwesomeAssertions;
using Azure.Functions.Cli.E2ETests.Helpers;
using Azure.Functions.Cli.E2ETests.Traits;
using Azure.Functions.Cli.TestFramework;
using Azure.Functions.Cli.TestFramework.Assertions;
using Azure.Functions.Cli.TestFramework.Commands;
using Azure.Functions.Cli.TestFramework.Helpers;
using Xunit;
using Xunit.Abstractions;

namespace Azure.Functions.Cli.E2ETests.Commands
{
    [Trait(WorkerRuntimeTraits.WorkerRuntime, WorkerRuntimeTraits.DotnetIsolated)]
    public class Dotnet11IsolatedTests(ITestOutputHelper log) : BaseE2ETests(log)
    {
        private const string HttpFunction = "HttpTrigger";
        private const string TimerFunction = "TimerTrigger";
        private const string TimerSuccessLog = "Executed 'Functions.TimerTrigger' (Succeeded";

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void Start_Net11_HttpAndTimerFunctionsExecute(bool useDotnetRun)
        {
            var testName = $"{nameof(Start_Net11_HttpAndTimerFunctionsExecute)}_{useDotnetRun}";
            ScaffoldProject(testName);
            var port = ProcessHelper.GetAvailablePort();
            var command = CreateCommand(useDotnetRun ? Dotnet11TestHelper.DotnetPath : FuncPath, testName);
            var timerExecuted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            string? response = null;

            command.CommandOutputHandler = line =>
            {
                if (line.Contains(TimerSuccessLog, StringComparison.Ordinal))
                {
                    timerExecuted.TrySetResult();
                }
            };
            command.ProcessStartedHandler = async process =>
            {
                try
                {
                    var writer = command.FileWriter ?? throw new InvalidOperationException("Process log is unavailable.");
                    await ProcessHelper.WaitForFunctionHostToStart(process, port, writer);
                    await timerExecuted.Task.WaitAsync(TimeSpan.FromSeconds(30));
                    response = await ProcessHelper.ProcessStartedHandlerHelper(port, process, writer, HttpFunction);
                }
                finally
                {
                    if (!process.HasExited)
                    {
                        process.Kill(true);
                    }
                }
            };

            var args = useDotnetRun
                ? new[] { "run", "--", "--port", port.ToString() }
                : new[] { "start", "--verbose", "--port", port.ToString() };
            var result = command.Execute(args);

            response.Should().Be("Welcome to Azure Functions!");
            timerExecuted.Task.IsCompletedSuccessfully.Should().BeTrue("the timer extension must execute, not merely index");
            result.Should().HaveStdOutContaining(TimerSuccessLog);
        }

        [Fact]
        public void Pack_Net11_IncludesWorkerAndExtensions()
        {
            var testName = nameof(Pack_Net11_IncludesWorkerAndExtensions);
            var assemblyName = ScaffoldProject(testName);
            var packageDirectory = Path.Combine(WorkingDirectory, "packages");

            var result = CreateCommand(FuncPath, testName)
                .Execute(["pack", "--output", packageDirectory]);

            result.Should().ExitWith(0);
            result.Should().HaveStdOutContaining("Building .NET project...");
            AssertPackage(packageDirectory, assemblyName);
        }

        [Fact]
        public void Pack_Net11_NoBuild_AcceptsAzurePublishBuildOutput()
        {
            var testName = nameof(Pack_Net11_NoBuild_AcceptsAzurePublishBuildOutput);
            var assemblyName = ScaffoldProject(testName);
            var testAssembly = typeof(Dotnet11IsolatedTests).Assembly.Location;
            var dotnet10Path = Path.GetFullPath(Path.Combine(
                RuntimeEnvironment.GetRuntimeDirectory(),
                "..", "..", "..",
                OperatingSystem.IsWindows() ? "dotnet.exe" : "dotnet"));

            // Use the test host's .NET 10 runtime for the runner; only its child builds select SDK 11.
            var buildResult = CreateCommand(dotnet10Path, testName)
                .Execute([
                    "exec",
                    "--runtimeconfig", Path.ChangeExtension(testAssembly, ".runtimeconfig.json"),
                    "--depsfile", Path.ChangeExtension(testAssembly, ".deps.json"),
                    typeof(DotnetCompatibilityRunner).Assembly.Location,
                    WorkingDirectory
                ]);

            buildResult.Should().ExitWith(0);
            buildResult.Should().HaveStdOutContaining("Detected target framework: net11.0");
            var publishDirectory = Path.Combine(WorkingDirectory, "bin", "publish");
            File.Exists(Path.Combine(publishDirectory, "worker.config.json")).Should().BeTrue();
            File.Exists(Path.Combine(publishDirectory, "functions.metadata")).Should().BeFalse();

            var packageDirectory = Path.Combine(WorkingDirectory, "packages");
            var result = CreateCommand(FuncPath, testName)
                .Execute(["pack", publishDirectory, "--no-build", "--output", packageDirectory]);

            result.Should().ExitWith(0);
            result.Should().HaveStdOutContaining("Skipping build event for functions project (--no-build).");
            result.Should().NotHaveStdOutContaining("Building .NET project...");
            AssertPackage(packageDirectory, assemblyName);
        }

        private string ScaffoldProject(string testName)
        {
            var initResult = CreateCommand(FuncPath, testName)
                .Execute(["init", "--worker-runtime", "dotnet-isolated", "--target-framework", "net11.0", "--language", "C#"]);
            initResult.Should().ExitWith(0);

            var projectFile = Assert.Single(Directory.GetFiles(WorkingDirectory, "*.csproj"));
            var project = XDocument.Load(projectFile);
            project.Descendants("TargetFramework").Single().Value.Should().Be("net11.0");
            project.Root!.Attribute("Sdk")!.Value.Should().StartWith("Azure.Functions.Sdk/");

            foreach (var (template, name) in new[] { ("HttpTrigger", HttpFunction), ("TimerTrigger", TimerFunction) })
            {
                var newResult = CreateCommand(FuncPath, testName)
                    .Execute(["new", "--template", template, "--name", name]);
                newResult.Should().ExitWith(0);
            }

            var timerFile = Path.Combine(WorkingDirectory, $"{TimerFunction}.cs");
            var timerSource = File.ReadAllText(timerFile);
            var trigger = new Regex("""\[TimerTrigger\("[^"]*"\)\]""");
            trigger.IsMatch(timerSource).Should().BeTrue("the scaffolded timer must be configured to execute during the test");
            File.WriteAllText(timerFile, trigger.Replace(
                timerSource,
                """[TimerTrigger("0 0 0 1 1 *", RunOnStartup = true, UseMonitor = false)]"""));

            return project.Descendants("AssemblyName").FirstOrDefault()?.Value ?? Path.GetFileNameWithoutExtension(projectFile);
        }

        private FuncRootCommand CreateCommand(string executable, string testName)
        {
            var command = new FuncRootCommand(executable, testName, Log);
            command.WithWorkingDirectory(WorkingDirectory);
            Dotnet11TestHelper.ConfigureCommand(command, FuncPath);
            return command;
        }

        private static void AssertPackage(string packageDirectory, string assemblyName)
        {
            using var archive = ZipFile.OpenRead(Assert.Single(Directory.GetFiles(packageDirectory, "*.zip")));
            var paths = archive.Entries.Select(entry => entry.FullName.Replace('\\', '/')).ToArray();
            paths.Should().Contain([
                "host.json",
                "worker.config.json",
                $"{assemblyName}.dll",
                $"{assemblyName}.deps.json",
                $"{assemblyName}.runtimeconfig.json",
                "extensions.json"
            ]);
            paths.Should().Contain(path => path.StartsWith(".azurefunctions/", StringComparison.Ordinal) && path.EndsWith(".dll", StringComparison.Ordinal));
            paths.Should().NotContain("functions.metadata", "Azure.Functions.Sdk uses worker indexing");
            paths.Should().NotContain("local.settings.json");

            using var configStream = archive.GetEntry("worker.config.json")!.Open();
            using var config = JsonDocument.Parse(configStream);
            config.RootElement.GetProperty("description").GetProperty("language").GetString().Should().Be("dotnet-isolated");
            config.RootElement.GetProperty("description").GetProperty("defaultWorkerPath").GetString().Should().Be($"{assemblyName}.dll");
        }
    }
}
