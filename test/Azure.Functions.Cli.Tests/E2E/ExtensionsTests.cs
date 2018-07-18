using System;
using System.Threading.Tasks;
using Azure.Functions.Cli.Tests.E2E.Helpers;
using Xunit;
using Xunit.Abstractions;

namespace Azure.Functions.Cli.Tests.E2E
{
    public class ExtensionsTests : BaseE2ETest
    {
        public ExtensionsTests(ITestOutputHelper output) : base(output) { }

        [Fact]
        public Task add_extension_node_app()
        {
            return CliTester.Run(new RunConfiguration
            {
                Commands = new[]
                {
                    "init . --worker-runtime node --no-source-control",
                    "extensions install --package Microsoft.Azure.WebJobs.Extensions.DurableTask --version 1.2.2-beta3"
                },
                CheckFiles = new[]
                {
                    new FileResult
                    {
                        Name = "extensions.csproj",
                        ContentContains = new[]
                        {
                            "DurableTask",
                            "ExtensionsMetadataGenerator"
                        }
                    },
                    new FileResult
                    {
                        Name = "bin/extensions.json",
                        ContentContains = new[]
                        {
                            "extensions",
                            "DurableTaskExtension",
                            "PublicKeyToken"
                        }
                    }
                }
            }, _output);
        }

        [Fact]
        public Task add_extension_dotnet_app()
        {
            return CliTester.Run(new RunConfiguration
            {
                Commands = new[]
                {
                    "init . --worker-runtime dotnet --no-source-control",
                    "extensions install --package Microsoft.Azure.WebJobs.Extensions.DurableTask --version 1.2.2-beta3"
                },
                CheckFiles = new[]
                {
                    new FileResult
                    {
                        Name = "{currentDirName}.csproj",
                        ContentContains = new[]
                        {
                            "DurableTask"
                        }
                    },
                    new FileResult
                    {
                        Name = "extensions.csproj",
                        Exists = false
                    }
                }
            }, _output);
        }
    }
}