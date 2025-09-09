// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.E2ETests.Traits;
using Xunit;
using Xunit.Abstractions;

namespace Azure.Functions.Cli.E2ETests.Commands.FuncPack
{
    [Trait(WorkerRuntimeTraits.WorkerRuntime, WorkerRuntimeTraits.Dotnet)]
    public class DotnetInProc6PackTests : BaseE2ETests
    {
        public DotnetInProc6PackTests(ITestOutputHelper log)
            : base(log)
        {
        }

        private string Dotnet6ProjectPath => Path.Combine(TestProjectDirectory, "TestNet6InProcProject");

        [Fact]
        public void Pack_Dotnet6InProc_WorksAsExpected()
        {
            var testName = nameof(Pack_Dotnet6InProc_WorksAsExpected);

            var logsToValidate = new[]
            {
                "Building .NET project...",
                "Determining projects to restore..."
            };

            BasePackTests.TestBasicPackFunctionality(
                Dotnet6ProjectPath,
                testName,
                FuncPath,
                Log,
                new[]
                {
                    "host.json",
                    Path.Combine("bin", "extensions.json"),
                    Path.Combine("bin", "function.deps.json"),
                    Path.Combine("bin", "Microsoft.Azure.WebJobs.Host.Storage.dll"),
                    Path.Combine("bin", "Microsoft.WindowsAzure.Storage.dll"),
                    Path.Combine("bin", "TestNet6InProcProject.dll"),
                    Path.Combine("bin", "TestNet6InProcProject.pdb"),
                    Path.Combine("Dotnet6InProc", "function.json"),
                    Path.Combine("bin", "runtimes", "browser", "lib", "net6.0", "System.Text.Encodings.Web.dll")
                },
                logsToValidate);
        }

        [Fact]
        public async Task Pack_Dotnet6InProc_CustomOutput_NoBuild()
        {
            var testName = nameof(Pack_Dotnet6InProc_CustomOutput_NoBuild);

            await BasePackTests.TestDotnetNoBuildCustomOutputPackFunctionality(
                Dotnet6ProjectPath,
                testName,
                FuncPath,
                Log,
                WorkingDirectory,
                new[]
                {
                    "host.json",
                    Path.Combine("bin", "extensions.json"),
                    Path.Combine("bin", "function.deps.json"),
                    Path.Combine("bin", "Microsoft.Azure.WebJobs.Host.Storage.dll"),
                    Path.Combine("bin", "Microsoft.WindowsAzure.Storage.dll"),
                    Path.Combine("bin", "TestNet6InProcProject.dll"),
                    Path.Combine("bin", "TestNet6InProcProject.pdb"),
                    Path.Combine("Dotnet6InProc", "function.json"),
                    Path.Combine("bin", "runtimes", "browser", "lib", "net6.0", "System.Text.Encodings.Web.dll")
                });
        }

        [Fact]
        public void Pack_Dotnet6InProc_WithRelativePathArgument_Works()
        {
            var testName = nameof(Pack_Dotnet6InProc_WithRelativePathArgument_Works);
            var projectName = "TestNet6InProcProject";
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
                    Path.Combine("bin", "extensions.json"),
                    Path.Combine("bin", "function.deps.json"),
                    Path.Combine("bin", "Microsoft.Azure.WebJobs.Host.Storage.dll"),
                    Path.Combine("bin", "Microsoft.WindowsAzure.Storage.dll"),
                    Path.Combine("bin", "TestNet6InProcProject.dll"),
                    Path.Combine("bin", "TestNet6InProcProject.pdb"),
                    Path.Combine("Dotnet6InProc", "function.json"),
                    Path.Combine("bin", "runtimes", "browser", "lib", "net6.0", "System.Text.Encodings.Web.dll")
                });
        }

        [Fact]
        public void Pack_Dotnet6InProc_WithAbsolutePathArgument_Works()
        {
            var testName = nameof(Pack_Dotnet6InProc_WithAbsolutePathArgument_Works);
            var projectAbs = Dotnet6ProjectPath;
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
                    Path.Combine("bin", "extensions.json"),
                    Path.Combine("bin", "function.deps.json"),
                    Path.Combine("bin", "Microsoft.Azure.WebJobs.Host.Storage.dll"),
                    Path.Combine("bin", "Microsoft.WindowsAzure.Storage.dll"),
                    Path.Combine("bin", "TestNet6InProcProject.dll"),
                    Path.Combine("bin", "TestNet6InProcProject.pdb"),
                    Path.Combine("Dotnet6InProc", "function.json"),
                    Path.Combine("bin", "runtimes", "browser", "lib", "net6.0", "System.Text.Encodings.Web.dll")
                });
        }

        [Fact]
        public void Pack_DotnetInProc6_WithDirectoryBuildProps_Works()
        {
            var testName = nameof(Pack_DotnetInProc6_WithDirectoryBuildProps_Works);
            var projectAbs = Dotnet6ProjectPath;
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
                    "host.json"
                },
                logsToValidate: logsToValidate);
        }

        [Fact]
        public void Pack_DotnetInProc6_WithDirectoryBuildProps_NoBuild_Works()
        {
            var testName = nameof(Pack_DotnetInProc6_WithDirectoryBuildProps_NoBuild_Works);
            var projectAbs = Dotnet6ProjectPath;
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
                    "host.json"
                },
                logsToValidate: logsToValidate);
        }
    }
}
