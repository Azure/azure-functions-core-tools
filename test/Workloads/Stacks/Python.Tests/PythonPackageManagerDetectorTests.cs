// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

namespace Azure.Functions.Cli.Workloads.Python.Tests;

public class PythonPackageManagerDetectorTests : IDisposable
{
    private readonly DirectoryInfo _dir = Directory.CreateDirectory(
        Path.Combine(Path.GetTempPath(), "func-py-detect-" + Guid.NewGuid().ToString("N")));

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
    public void UvLock_takes_precedence_over_poetry_and_requirements()
    {
        Write("uv.lock", "version = 1");
        Write("poetry.lock", "");
        Write("requirements.txt", "azure-functions");
        Write("pyproject.toml", "[project]\nname = \"app\"\n\n[tool.uv]\n");

        Detect().Should().Be(PythonPackageManager.Uv);
    }

    [Fact]
    public void ToolUv_with_requirements_is_uv()
    {
        Write("pyproject.toml", "[project]\nname = \"app\"\n\n[tool.uv]\ndev-dependencies = []\n");
        Write("requirements.txt", "azure-functions");

        Detect().Should().Be(PythonPackageManager.Uv);
    }

    [Fact]
    public void ToolUv_subtable_is_uv()
    {
        Write("pyproject.toml", "[tool.uv.sources]  # local\nfoo = { path = \"../foo\" }\n");
        Write("requirements.txt", "azure-functions");

        Detect().Should().Be(PythonPackageManager.Uv);
    }

    [Fact]
    public void Project_without_uv_markers_is_pip_even_without_requirements()
    {
        Write("pyproject.toml", "[project]\nname = \"app\"\n");

        Detect().Should().Be(PythonPackageManager.Pip);
    }

    [Fact]
    public void Project_with_requirements_is_pip()
    {
        Write("pyproject.toml", "[project]\nname = \"app\"\n");
        Write("requirements.txt", "azure-functions");

        Detect().Should().Be(PythonPackageManager.Pip);
    }

    [Fact]
    public void PoetryLock_takes_precedence_over_tool_uv()
    {
        Write("pyproject.toml", "[project]\nname = \"app\"\n\n[tool.uv]\n");
        Write("poetry.lock", "");

        Detect().Should().Be(PythonPackageManager.Pip);
    }

    [Fact]
    public void Commented_out_tool_uv_is_pip()
    {
        Write("pyproject.toml", "# [tool.uv]\n[tool.black]\nline-length = 100\n");

        Detect().Should().Be(PythonPackageManager.Pip);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Unmanaged_tool_uv_is_pip_even_with_uv_lock(bool hasUvLock)
    {
        Write("pyproject.toml", "[project]\nname = \"app\"\n\n[tool.uv]\nmanaged = false  # opt out\n");
        if (hasUvLock)
        {
            Write("uv.lock", "version = 1");
        }

        Detect().Should().Be(PythonPackageManager.Pip);
    }

    [Fact]
    public void Managed_true_is_uv()
    {
        Write("pyproject.toml", "[tool.uv]\nmanaged = true\n");

        Detect().Should().Be(PythonPackageManager.Uv);
    }

    [Fact]
    public void Managed_false_outside_tool_uv_table_is_ignored()
    {
        Write("pyproject.toml", "[tool.uv]\n\n[tool.other]\nmanaged = false\n");

        Detect().Should().Be(PythonPackageManager.Uv);
    }

    [Theory]
    [InlineData("[tool.uv.pip]\nindex-url = \"https://example.test/simple\"\n")]
    [InlineData("[tool.uv.pip.extra]\nkey = 1\n")]
    public void Pip_interface_settings_are_pip(string pyProject)
    {
        Write("pyproject.toml", pyProject);

        Detect().Should().Be(PythonPackageManager.Pip);
    }

    [Fact]
    public void Index_array_table_is_uv()
    {
        Write("pyproject.toml", "[[tool.uv.index]]\nname = \"private\"\nurl = \"https://example.test/simple\"\n");

        Detect().Should().Be(PythonPackageManager.Uv);
    }

    [Fact]
    public void No_manifests_is_pip()
    {
        Detect().Should().Be(PythonPackageManager.Pip);
    }

    private PythonPackageManager Detect() => PythonPackageManagerDetector.Detect(_dir.FullName);

    private void Write(string name, string content) => File.WriteAllText(Path.Combine(_dir.FullName, name), content);
}
