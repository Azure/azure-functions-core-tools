// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Net.Http;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Azure.Functions.Cli.Helpers;
using FluentAssertions;
using Moq.Protected;
using Moq;
using Xunit;

namespace Azure.Functions.Cli.Tests
{
    public class VersionHelperTests
    {
        [Fact]
        public async Task IsRunningAnOlderVersion_ShouldReturnTrue_WhenVersionIsOlder()
        {
            // Create the mocked HttpClient with the mock response
            var mockHttpClient = GetMockHttpClientWithResponse();

            VersionHelper.CliVersion = "4.0.5";
            var result = await VersionHelper.IsRunningAnOlderVersion(mockHttpClient);

            result.Should().Be(true);
        }

        [Fact]
        public async Task IsRunningAnOlderVersion_ShouldReturnFalse_WhenVersionIsUpToDate()
        {
            // Create the mocked HttpClient with the mock response
            var mockHttpClient = GetMockHttpClientWithResponse();

            VersionHelper.CliVersion = "4.0.6610";
            var result = await VersionHelper.IsRunningAnOlderVersion(mockHttpClient);

            result.Should().Be(false);
        }

        // Method to return a mocked HttpClient
        private HttpClient GetMockHttpClientWithResponse()
        {
            var mockJsonResponse = @"{
                'tag_name':'4.0.6610',
                'target_commitish': '48490a7ee744ed435fdce62f5e1f2f39c61c5309',
                'name': '4.0.6610',
                'draft': false,
                'prerelease': false,
                'created_at': '',
                'published_at': '2024-11-13T22:08:49Z',
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