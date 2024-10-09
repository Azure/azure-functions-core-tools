using System;
using System.Threading.Tasks;
using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Tests.E2E.Helpers;
using Xunit;
using Xunit.Abstractions;

namespace Azure.Functions.Cli.Tests.E2E
{
    public class VersionTests : BaseE2ETest
    {
        public VersionTests(ITestOutputHelper output) : base(output) { }

        [Theory]
        [InlineData("-v")]
        [InlineData("-version")]
        [InlineData("--version")]
        public async Task version(string args)
        {
            await CliTester.Run(new RunConfiguration
            {
                Commands = new[] { args },
                OutputContains = new[] { "4." },
                CommandTimeout = TimeSpan.FromSeconds(30)
            }, _output);
        }
    }
}