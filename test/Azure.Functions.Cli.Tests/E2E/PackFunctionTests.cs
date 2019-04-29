using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Tests.E2E.Helpers;
using System;
using System.IO;
using System.Threading.Tasks;
using Xunit;
using Xunit.Abstractions;

namespace Azure.Functions.Cli.Tests.E2E
{
    public class PackFunctionTests : BaseE2ETest
    {
        public PackFunctionTests(ITestOutputHelper output) : base(output) { }

        // [Fact]
        public Task pack_python_from_cache()
        {
            var syncDirMessage = "Directory .python_packages already in sync with requirements.txt. Skipping restoring dependencies...";
            return CliTester.Run(new[] {
                // Create a Python function
                new RunConfiguration
                {
                    Commands = new[]
                    {
                        "init . --worker-runtime python",
                        "new --template \"Httptrigger\" --name httptrigger",
                    },
                    CheckFiles = new FileResult[]
                    {
                        new FileResult
                        {
                            Name = "local.settings.json",
                            ContentContains = new []
                            {
                                "FUNCTIONS_WORKER_RUNTIME",
                                "python"
                            }
                        }
                    },
                    OutputContains = new[]
                    {
                        "Writing .gitignore",
                        "Writing host.json",
                        "Writing local.settings.json"
                    }
                },
                // Make sure pack works
                new RunConfiguration
                {
                    Commands = new[]
                    {
                        "pack",
                    },
                    OutputContains = new[]
                    {
                        "Creating a new package"
                    },
                    CheckFiles = new FileResult[]
                    {
                        new FileResult
                        {
                            Name = Path.Combine(".python_packages", "requirements.txt.md5")
                        }
                    },
                    OutputDoesntContain = new[]
                    {
                        syncDirMessage
                    },
                    CommandTimeout = TimeSpan.FromMinutes(2)
                },
                // Without changing requirements.txt, make sure pack does not restore again
                new RunConfiguration
                {
                    Commands = new[]
                    {
                        "pack",
                    },
                    OutputContains = new[]
                    {
                        "Creating a new package",
                        syncDirMessage
                    },
                    CheckFiles = new FileResult[]
                    {
                        new FileResult
                        {
                            Name = Path.Combine(".python_packages", "requirements.txt.md5")
                        }
                    },
                    CommandTimeout = TimeSpan.FromMinutes(2)
                },
                // Update requirements.txt and make sure pack restores the dependencies
                new RunConfiguration
                {
                    PreTest = (workingDir) =>
                    {
                        var reqTxt = Path.Combine(workingDir, "requirements.txt");
                        _output.WriteLine($"Writing to file {reqTxt}");
                        FileSystemHelpers.WriteAllTextToFile(reqTxt, "requests");
                    },
                    Commands = new[]
                    {
                        "pack",
                    },
                    OutputContains = new[]
                    {
                        "Creating a new package"
                    },
                    CheckFiles = new FileResult[]
                    {
                        new FileResult
                        {
                            Name = Path.Combine(".python_packages", "requirements.txt.md5")
                        }
                    },
                    OutputDoesntContain = new[]
                    {
                        syncDirMessage
                    },
                    CommandTimeout = TimeSpan.FromMinutes(2)
                }
            }, _output);
        }

    }
}
