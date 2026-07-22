// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Configuration;

namespace Azure.Functions.Cli.Tests.Configuration;

public sealed class LocalSettingsProviderTests : IDisposable
{
    private readonly DirectoryInfo _dir = Directory.CreateTempSubdirectory("local-settings-provider-");
    private readonly LocalSettingsProvider _provider = new();

    public void Dispose() => _dir.Delete(recursive: true);

    [Fact]
    public void Get_FileMissing_ReturnsEmptySnapshot()
    {
        LocalSettingsSnapshot snapshot = _provider.Get(_dir);

        snapshot.Values.Should().BeEmpty();
        snapshot.WorkerRuntime.Should().BeNull();
        snapshot.Host.Should().BeNull();
    }

    [Fact]
    public void Get_ValuesAndHostPresent_ReadsSnapshot()
    {
        WriteSettings(
            """
            {
              "Values": {
                "FUNCTIONS_WORKER_RUNTIME": "python",
                "AzureWebJobsStorage": "UseDevelopmentStorage=true",
                "IgnoredNumber": 42
              },
              "Host": {
                "LocalHttpPort": 7072,
                "CORS": "http://localhost,http://example",
                "CORSCredentials": true
              }
            }
            """);

        LocalSettingsSnapshot snapshot = _provider.Get(_dir);

        snapshot.WorkerRuntime.Should().Be("python");
        snapshot.Values["AzureWebJobsStorage"].Should().Be("UseDevelopmentStorage=true");
        snapshot.Values.ContainsKey("IgnoredNumber").Should().BeFalse();
        snapshot.Host.Should().NotBeNull();
        snapshot.Host!.LocalHttpPort.Should().Be(7072);
        snapshot.Host.Cors.Should().Be("http://localhost,http://example");
        snapshot.Host.CorsCredentials.Should().BeTrue();
    }

    [Fact]
    public void Get_MalformedJson_ReturnsEmptySnapshot()
    {
        WriteSettings("not json {");

        LocalSettingsSnapshot snapshot = _provider.Get(_dir);

        snapshot.Values.Should().BeEmpty();
        snapshot.WorkerRuntime.Should().BeNull();
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
