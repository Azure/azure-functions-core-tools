// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.E2ETests.Traits;
using Azure.Functions.Cli.TestFramework.Assertions;
using Azure.Functions.Cli.TestFramework.Commands;
using FluentAssertions;
using System.IO.Compression;
using Xunit;
using Xunit.Abstractions;

namespace Azure.Functions.Cli.E2ETests.Commands.FuncPack
{
    [Trait(WorkerRuntimeTraits.WorkerRuntime, WorkerRuntimeTraits.Dotnet)]
    public class DotnetInProc8PackTests : BaseE2ETests
    {
        public DotnetInProc8PackTests(ITestOutputHelper log)
            : base(log)
        {
        }

        private string Dotnet8ProjectPath => Path.Combine(TestProjectDirectory, "TestNet8InProcProject");

        [Fact]
        public void Pack_Dotnet8InProc_WorksAsExpected()
        {
            var testName = nameof(Pack_Dotnet8InProc_WorksAsExpected);

            var logsToValidate = new[]
            {
                "Building .NET project...",
                "Determining projects to restore..."
            };

            BasePackTests.TestBasicPackFunctionality(
                Dotnet8ProjectPath,
                testName,
                FuncPath,
                Log,
                new[]
                {
                    "host.json",
                    "Microsoft.AspNetCore.Authentication.Abstractions.dll",
                    "Microsoft.AspNetCore.Authentication.Core.dll",
                    "Microsoft.AspNetCore.Authorization.dll",
                    "Microsoft.AspNetCore.Authorization.Policy.dll",
                    "Microsoft.AspNetCore.Hosting.Abstractions.dll",
                    "Microsoft.AspNetCore.Hosting.Server.Abstractions.dll",
                    "Microsoft.AspNetCore.Http.Abstractions.dll",
                    "Microsoft.AspNetCore.Http.dll",
                    "Microsoft.AspNetCore.Http.Extensions.dll",
                    "Microsoft.AspNetCore.Http.Features.dll",
                    "Microsoft.AspNetCore.JsonPatch.dll",
                    "Microsoft.AspNetCore.Mvc.Abstractions.dll",
                    "Microsoft.AspNetCore.Mvc.Core.dll",
                    "Microsoft.AspNetCore.Mvc.Formatters.Json.dll",
                    "Microsoft.AspNetCore.Mvc.WebApiCompatShim.dll",
                    "Microsoft.AspNetCore.ResponseCaching.Abstractions.dll",
                    "Microsoft.AspNetCore.Routing.Abstractions.dll",
                    "Microsoft.AspNetCore.Routing.dll",
                    "Microsoft.AspNetCore.WebUtilities.dll",
                    "Microsoft.Azure.WebJobs.dll",
                    "Microsoft.Azure.WebJobs.Extensions.dll",
                    "Microsoft.Azure.WebJobs.Extensions.Http.dll",
                    "Microsoft.Azure.WebJobs.Host.dll",
                    "Microsoft.Azure.WebJobs.Host.Storage.dll",
                    "Microsoft.DotNet.PlatformAbstractions.dll",
                    "Microsoft.Extensions.Configuration.Abstractions.dll",
                    "Microsoft.Extensions.Configuration.Binder.dll",
                    "Microsoft.Extensions.Configuration.dll",
                    "Microsoft.Extensions.Configuration.EnvironmentVariables.dll",
                    "Microsoft.Extensions.Configuration.FileExtensions.dll",
                    "Microsoft.Extensions.Configuration.Json.dll",
                    "Microsoft.Extensions.DependencyInjection.Abstractions.dll",
                    "Microsoft.Extensions.DependencyInjection.dll",
                    "Microsoft.Extensions.DependencyModel.dll",
                    "Microsoft.Extensions.FileProviders.Abstractions.dll",
                    "Microsoft.Extensions.FileProviders.Physical.dll",
                    "Microsoft.Extensions.FileSystemGlobbing.dll",
                    "Microsoft.Extensions.Hosting.Abstractions.dll",
                    "Microsoft.Extensions.Hosting.dll",
                    "Microsoft.Extensions.Logging.Abstractions.dll",
                    "Microsoft.Extensions.Logging.Configuration.dll",
                    "Microsoft.Extensions.Logging.dll",
                    "Microsoft.Extensions.ObjectPool.dll",
                    "Microsoft.Extensions.Options.ConfigurationExtensions.dll",
                    "Microsoft.Extensions.Options.dll",
                    "Microsoft.Extensions.Primitives.dll",
                    "Microsoft.Net.Http.Headers.dll",
                    "Microsoft.WindowsAzure.Storage.dll",
                    "NCrontab.Signed.dll",
                    "Newtonsoft.Json.Bson.dll",
                    "Newtonsoft.Json.dll",
                    "System.Memory.Data.dll",
                    "System.Net.Http.Formatting.dll",
                    "TestNet8InProcProject.deps.json",
                    "TestNet8InProcProject.dll",
                    "TestNet8InProcProject.pdb",
                    Path.Combine("bin", "extensions.json"),
                    Path.Combine("bin", "function.deps.json"),
                    Path.Combine("bin", "Microsoft.Azure.WebJobs.Host.Storage.dll"),
                    Path.Combine("bin", "Microsoft.WindowsAzure.Storage.dll"),
                    Path.Combine("bin", "TestNet8InProcProject.dll"),
                    Path.Combine("bin", "TestNet8InProcProject.pdb"),
                    Path.Combine("Dotnet8InProc", "function.json")
                },
                logsToValidate);
        }

        [Fact]
        public async Task Pack_Dotnet8InProc_CustomOutput_NoBuild()
        {
            var testName = nameof(Pack_Dotnet8InProc_CustomOutput_NoBuild);

            await BasePackTests.TestNoBuildCustomOutputPackFunctionality(
                Dotnet8ProjectPath,
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
                    Path.Combine("bin", "TestNet8InProcProject.dll"),
                    Path.Combine("bin", "TestNet8InProcProject.pdb"),
                    Path.Combine("Dotnet8InProc", "function.json")
                });
        }

        [Fact]
        public void Pack_Dotnet8InProc_PreserveExecutables_SetsBit()
        {
            var testName = nameof(Pack_Dotnet8InProc_PreserveExecutables_SetsBit);
            var execRelativePath = "TurnThisExecutable";

            var packResult = new FuncPackCommand(FuncPath, testName, Log)
                .WithWorkingDirectory(Dotnet8ProjectPath)
                .Execute(["--preserve-executables", execRelativePath]);

            packResult.Should().ExitWith(0);

            var zipFiles = Directory.GetFiles(Dotnet8ProjectPath, "*.zip");
            Assert.True(zipFiles.Length > 0, $"No zip files found in {Dotnet8ProjectPath}");
            var zipPath = zipFiles.First();

            using (var archive = ZipFile.OpenRead(zipPath))
            {
                var entry = archive.Entries.FirstOrDefault(e => e.FullName.Replace('\\', '/').EndsWith(execRelativePath));
                entry.Should().NotBeNull();
                int permissions = (entry!.ExternalAttributes >> 16) & 0xFFFF;
                permissions.Should().Be(Convert.ToInt32("100777", 8));
            }

            File.Delete(zipPath);
        }
    }
}
