// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Configuration;

namespace Azure.Functions.Cli.Profiles;

/// <summary>
/// Loads project-level profiles from <c>.func/profiles.json</c>.
/// </summary>
internal sealed class ProjectProfileSource(ProfileDocumentParser parser, IProfileFileSystem fileSystem) : IProfileSource
{
    public const string ProfilesFileName = "profiles.json";

    private readonly ProfileDocumentParser _parser = parser ?? throw new ArgumentNullException(nameof(parser));
    private readonly IProfileFileSystem _fileSystem = fileSystem ?? throw new ArgumentNullException(nameof(fileSystem));

    public async Task<ProfileSourceSnapshot> LoadAsync(ProfileSourceContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);

        string path = Path.Combine(context.WorkingDirectory.FullName, CliConfigurationNames.ProjectConfigFolderName, ProfilesFileName);
        ProfileSourceInfo source = new(ProfileSourceKind.Project, ".func/profiles.json", path);
        string? json = await _fileSystem.ReadAllTextIfExistsAsync(path, cancellationToken);

        return json is null
            ? ProfileSourceSnapshot.Empty(source)
            : _parser.ParseCustomProfiles(json, source);
    }
}
