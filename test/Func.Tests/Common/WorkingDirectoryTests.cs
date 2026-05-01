// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Common;
using Xunit;

namespace Azure.Functions.Cli.Tests.Common;

public class WorkingDirectoryTests : IDisposable
{
    private readonly string _tempDir;

    public WorkingDirectoryTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"func-loc-{Guid.NewGuid():N}");
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
        {
            Directory.Delete(_tempDir, recursive: true);
        }
    }

    [Fact]
    public void FromCwd_ReturnsCurrentDirectoryAndIsNotExplicit()
    {
        var workingDirectory = WorkingDirectory.FromCwd();

        Assert.False(workingDirectory.WasExplicit);
        Assert.Equal(Directory.GetCurrentDirectory(), workingDirectory.Info.FullName);
    }

    [Fact]
    public void FromExplicit_ReturnsRequestedPathAndIsExplicit()
    {
        var workingDirectory = WorkingDirectory.FromExplicit(_tempDir);

        Assert.True(workingDirectory.WasExplicit);
        Assert.Equal(_tempDir, workingDirectory.Info.FullName);
    }

    [Fact]
    public void Exists_ReflectsDirectoryState()
    {
        var workingDirectory = WorkingDirectory.FromExplicit(_tempDir);
        Assert.False(workingDirectory.Exists);

        Directory.CreateDirectory(_tempDir);
        workingDirectory.Info.Refresh();
        Assert.True(workingDirectory.Exists);
    }

    [Fact]
    public void CreateIfNotExists_CreatesMissingDirectory()
    {
        var workingDirectory = WorkingDirectory.FromExplicit(_tempDir);
        Assert.False(workingDirectory.Exists);

        workingDirectory.CreateIfNotExists();

        Assert.True(Directory.Exists(_tempDir));
        Assert.True(workingDirectory.Exists);
    }

    [Fact]
    public void CreateIfNotExists_NoOpWhenAlreadyExists()
    {
        Directory.CreateDirectory(_tempDir);
        var workingDirectory = WorkingDirectory.FromExplicit(_tempDir);

        workingDirectory.CreateIfNotExists();
        workingDirectory.CreateIfNotExists();

        Assert.True(Directory.Exists(_tempDir));
    }
}
