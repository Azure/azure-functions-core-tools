// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Configuration;
using Xunit;

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

        Assert.Empty(snapshot.Values);
        Assert.Null(snapshot.WorkerRuntime);
        Assert.Null(snapshot.Host);
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

        Assert.Equal("python", snapshot.WorkerRuntime);
        Assert.Equal("UseDevelopmentStorage=true", snapshot.Values["AzureWebJobsStorage"]);
        Assert.False(snapshot.Values.ContainsKey("IgnoredNumber"));
        Assert.NotNull(snapshot.Host);
        Assert.Equal(7072, snapshot.Host!.LocalHttpPort);
        Assert.Equal("http://localhost,http://example", snapshot.Host.Cors);
        Assert.True(snapshot.Host.CorsCredentials);
    }

    [Fact]
    public void Get_MalformedJson_ReturnsEmptySnapshot()
    {
        WriteSettings("not json {");

        LocalSettingsSnapshot snapshot = _provider.Get(_dir);

        Assert.Empty(snapshot.Values);
        Assert.Null(snapshot.WorkerRuntime);
    }

    [Fact]
    public void Get_CachesSnapshotForDirectory()
    {
        WriteSettings("""{"Values":{"FUNCTIONS_WORKER_RUNTIME":"python"}}""");
        LocalSettingsSnapshot first = _provider.Get(_dir);

        WriteSettings("""{"Values":{"FUNCTIONS_WORKER_RUNTIME":"node"}}""");
        LocalSettingsSnapshot second = _provider.Get(_dir);

        Assert.Same(first, second);
        Assert.Equal("python", second.WorkerRuntime);
    }

    [Fact]
    public void Get_NullDirectory_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => _provider.Get(null!));
    }

    private void WriteSettings(string contents)
        => File.WriteAllText(Path.Combine(_dir.FullName, CliConfigurationNames.LocalSettingsFileName), contents);
}
