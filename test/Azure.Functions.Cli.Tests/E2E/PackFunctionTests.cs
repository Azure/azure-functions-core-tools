using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading.Tasks;
using Azure.Functions.Cli.Tests.E2E.Helpers;
using Xunit;
using Xunit.Abstractions;

namespace Azure.Functions.Cli.Tests.E2E
{
    public class PackFunctionTests : BaseE2ETest
    {
        public PackFunctionTests(ITestOutputHelper output) : base(output) { }

        [Fact]
        public Task pack_node_app_and_replace_existing_archive()
        {
            return CliTester.Run(new[]
            {
                new RunConfiguration
                {
                    Commands = new[]
                    {
                        "init . --worker-runtime node -m v3 --skip-npm-install",
                        "new -l javascript -t HttpTrigger -n httptrigger",
                        "pack -o packed"
                    },
                    CheckFiles = new[] { new FileResult { Name = "packed.zip" } },
                    OutputContains = new[] { "Creating a new package" },
                    CommandTimeout = TimeSpan.FromMinutes(2)
                },
                new RunConfiguration
                {
                    Commands = new[] { "pack -o packed" },
                    OutputContains = new[] { "Deleting the old package", "Creating a new package" },
                    Test = (workingDir, _, _) =>
                    {
                        using var archive = ZipFile.OpenRead(Path.Combine(workingDir, "packed.zip"));
                        var entries = archive.Entries.Select(entry => entry.FullName).ToArray();
                        Assert.Contains("host.json", entries);
                        Assert.Contains("httptrigger/function.json", entries);
                        Assert.Contains("httptrigger/index.js", entries);
                        Assert.DoesNotContain("local.settings.json", entries);
                        Assert.DoesNotContain("packed.zip", entries);
                        return Task.CompletedTask;
                    }
                }
            }, _output);
        }
    }
}
