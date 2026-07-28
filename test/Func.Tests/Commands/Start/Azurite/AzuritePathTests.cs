// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Commands.Start.Azurite;

namespace Azure.Functions.Cli.Tests.Commands.Start.Azurite;

public class AzuritePathTests
{
    [Fact]
    public void AreSameDirectory_TrueForIdenticalPaths()
    {
        string path = Path.Combine(Path.GetTempPath(), "azurite", "data");

        AzuritePath.AreSameDirectory(path, path).Should().BeTrue();
    }

    [Fact]
    public void AreSameDirectory_TrueIgnoringTrailingSeparator()
    {
        string path = Path.Combine(Path.GetTempPath(), "azurite", "data");

        AzuritePath.AreSameDirectory(path, path + Path.DirectorySeparatorChar).Should().BeTrue();
    }

    [Fact]
    public void AreSameDirectory_TrueForNonNormalizedPath()
    {
        string baseDirectory = Path.Combine(Path.GetTempPath(), "azurite", "data");
        string indirect = Path.Combine(Path.GetTempPath(), "azurite", "sub", "..", "data");

        AzuritePath.AreSameDirectory(baseDirectory, indirect).Should().BeTrue();
    }

    [Fact]
    public void AreSameDirectory_FalseForDifferentPaths()
    {
        string first = Path.Combine(Path.GetTempPath(), "azurite", "data");
        string second = Path.Combine(Path.GetTempPath(), "azurite", "other");

        AzuritePath.AreSameDirectory(first, second).Should().BeFalse();
    }

    [Fact]
    public void AreSameDirectory_UsesPlatformCaseSensitivity()
    {
        string original = Path.Combine(Path.GetTempPath(), "Azurite", "Data");
        string uppercased = original.ToUpperInvariant();

        AzuritePath.AreSameDirectory(original, uppercased).Should().Be(OperatingSystem.IsWindows());
    }
}
