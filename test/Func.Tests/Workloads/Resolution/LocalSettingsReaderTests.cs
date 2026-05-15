// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Workloads.Resolution;
using Xunit;

namespace Azure.Functions.Cli.Tests.Workloads.Resolution;

public sealed class LocalSettingsReaderTests : IDisposable
{
    private readonly DirectoryInfo _dir = Directory.CreateTempSubdirectory("local-settings-reader-");
    private readonly LocalSettingsReader _reader = new();

    public void Dispose() => _dir.Delete(recursive: true);

    [Fact]
    public void ReadWorkerRuntime_FileMissing_ReturnsNull()
    {
        Assert.Null(_reader.ReadWorkerRuntime(_dir));
    }

    [Fact]
    public void ReadWorkerRuntime_KeyPresent_ReturnsValue()
    {
        WriteSettings("""{"Values":{"FUNCTIONS_WORKER_RUNTIME":"dotnet-isolated"}}""");

        Assert.Equal("dotnet-isolated", _reader.ReadWorkerRuntime(_dir));
    }

    [Fact]
    public void ReadWorkerRuntime_KeyMissing_ReturnsNull()
    {
        WriteSettings("""{"Values":{"AzureWebJobsStorage":"UseDevelopmentStorage=true"}}""");

        Assert.Null(_reader.ReadWorkerRuntime(_dir));
    }

    [Fact]
    public void ReadWorkerRuntime_EmptyValue_ReturnsNull()
    {
        WriteSettings("""{"Values":{"FUNCTIONS_WORKER_RUNTIME":"  "}}""");

        Assert.Null(_reader.ReadWorkerRuntime(_dir));
    }

    [Fact]
    public void ReadWorkerRuntime_ValuesMissing_ReturnsNull()
    {
        WriteSettings("""{"IsEncrypted":false}""");

        Assert.Null(_reader.ReadWorkerRuntime(_dir));
    }

    [Fact]
    public void ReadWorkerRuntime_NonStringValue_ReturnsNull()
    {
        WriteSettings("""{"Values":{"FUNCTIONS_WORKER_RUNTIME":42}}""");

        Assert.Null(_reader.ReadWorkerRuntime(_dir));
    }

    [Fact]
    public void ReadWorkerRuntime_MalformedJson_ReturnsNullDoesNotThrow()
    {
        WriteSettings("not json {");

        Assert.Null(_reader.ReadWorkerRuntime(_dir));
    }

    [Fact]
    public void ReadWorkerRuntime_NullDirectory_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => _reader.ReadWorkerRuntime(null!));
    }

    private void WriteSettings(string contents)
        => File.WriteAllText(Path.Combine(_dir.FullName, "local.settings.json"), contents);
}
