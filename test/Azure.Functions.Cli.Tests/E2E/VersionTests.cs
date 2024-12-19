using System;
using System.Threading.Tasks;
using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Tests.E2E.Helpers;
using FluentAssertions;
using Xunit;
using Xunit.Abstractions;
using static Azure.Functions.Cli.Helpers.VersionHelper;

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

        [Fact]
        public void CoreToolsAssemblyZipFile_ShouldParseCorrectSegment_WhenValidDownloadLinkIsProvided()
        {
            var fakeDownloadLink = "https://example.com/public/coretoolnumber/V4/assemblyfile.zip";
            var releaseDetail = new CoreToolsRelease { DownloadLink = fakeDownloadLink };
            var releaseSummary = new ReleaseSummary ("V4",releaseDetail);

            var result = releaseSummary.CoreToolsAssemblyZipFile;

            result.Should().Be("assemblyfile.zip"); // We expect the segment "assemblyfile.zip" based on the provided URL
        }

        [Fact]
        public void CoreToolsAssemblyZipFile_ShouldReturnEmpty_WhenDownloadLinkIsNull()
        {
            var releaseDetail = new CoreToolsRelease { DownloadLink = null };
            var releaseSummary = new ReleaseSummary("V4", releaseDetail);

            var result = releaseSummary.CoreToolsAssemblyZipFile; 

            result.Should().Be(string.Empty); // The result should be empty when there is no link
        }

        [Theory]
        [InlineData("4.0.6610", "Azure.Functions.Cli.linux-x64.4.0.6610.zip", false)]
        [InlineData("4.0.1", "Azure.Functions.Cli.linux-x64.4.0.6610.zip", true)]

        public void Test_IsRunningAnOlderVersion(string cliVersion, string latestCoreToolsAssemblyZipFile, bool expected)
        {
            bool result = IsRunningAnOlderVersion(cliVersion, latestCoreToolsAssemblyZipFile);

            result.Should().Be(expected);
        }

        private bool IsRunningAnOlderVersion(string cliVersion, string latestCoreToolsAssemblyZipFile)
        {
            if (!string.IsNullOrEmpty(latestCoreToolsAssemblyZipFile) &&
                !latestCoreToolsAssemblyZipFile.Contains($"{cliVersion}.zip"))
            {
                return true;
            }

            return false;
        }


    }
}
