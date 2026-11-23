// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

namespace Azure.Functions.Cli.Workloads.Python.Tests;

public class UvLocatorTests : IDisposable
{
    private static readonly string _uvFileName = OperatingSystem.IsWindows() ? "uv.exe" : "uv";

    private readonly DirectoryInfo _dir = Directory.CreateDirectory(
        Path.Combine(Path.GetTempPath(), "func-py-uvloc-" + Guid.NewGuid().ToString("N")));

    public void Dispose()
    {
        try
        {
            _dir.Delete(recursive: true);
        }
        catch (IOException)
        {
        }
    }

    [Fact]
    public void UV_env_pointing_to_existing_file_wins_over_path()
    {
        string fromEnv = CreateFile("env-uv", _uvFileName);
        string pathDir = Path.GetDirectoryName(CreateFile("path-uv", _uvFileName))!;

        UvLocator.Find(fromEnv, pathDir).Should().Be(fromEnv);
    }

    [Fact]
    public void UV_env_pointing_to_missing_file_falls_through_to_path()
    {
        string onPath = CreateFile("path-uv", _uvFileName);

        UvLocator.Find(Path.Combine(_dir.FullName, "missing", _uvFileName), Path.GetDirectoryName(onPath))
            .Should().Be(onPath);
    }

    [Fact]
    public void Found_on_synthetic_path()
    {
        string emptyDir = Directory.CreateDirectory(Path.Combine(_dir.FullName, "empty")).FullName;
        string onPath = CreateFile("bin", _uvFileName);
        string path = string.Join(Path.PathSeparator, emptyDir, Path.GetDirectoryName(onPath));

        UvLocator.Find(null, path).Should().Be(onPath);
    }

    [Fact]
    public void Not_found_returns_null()
    {
        UvLocator.Find(null, _dir.FullName).Should().BeNull();
        UvLocator.Find(null, null).Should().BeNull();
    }

    [Fact]
    public void Uses_platform_executable_name()
    {
        // On Windows only uv.exe matches; elsewhere only the bare name does.
        string wrongName = OperatingSystem.IsWindows() ? "uv" : "uv.exe";
        string dir = Path.GetDirectoryName(CreateFile("bin", wrongName))!;

        UvLocator.Find(null, dir).Should().BeNull();

        string right = CreateFile("bin", _uvFileName);
        UvLocator.Find(null, dir).Should().Be(right);
    }

    private string CreateFile(string subDir, string name)
    {
        string dir = Directory.CreateDirectory(Path.Combine(_dir.FullName, subDir)).FullName;
        string path = Path.Combine(dir, name);
        File.WriteAllText(path, string.Empty);
        return path;
    }
}
