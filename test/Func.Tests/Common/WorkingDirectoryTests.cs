// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Common;

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

        workingDirectory.WasExplicit.Should().BeFalse();
        workingDirectory.Info.FullName.Should().Be(Directory.GetCurrentDirectory());
    }

    [Fact]
    public void FromExplicit_ReturnsRequestedPathAndIsExplicit()
    {
        var workingDirectory = WorkingDirectory.FromExplicit(_tempDir);

        workingDirectory.WasExplicit.Should().BeTrue();
        workingDirectory.Info.FullName.Should().Be(_tempDir);
    }

    [Fact]
    public void Exists_ReflectsDirectoryState()
    {
        var workingDirectory = WorkingDirectory.FromExplicit(_tempDir);
        workingDirectory.Exists.Should().BeFalse();

        Directory.CreateDirectory(_tempDir);
        workingDirectory.Info.Refresh();
        workingDirectory.Exists.Should().BeTrue();
    }

    [Fact]
    public void CreateIfNotExists_CreatesMissingDirectory()
    {
        var workingDirectory = WorkingDirectory.FromExplicit(_tempDir);
        workingDirectory.Exists.Should().BeFalse();

        workingDirectory.CreateIfNotExists();

        Directory.Exists(_tempDir).Should().BeTrue();
        workingDirectory.Exists.Should().BeTrue();
    }

    [Fact]
    public void CreateIfNotExists_NoOpWhenAlreadyExists()
    {
        Directory.CreateDirectory(_tempDir);
        var workingDirectory = WorkingDirectory.FromExplicit(_tempDir);

        workingDirectory.CreateIfNotExists();
        workingDirectory.CreateIfNotExists();

        Directory.Exists(_tempDir).Should().BeTrue();
    }
}
