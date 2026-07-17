// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Profiles;
using Xunit;

namespace Azure.Functions.Cli.Tests.Profiles;

public class ProfileCatalogSourceOrderTests
{
    private static readonly string _remoteRegistry = """
        {
          "$schema": "https://aka.ms/func-profiles/v1/schema.json",
          "generatedAt": "2026-06-01T00:00:00Z",
          "profiles": {
            "flex": {
              "sku": "flex-consumption",
              "status": "stable",
              "host": { "version": "[4.1100.0, 5.0.0)" },
              "extensionBundle": { "version": "[4.0.0, 5.0.0)" },
              "workers": { "node": { "version": "[4.0.0]" } },
              "supportedRuntimes": ["node", "python"]
            }
          }
        }
        """;

    private static readonly string _bundledRegistry = """
        {
          "$schema": "https://aka.ms/func-profiles/v1/schema.json",
          "generatedAt": "2026-05-01T00:00:00Z",
          "profiles": {
            "flex": {
              "sku": "flex-consumption",
              "status": "stable",
              "host": { "version": "[4.1000.0, 5.0.0)" },
              "extensionBundle": { "version": "[3.0.0, 5.0.0)" },
              "workers": { "node": { "version": "[3.13.0]" } },
              "supportedRuntimes": ["node", "python"]
            }
          }
        }
        """;

    [Fact]
    public async Task FindProfile_RemoteOverridesBundled_WhenBothContainSameName()
    {
        // Arrange: simulate the DI ordering — Remote before BuiltIn
        var parser = new ProfileDocumentParser();
        var remoteSource = new FakeProfileSource(_remoteRegistry, parser, "remote registry");
        var builtInSource = new FakeProfileSource(_bundledRegistry, parser, "bundled registry");

        var catalog = new ProfileCatalog([remoteSource, builtInSource]);
        var context = new ProfileSourceContext(new DirectoryInfo(Path.GetTempPath()));

        // Act
        IReadOnlyList<ProfileSourceSnapshot> snapshots = await catalog.LoadAsync(context, CancellationToken.None);
        ProfileDefinitionEntry? entry = catalog.FindProfile("flex", snapshots);

        // Assert: the remote (newer) version wins
        Assert.NotNull(entry);
        Assert.Contains("remote", entry.Source.DisplayName);
        Assert.Equal("[4.1100.0, 5.0.0)", entry.Definition.Host!.Version);
    }

    [Fact]
    public async Task FindProfile_FallsThroughToBuiltIn_WhenRemoteIsEmpty()
    {
        // Arrange: Remote returns empty (network failure scenario)
        var parser = new ProfileDocumentParser();
        var remoteSource = new EmptyProfileSource();
        var builtInSource = new FakeProfileSource(_bundledRegistry, parser, "bundled registry");

        var catalog = new ProfileCatalog([remoteSource, builtInSource]);
        var context = new ProfileSourceContext(new DirectoryInfo(Path.GetTempPath()));

        // Act
        IReadOnlyList<ProfileSourceSnapshot> snapshots = await catalog.LoadAsync(context, CancellationToken.None);
        ProfileDefinitionEntry? entry = catalog.FindProfile("flex", snapshots);

        // Assert: falls through to bundled
        Assert.NotNull(entry);
        Assert.Contains("bundled", entry.Source.DisplayName);
    }

    [Fact]
    public async Task ListEffectiveProfiles_RemoteWinsForSharedName()
    {
        // Arrange
        var parser = new ProfileDocumentParser();
        var remoteSource = new FakeProfileSource(_remoteRegistry, parser, "remote registry");
        var builtInSource = new FakeProfileSource(_bundledRegistry, parser, "bundled registry");

        var catalog = new ProfileCatalog([remoteSource, builtInSource]);
        var context = new ProfileSourceContext(new DirectoryInfo(Path.GetTempPath()));

        // Act
        IReadOnlyList<ProfileSourceSnapshot> snapshots = await catalog.LoadAsync(context, CancellationToken.None);
        IReadOnlyList<ProfileDefinitionEntry> profiles = catalog.ListEffectiveProfiles(snapshots);

        // Assert: "flex" appears once, from the remote source
        ProfileDefinitionEntry flex = Assert.Single(profiles, p => p.Name == "flex");
        Assert.Contains("remote", flex.Source.DisplayName);
    }

    private sealed class FakeProfileSource(string json, ProfileDocumentParser parser, string displayName) : IProfileSource
    {
        public Task<ProfileSourceSnapshot> LoadAsync(ProfileSourceContext context, CancellationToken cancellationToken)
        {
            var source = new ProfileSourceInfo(ProfileSourceKind.BuiltIn, displayName);
            return Task.FromResult(parser.ParseBuiltInRegistry(json, source));
        }
    }

    private sealed class EmptyProfileSource : IProfileSource
    {
        public Task<ProfileSourceSnapshot> LoadAsync(ProfileSourceContext context, CancellationToken cancellationToken)
        {
            var source = new ProfileSourceInfo(ProfileSourceKind.BuiltIn, "remote registry (unavailable)");
            return Task.FromResult(ProfileSourceSnapshot.Empty(source));
        }
    }
}
