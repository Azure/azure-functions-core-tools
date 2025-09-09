// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.E2ETests.Traits;
using Xunit;
using Xunit.Abstractions;

namespace Azure.Functions.Cli.E2ETests.Commands.FuncPack
{
    [Trait(WorkerRuntimeTraits.WorkerRuntime, WorkerRuntimeTraits.DotnetIsolated)]
    public class DotnetIsolatedPackTests : BaseE2ETests
    {
        public DotnetIsolatedPackTests(ITestOutputHelper log)
            : base(log)
        {
        }

        private string DotnetIsolatedProjectPath => Path.Combine(TestProjectDirectory, "TestDotnet8IsolatedProject");

        [Fact]
        public void Pack_DotnetIsolated_WorksAsExpected()
        {
            var testName = nameof(Pack_DotnetIsolated_WorksAsExpected);

            var logsToValidate = new[]
            {
                "Building .NET project...",
                "Determining projects to restore..."
            };

            BasePackTests.TestBasicPackFunctionality(
                DotnetIsolatedProjectPath,
                testName,
                FuncPath,
                Log,
                new[]
                {
                    "Azure.Core.dll",
                    "Azure.Identity.dll",
                    "extensions.json",
                    "functions.metadata",
                    "Google.Protobuf.dll",
                    "Grpc.Core.Api.dll",
                    "Grpc.Net.Client.dll",
                    "Grpc.Net.ClientFactory.dll",
                    "Grpc.Net.Common.dll",
                    "host.json",
                    "Microsoft.AI.DependencyCollector.dll",
                    "Microsoft.AI.EventCounterCollector.dll",
                    "Microsoft.AI.PerfCounterCollector.dll",
                    "Microsoft.AI.ServerTelemetryChannel.dll",
                    "Microsoft.AI.WindowsServer.dll",
                    "Microsoft.ApplicationInsights.dll",
                    "Microsoft.ApplicationInsights.WorkerService.dll",
                    "Microsoft.Azure.Functions.Worker.ApplicationInsights.dll",
                    "Microsoft.Azure.Functions.Worker.Core.dll",
                    "Microsoft.Azure.Functions.Worker.dll",
                    "Microsoft.Azure.Functions.Worker.Extensions.Abstractions.dll",
                    "Microsoft.Azure.Functions.Worker.Extensions.Http.AspNetCore.dll",
                    "Microsoft.Azure.Functions.Worker.Extensions.Http.dll",
                    "Microsoft.Azure.Functions.Worker.Grpc.dll",
                    "Microsoft.Bcl.AsyncInterfaces.dll",
                    "Microsoft.Extensions.Configuration.Binder.dll",
                    "Microsoft.Extensions.Configuration.FileExtensions.dll",
                    "Microsoft.Extensions.Configuration.Json.dll",
                    "Microsoft.Extensions.Configuration.UserSecrets.dll",
                    "Microsoft.Extensions.DependencyInjection.Abstractions.dll",
                    "Microsoft.Extensions.DependencyInjection.dll",
                    "Microsoft.Extensions.Diagnostics.Abstractions.dll",
                    "Microsoft.Extensions.Diagnostics.dll",
                    "Microsoft.Extensions.Hosting.Abstractions.dll",
                    "Microsoft.Extensions.Hosting.dll",
                    "Microsoft.Extensions.Logging.Abstractions.dll",
                    "Microsoft.Extensions.Logging.ApplicationInsights.dll",
                    "Microsoft.Extensions.Logging.Configuration.dll",
                    "Microsoft.Extensions.Logging.Console.dll",
                    "Microsoft.Extensions.Logging.Debug.dll",
                    "Microsoft.Extensions.Logging.dll",
                    "Microsoft.Extensions.Logging.EventLog.dll",
                    "Microsoft.Extensions.Logging.EventSource.dll",
                    "Microsoft.Extensions.Options.dll",
                    "Microsoft.Identity.Client.dll",
                    "Microsoft.Identity.Client.Extensions.Msal.dll",
                    "Microsoft.IdentityModel.Abstractions.dll",
                    "Microsoft.Win32.SystemEvents.dll",
                    "System.ClientModel.dll",
                    "System.Configuration.ConfigurationManager.dll",
                    "System.Diagnostics.EventLog.dll",
                    "System.Diagnostics.PerformanceCounter.dll",
                    "System.Drawing.Common.dll",
                    "System.Memory.Data.dll",
                    "System.Security.Cryptography.ProtectedData.dll",
                    "System.Security.Permissions.dll",
                    "System.Windows.Extensions.dll",
                    "TestDotnet8IsolatedProject.deps.json",
                    "TestDotnet8IsolatedProject.dll",
                    "TestDotnet8IsolatedProject.pdb",
                    "TestDotnet8IsolatedProject.runtimeconfig.json",
                    "worker.config.json",
                    Path.Combine(".azurefunctions", "function.deps.json"),
                    Path.Combine(".azurefunctions", "Microsoft.Azure.Functions.Worker.Extensions.dll"),
                    Path.Combine(".azurefunctions", "Microsoft.Azure.Functions.Worker.Extensions.pdb"),
                    Path.Combine(".azurefunctions", "Microsoft.Azure.WebJobs.Extensions.FunctionMetadataLoader.dll"),
                    Path.Combine(".azurefunctions", "Microsoft.Azure.WebJobs.Host.Storage.dll"),
                    Path.Combine(".azurefunctions", "Microsoft.WindowsAzure.Storage.dll")
                },
                logsToValidate);
        }

