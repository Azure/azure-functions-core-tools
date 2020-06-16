using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Tests.E2E;
using Azure.Functions.Cli.Tests.E2E.Helpers;
using Microsoft.Azure.WebJobs.Script;
using System;
using System.IO;
using System.Threading.Tasks;
using Xunit;
using Xunit.Abstractions;

namespace Azure.Functions.Cli.Tests.ExtensionsTests
{
    public class InstallExtensionsTests : BaseE2ETest
    {
        public InstallExtensionsTests(ITestOutputHelper output) : base(output) { }

        [Fact]
        public Task try_install_with_no_valid_trigger()
        {
            return CliTester.Run(new RunConfiguration
            {
                Commands = new[] {
                    "init . --worker-runtime node --no-bundle",
                    "new --template HttpTrigger --name testfunc",
                    "extensions install"
                },
                OutputContains = new[]
                {
                    "No action performed because no functions in your app require extensions"
                },
                OutputDoesntContain = new[]
                {
                    "Restoring packages for"
                },
                CheckFiles = new[]
                {
                    new FileResult
                    {
                        Name = "extensions.csproj",
                        Exists = false
                    }
                }
            }, _output);
        }

        [Fact]
        public Task try_install_with_a_valid_trigger()
        {
            return CliTester.Run(new RunConfiguration
            {
                Commands = new[] {
                    "init . --worker-runtime node --no-bundle",
                    "new --template SendGrid --name testfunc",
                    "extensions install"
                },
                OutputContains = new[]
                {
                    "Restoring packages for",
                    "Build succeeded"
                },
                OutputDoesntContain = new[]
                {
                    "No action performed because no functions in your app require extensions"
                },
                CheckFiles = new[]
                {
                    new FileResult
                    {
                        Name = "extensions.csproj",
                        Exists = true,
                        ContentContains = new[]
                        {
                            "Microsoft.Azure.WebJobs.Script.ExtensionsMetadataGenerator",
                            "Microsoft.Azure.WebJobs.Extensions.SendGrid"
                        }
                    }
                },
                CommandTimeout = TimeSpan.FromMinutes(1)
            }, _output);
        }

        [Fact]
        public Task try_install_with_a_version()
        {
            return CliTester.Run(new RunConfiguration
            {
                Commands = new[] {
                    "init . --worker-runtime node --no-bundle",
                    "new --template SendGrid --name testfunc",
                    "extensions install",
                    "extensions install -p Microsoft.Azure.WebJobs.Extensions.Storage -v 3.0.8"
                },
                OutputContains = new[]
                {
                    "Restoring packages for",
                    "Build succeeded"
                },
                OutputDoesntContain = new[]
                {
                    "No action performed because no functions in your app require extensions"
                },
                CheckFiles = new[]
                {
                    new FileResult
                    {
                        Name = "extensions.csproj",
                        Exists = true,
                        ContentContains = new[]
                        {
                            "Microsoft.Azure.WebJobs.Script.ExtensionsMetadataGenerator",
                            "Microsoft.Azure.WebJobs.Extensions.SendGrid",
                            "Include=\"Microsoft.Azure.WebJobs.Extensions.Storage\" Version=\"3.0.8\""
                        }
                    }
                },
                CommandTimeout = TimeSpan.FromMinutes(1)
            }, _output);
        }
    }
}
