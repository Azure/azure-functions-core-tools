// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

namespace Azure.Functions.Cli.Templates.Acquisition;

internal interface IBasicCompanionCatalog
{
    public Task<BasicCompanionManifest> LoadAsync(CancellationToken cancellationToken);
}

/// <summary>
/// Reads CLI-release-bound acquisition authority independently of project, user and remote profile overrides.
/// </summary>
internal sealed class BundledBasicCompanionCatalog(BasicCompanionManifestReader reader) : IBasicCompanionCatalog
{
    private readonly BasicCompanionManifestReader _reader = reader ?? throw new ArgumentNullException(nameof(reader));

    public async Task<BasicCompanionManifest> LoadAsync(CancellationToken cancellationToken)
    {
        await using Stream resource = typeof(BundledBasicCompanionCatalog).Assembly
            .GetManifestResourceStream("Azure.Functions.Cli.Templates.Acquisition.BasicCompanions.json")
            ?? throw new InvalidOperationException("The bundled basic companion acquisition resource is missing.");
        using StreamReader text = new(resource);
        return _reader.Read(await text.ReadToEndAsync(cancellationToken));
    }
}
