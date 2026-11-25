// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Configuration;

namespace Azure.Functions.Cli.Tests.Configuration;

public sealed class LocalSettingsProviderTests : IDisposable
{
    private const string EncryptedSecretSentinel = "S1_SYNTHETIC_CIPHERTEXT_SENTINEL";

    private readonly DirectoryInfo _dir = Directory.CreateTempSubdirectory("local-settings-provider-");
    private readonly LocalSettingsProvider _provider = new();

    public void Dispose() => _dir.Delete(recursive: true);

    [Fact]
    public void Get_FileMissing_ReturnsEmptySnapshot()
    {
        LocalSettingsSnapshot snapshot = _provider.Get(_dir);

        snapshot.IsEncrypted.Should().BeFalse();
        snapshot.Values.Should().BeEmpty();
        snapshot.WorkerRuntime.Should().BeNull();
        snapshot.Host.Should().BeNull();
    }

    [Theory]
    [InlineData("")]
    [InlineData("\"IsEncrypted\":false,")]
    public void Get_ValuesAndHostPresent_ReadsSnapshot(string encryptionMarker)
    {
        WriteSettings(
            $$"""
            {
              {{encryptionMarker}}
              "Values": {
                "FUNCTIONS_WORKER_RUNTIME": "python",
                "AzureWebJobsStorage": "UseDevelopmentStorage=true",
                "IgnoredNumber": 42
              },
              "Host": {
                "LocalHttpPort": 7072,
                "CORS": "http://localhost,http://example",
                "CORSCredentials": true,
                "LocalHttpAddress": "0.0.0.0"
              }
            }
            """);

        LocalSettingsSnapshot snapshot = _provider.Get(_dir);

        snapshot.IsEncrypted.Should().BeFalse();
        snapshot.LoadError.Should().BeNull();
        snapshot.WorkerRuntime.Should().Be("python");
        snapshot.Values["AzureWebJobsStorage"].Should().Be("UseDevelopmentStorage=true");
        snapshot.Values.ContainsKey("IgnoredNumber").Should().BeFalse();
        snapshot.SkippedNonStringValueNames.Should().Equal("IgnoredNumber");
        snapshot.Host.Should().NotBeNull();
        snapshot.Host!.LocalHttpPort.Should().Be(7072);
        snapshot.Host.Cors.Should().Be("http://localhost,http://example");
        snapshot.Host.CorsCredentials.Should().BeTrue();
        snapshot.Host.HasLocalHttpAddress.Should().BeTrue();
    }

