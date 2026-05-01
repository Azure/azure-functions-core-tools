// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Commands;
using Xunit;

namespace Azure.Functions.Cli.Tests.Commands;

public class ProjectDetectorTests : IDisposable
{
    private readonly string _tempDir;

    public ProjectDetectorTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"func-projdetect-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
        {
            Directory.Delete(_tempDir, recursive: true);
        }
    }

    [Fact]
    public void DetectStackAndLanguage_CSharpProject_ReturnsDotnetCSharp()
    {
        File.WriteAllText(Path.Combine(_tempDir, "MyApp.csproj"), "<Project/>");

        var (stack, language) = ProjectDetector.DetectStackAndLanguage(_tempDir);

        Assert.Equal("dotnet", stack);
        Assert.Equal("C#", language);
    }

    [Fact]
    public void DetectStackAndLanguage_FSharpProject_ReturnsDotnetFSharp()
    {
        File.WriteAllText(Path.Combine(_tempDir, "MyApp.fsproj"), "<Project/>");

        var (stack, language) = ProjectDetector.DetectStackAndLanguage(_tempDir);

        Assert.Equal("dotnet", stack);
        Assert.Equal("F#", language);
    }

    [Fact]
    public void DetectStackAndLanguage_NodeProject_ReturnsNode()
    {
        File.WriteAllText(Path.Combine(_tempDir, "package.json"), "{}");

        var (stack, language) = ProjectDetector.DetectStackAndLanguage(_tempDir);

        Assert.Equal("node", stack);
        Assert.Null(language);
    }

    [Fact]
    public void DetectStackAndLanguage_PythonRequirements_ReturnsPython()
    {
        File.WriteAllText(Path.Combine(_tempDir, "requirements.txt"), "");

        var (stack, language) = ProjectDetector.DetectStackAndLanguage(_tempDir);

        Assert.Equal("python", stack);
        Assert.Null(language);
    }

    [Fact]
    public void DetectStackAndLanguage_PyProject_ReturnsPython()
    {
        File.WriteAllText(Path.Combine(_tempDir, "pyproject.toml"), "");

        var (stack, language) = ProjectDetector.DetectStackAndLanguage(_tempDir);

        Assert.Equal("python", stack);
        Assert.Null(language);
    }

    [Fact]
    public void DetectStackAndLanguage_PomXml_ReturnsJava()
    {
        File.WriteAllText(Path.Combine(_tempDir, "pom.xml"), "<project/>");

        var (stack, language) = ProjectDetector.DetectStackAndLanguage(_tempDir);

        Assert.Equal("java", stack);
        Assert.Null(language);
    }

    [Fact]
    public void DetectStackAndLanguage_BuildGradle_ReturnsJava()
    {
        File.WriteAllText(Path.Combine(_tempDir, "build.gradle"), "");

        var (stack, language) = ProjectDetector.DetectStackAndLanguage(_tempDir);

        Assert.Equal("java", stack);
        Assert.Null(language);
    }

    [Fact]
    public void DetectStackAndLanguage_ProfilePs1_ReturnsPowerShell()
    {
        File.WriteAllText(Path.Combine(_tempDir, "profile.ps1"), "");

        var (stack, language) = ProjectDetector.DetectStackAndLanguage(_tempDir);

        Assert.Equal("powershell", stack);
        Assert.Null(language);
    }

    [Fact]
    public void DetectStackAndLanguage_EmptyDir_ReturnsNulls()
    {
        var (stack, language) = ProjectDetector.DetectStackAndLanguage(_tempDir);

        Assert.Null(stack);
        Assert.Null(language);
    }

    [Fact]
    public void DetectStackAndLanguage_CSharpTakesPriorityOverNode()
    {
        // If both .csproj and package.json exist, .csproj wins
        File.WriteAllText(Path.Combine(_tempDir, "MyApp.csproj"), "<Project/>");
        File.WriteAllText(Path.Combine(_tempDir, "package.json"), "{}");

        var (stack, language) = ProjectDetector.DetectStackAndLanguage(_tempDir);

        Assert.Equal("dotnet", stack);
        Assert.Equal("C#", language);
    }

    [Fact]
    public void DetectStack_ReturnStackOnly()
    {
        File.WriteAllText(Path.Combine(_tempDir, "MyApp.fsproj"), "<Project/>");

        var stack = ProjectDetector.DetectStack(_tempDir);

        Assert.Equal("dotnet", stack);
    }

    [Fact]
    public void DetectStack_EmptyDir_ReturnsNull()
    {
        var stack = ProjectDetector.DetectStack(_tempDir);

        Assert.Null(stack);
    }
}
