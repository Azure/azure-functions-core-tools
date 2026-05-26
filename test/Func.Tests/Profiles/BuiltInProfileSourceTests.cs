// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Profiles;
using Xunit;

namespace Azure.Functions.Cli.Tests.Profiles;

public class BuiltInProfileSourceTests
{
    [Fact]
    public async Task LoadAsync_LoadsBundledRegistry()
    {
        var source = new BuiltInProfileSource(new ProfileDocumentParser());
        var context = new ProfileSourceContext(new DirectoryInfo(Path.GetTempPath()));

        ProfileSourceSnapshot snapshot = await source.LoadAsync(context, CancellationToken.None);

        Assert.Equal(ProfileSourceKind.BuiltIn, snapshot.Source.Kind);
        Assert.Equal(
            ["flex", "linux-consumption", "linux-premium", "windows-consumption", "windows-dedicated"],
            snapshot.Profiles.Keys.OrderBy(static p => p, StringComparer.Ordinal));
        Assert.Equal("stable", snapshot.Profiles["flex"].Status);
        Assert.Equal("deprecated", snapshot.Profiles["linux-consumption"].Status);
    }
}