        [Fact]
        public async Task Pack_DotnetIsolated_CustomOutput_NoBuild()
        {
            var testName = nameof(Pack_DotnetIsolated_CustomOutput_NoBuild);

            await BasePackTests.TestDotnetNoBuildCustomOutputPackFunctionality(
                DotnetIsolatedProjectPath,
                testName,
                FuncPath,
                Log,
                WorkingDirectory,
                new[]
                {
                    "Azure.Core.dll",
                    "Azure.Identity.dll",
                    "extensions.json",
                    "functions.metadata",
                    "host.json"
                });
        }

        [Fact]
        public void Pack_DotnetIsolated_WithRelativePathArgument_Works()
        {
            var testName = nameof(Pack_DotnetIsolated_WithRelativePathArgument_Works);
            var projectName = "TestDotnet8IsolatedProject";
            BasePackTests.TestPackWithPathArgument(
                funcInvocationWorkingDir: TestProjectDirectory,
                projectAbsoluteDir: Path.Combine(TestProjectDirectory, projectName),
                pathArgumentToPass: $"./{projectName}",
                testName: testName,
                funcPath: FuncPath,
                log: Log,
                filesToValidate: new[]
                {
                    "host.json",
                    "extensions.json"
                });
        }

        [Fact]
        public void Pack_DotnetIsolated_WithAbsolutePathArgument_Works()
        {
            var testName = nameof(Pack_DotnetIsolated_WithAbsolutePathArgument_Works);
            var projectAbs = DotnetIsolatedProjectPath;
            BasePackTests.TestPackWithPathArgument(
                funcInvocationWorkingDir: WorkingDirectory,
                projectAbsoluteDir: projectAbs,
                pathArgumentToPass: projectAbs,
                testName: testName,
                funcPath: FuncPath,
                log: Log,
                filesToValidate: new[]
                {
                    "host.json",
                    "extensions.json"
                });
        }

        [Fact]
        public void Pack_DotnetIsolated_WithDirectoryBuildProps_Works()
        {
            var testName = nameof(Pack_DotnetIsolated_WithDirectoryBuildProps_Works);
            var projectAbs = DotnetIsolatedProjectPath;
            var logsToValidate = new[]
            {
                "Building .NET project...",
                "Determining projects to restore...",
                Path.Combine(projectAbs, "customArtifacts")
            };

            BasePackTests.TestPackWithDirectoryBuildProps(
                projectAbsoluteDir: projectAbs,
                noBuild: false,
                testName: testName,
                funcPath: FuncPath,
                log: Log,
                filesToValidate: new[]
                {
                    "host.json",
                    "extensions.json"
                },
                logsToValidate: logsToValidate);
        }

        [Fact]
        public void Pack_DotnetIsolated_WithDirectoryBuildProps_NoBuild_Works()
        {
            var testName = nameof(Pack_DotnetIsolated_WithDirectoryBuildProps_NoBuild_Works);
            var projectAbs = DotnetIsolatedProjectPath;
            var logsToValidate = new[]
            {
                "Found ArtifactsPath within Directory.Build.props. Using as build output directory.",
                "Skipping build event for functions project (--no-build)."
            };
            BasePackTests.TestPackWithDirectoryBuildProps(
                projectAbsoluteDir: projectAbs,
                noBuild: true,
                testName: testName,
                funcPath: FuncPath,
                log: Log,
                filesToValidate: new[]
                {
                    "host.json",
                    "extensions.json"
                },
                logsToValidate: logsToValidate);
        }
    }
}
