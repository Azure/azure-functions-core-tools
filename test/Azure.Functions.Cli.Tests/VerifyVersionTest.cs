using System;
using System.IO;
using System.Threading.Tasks;
using System.Linq;
using Azure.Functions.Cli.Common;
using FluentAssertions;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Octokit;
using Xunit;
using System.Runtime.InteropServices;

namespace Azure.Functions.Cli.Tests
{
    public static class VerifyVersionTest
    {
        [Fact]
        public static async Task VerifyVersion()
        {
            var thisRelease = Constants.CliDisplayVersion;
            var latestBetaReleased = await GetLatestBetaVersion();
            var npmPackageJsonVersion = await GetNpmVersion();

            if (npmPackageJsonVersion != null)
            {
                npmPackageJsonVersion.Should().Be(thisRelease, because: "NPM version must match csproj version");
            }

            thisRelease.Should().NotBeNullOrWhiteSpace(because: "Current release version should always have a value");
            latestBetaReleased.Should().NotBeNullOrWhiteSpace(because: "latest RTW release should always have a version");

            thisRelease.Should().Contain("beta");
            latestBetaReleased.Should().Contain("beta");

            thisRelease.Equals(latestBetaReleased, StringComparison.OrdinalIgnoreCase)
                .Should().BeFalse(because: $"this build has version {thisRelease}. Github has version {latestBetaReleased}. Make sure to update the current version");

            var thisBetaRevision = int.Parse(thisRelease.Split('.').Last());
            var releasedBetaRevision = int.Parse(latestBetaReleased.Split('.').Last());

            (thisBetaRevision > releasedBetaRevision)
                .Should().BeTrue(because: $"this build has revision {thisBetaRevision} while released is {releasedBetaRevision}. {nameof(thisBetaRevision)} must be > {nameof(releasedBetaRevision)}");
        }

        private static async Task<string> GetLatestBetaVersion()
        {
            var github = new GitHubClient(new ProductHeaderValue("azure-functions-core-tools"));
            var repo = await github.Repository.Get("Azure", "azure-functions-core-tools");
            var release = await github.Repository.Release.GetAll(repo.Id);
            return release.FirstOrDefault(r => r.Name.Contains("beta"))?.Name;
        }

        private static async Task<string> GetNpmVersion()
        {
            if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("APPVEYOR_BUILD_NUMBER")))
            {
                return null;
            }

            var path = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
                ? @"C:\azure-functions-cli\src\Azure.Functions.Cli\npm\package.json"
                : "/home/appveyor/projects/azure-functions-core-tools/src/Azure.Functions.Cli/npm/package.json";
            
            var jObject = JsonConvert.DeserializeObject<JObject>(File.ReadAllText(path));
            return jObject["version"]?.ToString() ?? string.Empty;
        }
    }
}