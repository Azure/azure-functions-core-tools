// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Hosting;
using Xunit;

namespace Azure.Functions.Cli.Tests.Hosting;

public class WorkerRuntimeDetectorTests : IDisposable
{
    private readonly string _tempDir;

    public WorkerRuntimeDetectorTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"func-test-{Guid.NewGuid():N}");
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
    public void Detect_CsprojFile_ReturnsDotnetIsolated()
    {
        File.WriteAllText(Path.Combine(_tempDir, "MyApp.csproj"), "<Project/>");
        Assert.Equal("dotnet-isolated", WorkerRuntimeDetector.Detect(_tempDir));
    }

    [Fact]
    public void Detect_FsprojFile_ReturnsDotnetIsolated()
    {
        File.WriteAllText(Path.Combine(_tempDir, "MyApp.fsproj"), "<Project/>");
        Assert.Equal("dotnet-isolated", WorkerRuntimeDetector.Detect(_tempDir));
    }

    [Fact]
    public void Detect_PackageJson_ReturnsNode()
    {
        File.WriteAllText(Path.Combine(_tempDir, "package.json"), "{}");
        Assert.Equal("node", WorkerRuntimeDetector.Detect(_tempDir));
    }

    [Fact]
    public void Detect_RequirementsTxt_ReturnsPython()
    {
        File.WriteAllText(Path.Combine(_tempDir, "requirements.txt"), "azure-functions");
        Assert.Equal("python", WorkerRuntimeDetector.Detect(_tempDir));
    }

    [Fact]
    public void Detect_PyprojectToml_ReturnsPython()
    {
        File.WriteAllText(Path.Combine(_tempDir, "pyproject.toml"), "[tool]");
        Assert.Equal("python", WorkerRuntimeDetector.Detect(_tempDir));
    }

    [Fact]
    public void Detect_PomXml_ReturnsJava()
    {
        File.WriteAllText(Path.Combine(_tempDir, "pom.xml"), "<project/>");
        Assert.Equal("java", WorkerRuntimeDetector.Detect(_tempDir));
    }

    [Fact]
    public void Detect_BuildGradle_ReturnsJava()
    {
        File.WriteAllText(Path.Combine(_tempDir, "build.gradle"), "");
        Assert.Equal("java", WorkerRuntimeDetector.Detect(_tempDir));
    }

    [Fact]
    public void Detect_EmptyDirectory_ReturnsNull()
    {
        Assert.Null(WorkerRuntimeDetector.Detect(_tempDir));
    }

    [Fact]
    public void Detect_NonexistentDirectory_ReturnsNull()
    {
        var badPath = Path.Combine(_tempDir, "nonexistent");
        Assert.Null(WorkerRuntimeDetector.Detect(badPath));
    }

    [Fact]
    public void Detect_CsprojTakesPrecedenceOverPackageJson()
    {
        // If both .csproj and package.json exist (e.g., Blazor hybrid), .NET wins
        File.WriteAllText(Path.Combine(_tempDir, "MyApp.csproj"), "<Project/>");
        File.WriteAllText(Path.Combine(_tempDir, "package.json"), "{}");
        Assert.Equal("dotnet-isolated", WorkerRuntimeDetector.Detect(_tempDir));
    }

    [Fact]
    public void Detect_BuildGradleKts_ReturnsJava()
    {
        File.WriteAllText(Path.Combine(_tempDir, "build.gradle.kts"), "");
        Assert.Equal("java", WorkerRuntimeDetector.Detect(_tempDir));
    }

    [Fact]
    public void Detect_PackageJsonWithTsconfig_ReturnsNode()
    {
        File.WriteAllText(Path.Combine(_tempDir, "package.json"), "{}");
        File.WriteAllText(Path.Combine(_tempDir, "tsconfig.json"), "{}");
        Assert.Equal("node", WorkerRuntimeDetector.Detect(_tempDir));
    }
}
