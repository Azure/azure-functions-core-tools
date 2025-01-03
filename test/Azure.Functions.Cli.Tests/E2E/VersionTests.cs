using System;
using System.Threading.Tasks;
using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Tests.E2E.Helpers;
using FluentAssertions;
using Xunit;
using Xunit.Abstractions;
using static Azure.Functions.Cli.Helpers.VersionHelper;
using Azure.Functions.Cli.Helpers;
using Moq.Protected;
using Moq;
using System.Net.Http;
using System.Net;
using System.Threading;

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

        [Fact]
        public async Task IsRunningAnOlderVersion_ShouldReturnTrue_WhenVersionIsOlder()
        {
            // Create the mocked HttpClient with the mock response
            var mockHttpClient = GetMockHttpClient();

            SetCliVersion("4.0.1");
            var result = await VersionHelper.IsRunningAnOlderVersion(mockHttpClient);

            Assert.True(result);
        }

        [Fact]
        public async Task IsRunningAnOlderVersion_ShouldReturnFalse_WhenVersionIsUpToDate()
        {
            // Create the mocked HttpClient with the mock response
            var mockHttpClient = GetMockHttpClient();

            SetCliVersion("4.0.6610");
            var result = await VersionHelper.IsRunningAnOlderVersion(mockHttpClient);

            Assert.False(result);
        }
        // Method to return a mocked HttpClient
        private HttpClient GetMockHttpClient()
        {
            var mockJsonResponse = @"{
                'tags': {
                    'v4': {
                        'release': '4.0',
                        'releaseQuality': 'GA',
                        'hidden': false
                    },
                },
                'releases': {
                    '4.0': {
                        'coreTools': [
                            {
                                'OS': 'Windows',
                                'Architecture': 'x86',
                                'downloadLink': 'https://example.com/public/0.0.1/Azure.Functions.Latest.4.0.6610.zip',
                                'sha2': 'BB4978D83CFBABAE67D4D720FEC1F1171BE0406B2147EF3FECA476C19ADD9920',
                                'size': 'full',
                                'default': 'true'
                            }
                        ]
                    }
                }
            }";
            var mockHandler = new Mock<HttpMessageHandler>();

            // Mock the SendAsync method to return a mocked response
            mockHandler.Protected()
                .Setup<Task<HttpResponseMessage>>("SendAsync",
                    ItExpr.IsAny<HttpRequestMessage>(),
                    ItExpr.IsAny<CancellationToken>())
                .ReturnsAsync(new HttpResponseMessage
                {
                    StatusCode = HttpStatusCode.OK,
                    Content = new StringContent(mockJsonResponse)
                });

            // Return HttpClient with mocked handler
            return new HttpClient(mockHandler.Object);
        }
    }
}
