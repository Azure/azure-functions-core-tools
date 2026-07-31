// Copyright (c) .NET Foundation and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.
//
// Vendored and adapted from the .NET Templating engine:
//   repo:   https://github.com/dotnet/templating
//   source: src/Tools/Microsoft.TemplateSearch.TemplateDiscovery/TestProvider/TestPackProvider.cs
//   version: 10.0.302
// See README.md in this directory for the full provenance note and list of adaptations.

using System.Runtime.CompilerServices;
using NuGet.Packaging;

namespace Azure.Functions.Cli.TemplateDiscovery;

/// <summary>
/// Enumerates <c>.nupkg</c> files already on disk (a directory feed or a pre-downloaded folder), reading
/// identity and metadata straight from each package's nuspec. This is the fully-offline discovery path.
/// </summary>
internal sealed class DirectoryPackageProvider : IPackageProvider
{
    private readonly DirectoryInfo _folder;

    public DirectoryPackageProvider(DirectoryInfo folder)
    {
        _folder = folder ?? throw new ArgumentNullException(nameof(folder));
        if (!_folder.Exists)
        {
            throw new DirectoryNotFoundException($"Package directory not found: {_folder.FullName}");
        }
    }

    public string Name => "DirectoryProvider";

    public async IAsyncEnumerable<CandidatePackage> GetCandidatesAsync([EnumeratorCancellation] CancellationToken cancellationToken)
    {
        foreach (FileInfo file in _folder.EnumerateFiles("*.nupkg", SearchOption.AllDirectories))
        {
            cancellationToken.ThrowIfCancellationRequested();
            yield return ReadCandidate(file.FullName);
        }

        await Task.CompletedTask;
    }

    public Task<string?> EnsureLocalAsync(CandidatePackage package, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(package);
        return Task.FromResult(package.LocalPath);
    }

    public void CleanupDownloads()
    {
        // Nothing to clean up: packages are user-provided and stay where they are.
    }

    private static CandidatePackage ReadCandidate(string path)
    {
        using var reader = new PackageArchiveReader(path);
        NuspecReader nuspec = reader.NuspecReader;
        string id = nuspec.GetId();
        string version = nuspec.GetVersion().ToNormalizedString();

        IReadOnlyList<string> owners = SplitAuthors(nuspec.GetOwners());
        if (owners.Count == 0)
        {
            owners = SplitAuthors(nuspec.GetAuthors());
        }

        return new CandidatePackage(
            Name: id,
            Version: version,
            TotalDownloads: 0,
            Owners: owners,
            Reserved: false,
            Description: NullIfWhitespace(nuspec.GetDescription()),
            IconUrl: NullIfWhitespace(nuspec.GetIconUrl()),
            LocalPath: path);
    }

    private static IReadOnlyList<string> SplitAuthors(string? value)
        => string.IsNullOrWhiteSpace(value)
            ? []
            : value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    private static string? NullIfWhitespace(string? value) => string.IsNullOrWhiteSpace(value) ? null : value;
}
