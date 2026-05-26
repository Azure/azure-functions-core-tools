// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Configuration;

namespace Azure.Functions.Cli.Profiles;

/// <summary>
/// Loads user-level profiles from the Azure Functions CLI home directory.
/// </summary>
internal sealed class UserProfileSource(ProfileDocumentParser parser, IProfileFileSystem fileSystem, UserConfigurationPathsOptions userConfigurationPaths)
    : IProfileSource
{
    private readonly ProfileDocumentParser _parser = parser ?? throw new ArgumentNullException(nameof(parser));
    private readonly IProfileFileSystem _fileSystem = fileSystem ?? throw new ArgumentNullException(nameof(fileSystem));
    private readonly UserConfigurationPathsOptions _userConfigurationPaths =
        userConfigurationPaths ?? throw new ArgumentNullException(nameof(userConfigurationPaths));

    public async Task<ProfileSourceSnapshot> LoadAsync(ProfileSourceContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);

        string path = _userConfigurationPaths.ProfilesPath;
        ProfileSourceInfo source = new(ProfileSourceKind.User, "~/.azure-functions/profiles.json", path);
        string? json = await _fileSystem.ReadAllTextIfExistsAsync(path, cancellationToken);

        return json is null
            ? ProfileSourceSnapshot.Empty(source)
            : _parser.ParseCustomProfiles(json, source);
    }
}
