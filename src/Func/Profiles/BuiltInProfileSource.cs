// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Reflection;

namespace Azure.Functions.Cli.Profiles;

/// <summary>
/// Loads the bundled built-in profile registry embedded in the CLI.
/// </summary>
internal sealed class BuiltInProfileSource(ProfileDocumentParser parser) : IProfileSource
{
    private const string ResourceName = "Azure.Functions.Cli.Profiles.BuiltInProfiles.json";

    private readonly ProfileDocumentParser _parser = parser ?? throw new ArgumentNullException(nameof(parser));

    public async Task<ProfileSourceSnapshot> LoadAsync(ProfileSourceContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);

        Assembly assembly = typeof(BuiltInProfileSource).Assembly;
        await using Stream stream = assembly.GetManifestResourceStream(ResourceName)
            ?? throw new InvalidOperationException($"Bundled profile registry resource '{ResourceName}' was not found.");

        using StreamReader reader = new(stream);
        string json = await reader.ReadToEndAsync(cancellationToken);

        var source = new ProfileSourceInfo(ProfileSourceKind.BuiltIn, "bundled registry");

        return _parser.ParseBuiltInRegistry(json, source);
    }
}
