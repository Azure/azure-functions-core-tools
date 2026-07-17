// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Profiles;

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
        snapshot.GeneratedAt.Should().Be(new DateTimeOffset(2025, 1, 2, 3, 4, 5, TimeSpan.Zero));
        profile.Host!.Version.Should().Be("[1.8.1, 4.1048.200)");
        profile.ExtensionBundle!.Version.Should().Be("[3.0.0, 5.0.0)");
        profile.Workers!["node"]!.Version.Should().Be("[3.13.0]");
        profile.SupportedRuntimes.Should().Equal(["node", "python"]);
    }

    [Fact]
    public void ParseBuiltInRegistry_RejectsMissingSchema()
    {
        string json = """
            {
              "profiles": {}
            }
            """;

        ProfileConfigurationException ex = FluentActions.Invoking(() => _parser.ParseBuiltInRegistry(json, Source(ProfileSourceKind.BuiltIn))).Should().ThrowExactly<ProfileConfigurationException>().Which;

        ex.Message.Should().Contain(ProfileSchemas.BuiltInRegistryV1);
    }

    [Fact]
    public void ParseCustomProfiles_RejectsUnknownSchema()
    {
        string json = """
            {
              "$schema": "https://aka.ms/func-custom-profiles/v999/schema.json"
            }
            """;

        ProfileConfigurationException ex = FluentActions.Invoking(() => _parser.ParseCustomProfiles(json, Source(ProfileSourceKind.Project))).Should().ThrowExactly<ProfileConfigurationException>().Which;

        ex.Message.Should().ContainEquivalentOf("unsupported schema");
        ex.Message.Should().Contain(ProfileSchemas.CustomProfilesV1);
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

        ProfileConfigurationException ex = FluentActions.Invoking(() => _parser.ParseCustomProfiles(json, Source(ProfileSourceKind.Project))).Should().ThrowExactly<ProfileConfigurationException>().Which;

        ex.Message.Should().ContainEquivalentOf("invalid NuGet version range");
        ex.Message.Should().Contain("host.version");
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

        ProfileConfigurationException ex = FluentActions.Invoking(() => _parser.ParseCustomProfiles(json, Source(ProfileSourceKind.Project))).Should().ThrowExactly<ProfileConfigurationException>().Which;

        ex.Message.Should().ContainEquivalentOf("unsupported status");
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

        snapshot.Profiles.ContainsKey("flex").Should().BeTrue();
    }

    private static ProfileSourceInfo Source(ProfileSourceKind kind)
        => new(kind, "test profiles");
}
