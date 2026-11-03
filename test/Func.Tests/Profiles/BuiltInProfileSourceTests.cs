// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Profiles;

namespace Azure.Functions.Cli.Tests.Profiles;

public class BuiltInProfileSourceTests
{
    [Fact]
    public async Task LoadAsync_LoadsBundledRegistry()
    {
        var source = new BuiltInProfileSource(new ProfileDocumentParser());
        var context = new ProfileSourceContext(new DirectoryInfo(Path.GetTempPath()));

        ProfileSourceSnapshot snapshot = await source.LoadAsync(context, CancellationToken.None);

        snapshot.Source.Kind.Should().Be(ProfileSourceKind.BuiltIn);
        snapshot.Profiles.Keys.OrderBy(static p => p, StringComparer.Ordinal).Should().Equal(
            ["flex", "linux-consumption", "linux-dedicated", "linux-premium", "windows-consumption", "windows-dedicated", "windows-premium"]);
        snapshot.Profiles["flex"].Status.Should().Be("stable");
        snapshot.Profiles["linux-consumption"].Status.Should().Be("deprecated");
    }

    [Theory]
    [InlineData("windows-consumption", false, false)]
    [InlineData("windows-dedicated", false, false)]
    [InlineData("windows-premium", false, false)]
    [InlineData("linux-dedicated", true, false)]
    [InlineData("linux-premium", true, false)]
    [InlineData("linux-consumption", true, false)]
    [InlineData("flex", true, true)]
    public async Task LoadAsync_BundledProfilesMatchSupportedStacks(string profileName, bool supportsPython, bool supportsGo)
    {
        var source = new BuiltInProfileSource(new ProfileDocumentParser());
        var context = new ProfileSourceContext(new DirectoryInfo(Path.GetTempPath()));

        ProfileSourceSnapshot snapshot = await source.LoadAsync(context, CancellationToken.None);

        var profile = snapshot.Profiles[profileName];
        List<string> expectedRuntimes = ["node", "java", "powershell", "dotnet-isolated", "custom"];
        List<string> expectedWorkers = ["node", "java", "powershell"];
        if (supportsPython)
        {
            expectedRuntimes.Add("python");
            expectedWorkers.Add("python");
        }

        if (supportsGo)
        {
            expectedRuntimes.Add("go");
            expectedWorkers.Add("go");
        }

        profile.Sku.Should().Be(profileName == "flex" ? "flex-consumption" : profileName);
        profile.Status.Should().Be(profileName == "linux-consumption" ? "deprecated" : "stable");
        profile.Host!.Version.Should().Be("[1.8.1, 4.1054.200]");
        profile.ExtensionBundle!.Version.Should().Be("[3.0.0, 5.0.0)");
        profile.SupportedRuntimes.Should().BeEquivalentTo(expectedRuntimes);
        profile.Workers.Should().NotBeNull();
        profile.Workers!.Keys.Should().BeEquivalentTo(expectedWorkers);
    }
}
