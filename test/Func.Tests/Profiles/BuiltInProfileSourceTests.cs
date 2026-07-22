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
        snapshot.Profiles.Keys.OrderBy(static p => p, StringComparer.Ordinal).Should().Equal(["flex", "linux-consumption", "linux-premium", "windows-consumption", "windows-dedicated"]);
        snapshot.Profiles["flex"].Status.Should().Be("stable");
        snapshot.Profiles["linux-consumption"].Status.Should().Be("deprecated");
    }
}
