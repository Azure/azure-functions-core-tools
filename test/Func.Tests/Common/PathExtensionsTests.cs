// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Common;

namespace Azure.Functions.Cli.Tests.Common;

public sealed class PathExtensionsTests
{
    private readonly string _baseDirectory = Path.Combine(Path.GetTempPath(), $"func-path-test-{Guid.NewGuid():N}");

    [Fact]
    public void NormalizePath_RejectsBaseDirectoryEvenWhenItIsAVolumeRoot()
    {
        string baseDirectory = Path.GetPathRoot(_baseDirectory)!;

        FluentActions.Invoking(() => Path.NormalizePath(".", baseDirectory))
            .Should().ThrowExactly<ArgumentException>()
            .WithMessage("*inside the base directory*");
    }

    [Fact]
    public void NormalizePath_UsesPlatformCaseComparison()
    {
        string baseDirectory = Path.Combine(_baseDirectory, "package");
        const string path = "../PACKAGE/templates";

        if (OperatingSystem.IsWindows())
        {
            Path.NormalizePath(path, baseDirectory).Should().Be("templates");
        }
        else
        {
            FluentActions.Invoking(() => Path.NormalizePath(path, baseDirectory))
                .Should().ThrowExactly<ArgumentException>()
                .WithMessage("*inside the base directory*");
        }
    }

    [Fact]
    public void NormalizePath_NormalizesBaseDirectoryAndTrailingSeparators()
    {
        string baseDirectory = Path.Combine(_baseDirectory, "intermediate", "..") + Path.DirectorySeparatorChar;

        Path.NormalizePath("templates///", baseDirectory).Should().Be("templates");
    }

    [Fact]
    public void NormalizePath_RejectsNullPath()
    {
        FluentActions.Invoking(() => Path.NormalizePath(null!, _baseDirectory))
            .Should().ThrowExactly<ArgumentNullException>();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    public void NormalizePath_RejectsInvalidBaseDirectory(string? baseDirectory)
    {
        FluentActions.Invoking(() => Path.NormalizePath("templates", baseDirectory!))
            .Should().Throw<ArgumentException>();
    }
}
