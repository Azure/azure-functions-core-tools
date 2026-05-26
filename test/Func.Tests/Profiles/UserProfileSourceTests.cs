// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Configuration;
using Azure.Functions.Cli.Profiles;
using NSubstitute;
using Xunit;

namespace Azure.Functions.Cli.Tests.Profiles;

public class UserProfileSourceTests
{
    [Fact]
    public async Task LoadAsync_ReadsProfilesFromUserConfigurationHome()
    {
        string userHome = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        string expectedPath = Path.Combine(userHome, UserConfigurationPathsOptions.ProfilesFileName);
        var paths = new UserConfigurationPathsOptions(userHome);
        IProfileFileSystem fileSystem = Substitute.For<IProfileFileSystem>();
        fileSystem.ReadAllTextIfExistsAsync(expectedPath, CancellationToken.None).Returns(Task.FromResult<string?>(null));
        var source = new UserProfileSource(new ProfileDocumentParser(), fileSystem, paths);
        var context = new ProfileSourceContext(new DirectoryInfo(Path.GetTempPath()));

        ProfileSourceSnapshot snapshot = await source.LoadAsync(context, CancellationToken.None);

        Assert.Equal(ProfileSourceKind.User, snapshot.Source.Kind);
        Assert.Equal("~/.azure-functions/profiles.json", snapshot.Source.DisplayName);
        Assert.Equal(expectedPath, snapshot.Source.Path);
        await fileSystem.Received(1).ReadAllTextIfExistsAsync(expectedPath, CancellationToken.None);
    }
}