    [Theory]
    [InlineData("true")]
    [InlineData("\"true\"")]
    public void Get_ExplicitlyEncryptedValues_AreWithheldWithoutRewritingFile(string encryptionMarker)
    {
        WriteSettings(
            $$"""
            {
              "IsEncrypted": {{encryptionMarker}},
              "Values": {
                "FUNCTIONS_WORKER_RUNTIME": "{{EncryptedSecretSentinel}}",
                "PrivateSetting": "{{EncryptedSecretSentinel}}",
                "IgnoredNumber": 42
              },
              "Host": {
                "LocalHttpPort": 7072
              }
            }
            """);
        string path = Path.Combine(_dir.FullName, CliConfigurationNames.LocalSettingsFileName);
        byte[] originalBytes = File.ReadAllBytes(path);
        DateTime originalWriteTime = new(2000, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        File.SetLastWriteTimeUtc(path, originalWriteTime);

        LocalSettingsSnapshot snapshot = _provider.Get(_dir);

        snapshot.IsEncrypted.Should().BeTrue();
        snapshot.Values.Should().BeEmpty();
        snapshot.WorkerRuntime.Should().BeNull();
        snapshot.Host!.LocalHttpPort.Should().Be(7072);
        File.ReadAllBytes(path).Should().Equal(originalBytes);
        File.GetLastWriteTimeUtc(path).Should().Be(originalWriteTime);
        Directory.Exists(Path.Combine(_dir.FullName, CliConfigurationNames.ProjectConfigFolderName)).Should().BeFalse();
    }

    [Theory]
    [InlineData("""{"IsEncrypted":true}""")]
    [InlineData("""{"IsEncrypted":true,"Values":null}""")]
    [InlineData("""{"IsEncrypted":true,"Values":[]}""")]
    [InlineData("""{"IsEncrypted":true,"Values":{"IgnoredNumber":42}}""")]
    public void Get_EncryptedMarkerWithoutStringValues_ReturnsEncryptedSnapshot(string settings)
    {
        WriteSettings(settings);

        LocalSettingsSnapshot snapshot = _provider.Get(_dir);

        snapshot.IsEncrypted.Should().BeTrue();
        snapshot.Values.Should().BeEmpty();
        snapshot.WorkerRuntime.Should().BeNull();
        snapshot.SkippedNonStringValueNames.Should().BeEmpty();
    }

    [Fact]
    public void Get_NonStringValues_AreSkippedAndRecorded()
    {
        WriteSettings(
            """
            {
              "Values": {
                "Kept": "value",
                "Nested": { "Child": "x" },
                "List": ["a"],
                "Flag": true,
                "Missing": null
              }
            }
            """);

        LocalSettingsSnapshot snapshot = _provider.Get(_dir);

        snapshot.Values.Keys.Should().Equal("Kept");
        snapshot.SkippedNonStringValueNames.Should().Equal("Nested", "List", "Flag", "Missing");
    }

    [Fact]
    public void Get_DuplicateValueNames_RecordsOnlyUnloadedNamesOnce()
    {
        WriteSettings("""{ "Values": { "Mixed": 1, "Mixed": "loaded", "Repeated": 1, "repeated": true } }""");

        LocalSettingsSnapshot snapshot = _provider.Get(_dir);

        snapshot.Values["Mixed"].Should().Be("loaded");
        snapshot.SkippedNonStringValueNames.Should().Equal("Repeated");
    }

    [Fact]
    public void WorkerRuntime_ExplicitlyEncryptedSnapshot_DoesNotProvideFallback()
    {
        var snapshot = new LocalSettingsSnapshot
        {
            IsEncrypted = true,
            Values = new Dictionary<string, string>
            {
                ["FUNCTIONS_WORKER_RUNTIME"] = EncryptedSecretSentinel,
            },
        };

        snapshot.WorkerRuntime.Should().BeNull();
    }

    [Fact]
    public void Get_MalformedJson_ReturnsEmptySnapshot()
    {
        WriteSettings("not json {");

        LocalSettingsSnapshot snapshot = _provider.Get(_dir);

        snapshot.Values.Should().BeEmpty();
        snapshot.WorkerRuntime.Should().BeNull();
        snapshot.LoadError.Should().Contain("LineNumber");
    }

    [Fact]
    public void Get_RootIsNotObject_ReturnsEmptySnapshotWithLoadError()
    {
        WriteSettings("[]");

        LocalSettingsSnapshot snapshot = _provider.Get(_dir);

        snapshot.Values.Should().BeEmpty();
        snapshot.LoadError.Should().Be("The file must contain a JSON object.");
    }

    [Fact]
    public void Get_UndecodableLocalHttpAddress_IsRecordedWithoutReadingValue()
    {
        WriteSettings("""{ "Host": { "LocalHttpAddress": "\uD800" } }""");

        LocalSettingsSnapshot snapshot = _provider.Get(_dir);

        snapshot.Host!.HasLocalHttpAddress.Should().BeTrue();
    }

    [Fact]
    public void Get_CachesSnapshotForDirectory()
    {
        WriteSettings("""{"Values":{"FUNCTIONS_WORKER_RUNTIME":"python"}}""");
        LocalSettingsSnapshot first = _provider.Get(_dir);

        WriteSettings("""{"Values":{"FUNCTIONS_WORKER_RUNTIME":"node"}}""");
        LocalSettingsSnapshot second = _provider.Get(_dir);

        second.Should().BeSameAs(first);
        second.WorkerRuntime.Should().Be("python");
    }

    [Fact]
    public void Get_NullDirectory_Throws()
    {
        FluentActions.Invoking(() => _provider.Get(null!)).Should().ThrowExactly<ArgumentNullException>();
    }

    private void WriteSettings(string contents)
        => File.WriteAllText(Path.Combine(_dir.FullName, CliConfigurationNames.LocalSettingsFileName), contents);
}
