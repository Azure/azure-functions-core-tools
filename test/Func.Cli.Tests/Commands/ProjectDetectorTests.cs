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
    public void DetectRuntimeAndLanguage_CSharpProject_ReturnsDotnetCSharp()
    {
        File.WriteAllText(Path.Combine(_tempDir, "MyApp.csproj"), "<Project/>");

        var (runtime, language) = ProjectDetector.DetectRuntimeAndLanguage(_tempDir);

        Assert.Equal("dotnet", runtime);
        Assert.Equal("C#", language);
    }

    [Fact]
    public void DetectRuntimeAndLanguage_FSharpProject_ReturnsDotnetFSharp()
    {
        File.WriteAllText(Path.Combine(_tempDir, "MyApp.fsproj"), "<Project/>");

        var (runtime, language) = ProjectDetector.DetectRuntimeAndLanguage(_tempDir);

        Assert.Equal("dotnet", runtime);
        Assert.Equal("F#", language);
    }

    [Fact]
    public void DetectRuntimeAndLanguage_NodeProject_ReturnsNode()
    {
        File.WriteAllText(Path.Combine(_tempDir, "package.json"), "{}");

        var (runtime, language) = ProjectDetector.DetectRuntimeAndLanguage(_tempDir);

        Assert.Equal("node", runtime);
        Assert.Null(language);
    }

    [Fact]
    public void DetectRuntimeAndLanguage_PythonRequirements_ReturnsPython()
    {
        File.WriteAllText(Path.Combine(_tempDir, "requirements.txt"), "");

        var (runtime, language) = ProjectDetector.DetectRuntimeAndLanguage(_tempDir);

        Assert.Equal("python", runtime);
        Assert.Null(language);
    }

    [Fact]
    public void DetectRuntimeAndLanguage_PyProject_ReturnsPython()
    {
        File.WriteAllText(Path.Combine(_tempDir, "pyproject.toml"), "");

        var (runtime, language) = ProjectDetector.DetectRuntimeAndLanguage(_tempDir);

        Assert.Equal("python", runtime);
        Assert.Null(language);
    }

    [Fact]
    public void DetectRuntimeAndLanguage_PomXml_ReturnsJava()
    {
        File.WriteAllText(Path.Combine(_tempDir, "pom.xml"), "<project/>");

        var (runtime, language) = ProjectDetector.DetectRuntimeAndLanguage(_tempDir);

        Assert.Equal("java", runtime);
        Assert.Null(language);
    }

    [Fact]
    public void DetectRuntimeAndLanguage_BuildGradle_ReturnsJava()
    {
        File.WriteAllText(Path.Combine(_tempDir, "build.gradle"), "");

        var (runtime, language) = ProjectDetector.DetectRuntimeAndLanguage(_tempDir);

        Assert.Equal("java", runtime);
        Assert.Null(language);
    }

    [Fact]
    public void DetectRuntimeAndLanguage_ProfilePs1_ReturnsPowerShell()
    {
        File.WriteAllText(Path.Combine(_tempDir, "profile.ps1"), "");

        var (runtime, language) = ProjectDetector.DetectRuntimeAndLanguage(_tempDir);

        Assert.Equal("powershell", runtime);
        Assert.Null(language);
    }

    [Fact]
    public void DetectRuntimeAndLanguage_EmptyDir_ReturnsNulls()
    {
        var (runtime, language) = ProjectDetector.DetectRuntimeAndLanguage(_tempDir);

        Assert.Null(runtime);
        Assert.Null(language);
    }

    [Fact]
    public void DetectRuntimeAndLanguage_CSharpTakesPriorityOverNode()
    {
        // If both .csproj and package.json exist, .csproj wins
        File.WriteAllText(Path.Combine(_tempDir, "MyApp.csproj"), "<Project/>");
        File.WriteAllText(Path.Combine(_tempDir, "package.json"), "{}");

        var (runtime, language) = ProjectDetector.DetectRuntimeAndLanguage(_tempDir);

        Assert.Equal("dotnet", runtime);
        Assert.Equal("C#", language);
    }

    [Fact]
    public void DetectRuntime_ReturnRuntimeOnly()
    {
        File.WriteAllText(Path.Combine(_tempDir, "MyApp.fsproj"), "<Project/>");

        var runtime = ProjectDetector.DetectRuntime(_tempDir);

        Assert.Equal("dotnet", runtime);
    }

    [Fact]
    public void DetectRuntime_EmptyDir_ReturnsNull()
    {
        var runtime = ProjectDetector.DetectRuntime(_tempDir);

        Assert.Null(runtime);
    }
}
