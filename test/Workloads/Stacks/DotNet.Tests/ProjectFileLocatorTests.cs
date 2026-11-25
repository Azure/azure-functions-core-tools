// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

namespace Azure.Functions.Cli.Workloads.DotNet.Tests;

public sealed class ProjectFileLocatorTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "func-locator-" + Guid.NewGuid().ToString("N"));
    private readonly ProjectFileLocator _locator = new();

    public ProjectFileLocatorTests() => Directory.CreateDirectory(_root);

    public void Dispose() => Directory.Delete(_root, recursive: true);

    [Fact]
    public void FindProjectFilesAtOrAbove_NearestDirectoryWins_SortedOrdinally()
    {
        string nested = Directory.CreateDirectory(Path.Combine(_root, "src", "app")).FullName;
        Touch(_root, "Root.csproj");
        Touch(nested, "b.csproj", "B.fsproj", "Program.cs", "notes.projx");

        IReadOnlyList<string> result = _locator.FindProjectFilesAtOrAbove(nested, new HashSet<string>());

        result.Should().Equal(Path.Combine(nested, "B.fsproj"), Path.Combine(nested, "b.csproj"));
    }

    [Fact]
    public void FindProjectFilesAtOrAbove_FilteredOut_ContinuesToParent()
    {
        string nested = Directory.CreateDirectory(Path.Combine(_root, "app")).FullName;
        Touch(_root, "Root.csproj");
        Touch(nested, "App.fsproj");

        IReadOnlyList<string> result = _locator.FindProjectFilesAtOrAbove(
            nested, new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ".CSPROJ" });

        result.Should().Equal(Path.Combine(_root, "Root.csproj"));
    }

    [Fact]
    public void FindProjectFilesAtOrAbove_MissingStartDirectory_SearchesExistingAncestors()
    {
        Touch(_root, "Root.csproj");

        IReadOnlyList<string> result = _locator.FindProjectFilesAtOrAbove(
            Path.Combine(_root, "missing", "deeper"), new HashSet<string>());

        result.Should().Equal(Path.Combine(_root, "Root.csproj"));
    }

    [Fact]
    public void FileExists_ReflectsFilesystem()
    {
        Touch(_root, "App.csproj");

        _locator.FileExists(Path.Combine(_root, "App.csproj")).Should().BeTrue();
        _locator.FileExists(Path.Combine(_root, "Missing.csproj")).Should().BeFalse();
    }

    private static void Touch(string directory, params string[] names)
    {
        foreach (string name in names)
        {
            File.WriteAllText(Path.Combine(directory, name), string.Empty);
        }
    }
}
