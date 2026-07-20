// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Configuration;
using Azure.Functions.Cli.Profiles;
using NSubstitute;

namespace Azure.Functions.Cli.Tests.Profiles;

public class UserProfileSourceTests
{
    [Fact]
    public async Task LoadAsync_ReadsProfilesFromUserConfigurationHome()
    {
        string userHome = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        string expectedPath = Path.Combine(userHome, CliConfigurationPathsOptions.ProfilesFileName);
        var paths = new CliConfigurationPathsOptions(userHome);
        IProfileFileSystem fileSystem = Substitute.For<IProfileFileSystem>();
        fileSystem.ReadAllTextIfExistsAsync(expectedPath, CancellationToken.None).Returns(Task.FromResult<string?>(null));
        var source = new UserProfileSource(new ProfileDocumentParser(), fileSystem, paths);
        var context = new ProfileSourceContext(new DirectoryInfo(Path.GetTempPath()));

        ProfileSourceSnapshot snapshot = await source.LoadAsync(context, CancellationToken.None);

        snapshot.Source.Kind.Should().Be(ProfileSourceKind.User);
        snapshot.Source.DisplayName.Should().Be("~/.azure-functions/profiles.json");
        snapshot.Source.Path.Should().Be(expectedPath);
        await fileSystem.Received(1).ReadAllTextIfExistsAsync(expectedPath, CancellationToken.None);
    }
}
