using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Tests.E2E;
using Azure.Functions.Cli.Tests.E2E.Helpers;
using System;
using System.IO;
using System.Threading.Tasks;
using Xunit;
using Xunit.Abstractions;

namespace Azure.Functions.Cli.Tests.ExtensionsTests
{
    public class ExtensionBundleTests : BaseE2ETest
    {
        public ExtensionBundleTests(ITestOutputHelper output) : base(output) { }

        [Fact]
        public Task bundlesconfiguredbydefault_no_action()
        {
            return CliTester.Run(new RunConfiguration
            {
                Commands = new[] {
                    "init . --worker-runtime node",
                    "new --template SendGrid --name testfunc",
                    "extensions install"
                },
                OutputContains = new[]
                {
                    "No action performed"
                },
                CommandTimeout = TimeSpan.FromMinutes(1)
            }, _output);
        }
    }
}
