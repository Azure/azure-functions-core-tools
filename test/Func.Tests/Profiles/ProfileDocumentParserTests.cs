// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Profiles;
using Xunit;

namespace Azure.Functions.Cli.Tests.Profiles;

public class ProfileDocumentParserTests
{
    private readonly ProfileDocumentParser _parser = new();

    [Fact]
    public void ParseBuiltInRegistry_AcceptsKnownSchemaAndVersionRanges()
    {
        string json = $$"""
            {
              "$schema": "{{ProfileSchemas.BuiltInRegistryV1}}",
              "generatedAt": "2025-01-02T03:04:05Z",
              "profiles": {
                "flex": {
                  "status": "stable",
                  "host": { "version": "[1.8.1, 4.1048.200)" },
                  "extensionBundle": { "version": "[3.0.0, 5.0.0)" },
                  "workers": {
                    "node": { "version": "[3.13.0]" }
                  },
                  "supportedRuntimes": [ "node", "python" ]
                }
              }
            }
            """;

        ProfileSourceSnapshot snapshot = _parser.ParseBuiltInRegistry(json, Source(ProfileSourceKind.BuiltIn));

        ProfileDefinition profile = snapshot.Profiles["flex"];
        Assert.Equal(new DateTimeOffset(2025, 1, 2, 3, 4, 5, TimeSpan.Zero), snapshot.GeneratedAt);
        Assert.Equal("[1.8.1, 4.1048.200)", profile.Host!.Version);
        Assert.Equal("[3.0.0, 5.0.0)", profile.ExtensionBundle!.Version);
        Assert.Equal("[3.13.0]", profile.Workers!["node"]!.Version);
        Assert.Equal(["node", "python"], profile.SupportedRuntimes);
    }

    [Fact]
    public void ParseBuiltInRegistry_RejectsMissingSchema()
    {
        string json = """
            {
              "profiles": {}
            }
            """;

        ProfileConfigurationException ex = Assert.Throws<ProfileConfigurationException>(
            () => _parser.ParseBuiltInRegistry(json, Source(ProfileSourceKind.BuiltIn)));

        Assert.Contains(ProfileSchemas.BuiltInRegistryV1, ex.Message);
    }

    [Fact]
    public void ParseCustomProfiles_RejectsUnknownSchema()
    {
        string json = """
            {
              "$schema": "https://aka.ms/func-custom-profiles/v999/schema.json"
            }
            """;

        ProfileConfigurationException ex = Assert.Throws<ProfileConfigurationException>(
            () => _parser.ParseCustomProfiles(json, Source(ProfileSourceKind.Project)));

        Assert.Contains("unsupported schema", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(ProfileSchemas.CustomProfilesV1, ex.Message);
    }

    [Fact]
    public void ParseCustomProfiles_RejectsInvalidVersionRange()
    {
        string json = """
            {
              "flex": {
                "host": { "version": "not-a-range" }
              }
            }
            """;

        ProfileConfigurationException ex = Assert.Throws<ProfileConfigurationException>(
            () => _parser.ParseCustomProfiles(json, Source(ProfileSourceKind.Project)));

        Assert.Contains("invalid NuGet version range", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("host.version", ex.Message);
    }

    [Fact]
    public void ParseCustomProfiles_RejectsUnsupportedStatus()
    {
        string json = """
            {
              "flex": {
                "status": "retired"
              }
            }
            """;

        ProfileConfigurationException ex = Assert.Throws<ProfileConfigurationException>(
            () => _parser.ParseCustomProfiles(json, Source(ProfileSourceKind.Project)));

        Assert.Contains("unsupported status", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ParseCustomProfiles_AllowsUnknownFields()
    {
        string json = """
            {
              "$schema": "https://aka.ms/func-custom-profiles/v1/schema.json",
              "$metadata": { "owner": "release" },
              "flex": {
                "host": { "version": "[1.0.0, 2.0.0)" },
                "unknownObject": { "future": true }
              }
            }
            """;

        ProfileSourceSnapshot snapshot = _parser.ParseCustomProfiles(json, Source(ProfileSourceKind.Project));

        Assert.True(snapshot.Profiles.ContainsKey("flex"));
    }

    private static ProfileSourceInfo Source(ProfileSourceKind kind)
        => new(kind, "test profiles");
}
