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
            var fakeDownloadLink = "https://example.com/public/coretoolnumber/assemblyfile.zip";
            var releaseDetail = new CoreToolsRelease { DownloadLink = fakeDownloadLink };
            var releaseSummary = new ReleaseSummary { ReleaseDetail = releaseDetail };

            var result = releaseSummary.CoreToolsAssemblyZipFile;

            Assert.Equal("assemblyfile.zip", result);  // We expect the segment "assembly.zip" based on the provided URL
        }

        [Fact]
        public void CoreToolsAssemblyZipFile_ShouldReturnEmpty_WhenDownloadLinkHasLessThanFourSegments()
        {
            var fakeDownloadLink = "https://example.com/path/to/";
            var releaseDetail = new CoreToolsRelease { DownloadLink = fakeDownloadLink };
            var releaseSummary = new ReleaseSummary { ReleaseDetail = releaseDetail };

            var result = releaseSummary.CoreToolsAssemblyZipFile;

            Assert.Equal(string.Empty, result);  // There are fewer than 4 segments, so the result should be empty
        }

        [Fact]
        public void CoreToolsAssemblyZipFile_ShouldReturnEmpty_WhenDownloadLinkIsNull()
        {
            var releaseDetail = new CoreToolsRelease { DownloadLink = null };
            var releaseSummary = new ReleaseSummary { ReleaseDetail = releaseDetail };

            var result = releaseSummary.CoreToolsAssemblyZipFile;

            Assert.Equal(string.Empty, result);  // The result should be empty when there is no link
        }
    }
}
