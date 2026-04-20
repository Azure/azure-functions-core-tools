// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Hosting;
using Xunit;

namespace Azure.Functions.Cli.Tests.Hosting;

public class DotEnvLoaderTests : IDisposable
{
    private readonly string _tempDir;

    public DotEnvLoaderTests()
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
    public void Load_BasicKeyValue()
    {
        var envFile = Path.Combine(_tempDir, ".env");
        File.WriteAllText(envFile, "MY_KEY=my_value\nOTHER_KEY=other_value");

        var env = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        DotEnvLoader.Load(envFile, env);

        Assert.Equal("my_value", env["MY_KEY"]);
        Assert.Equal("other_value", env["OTHER_KEY"]);
    }

    [Fact]
    public void Load_SkipsCommentsAndEmptyLines()
    {
        var envFile = Path.Combine(_tempDir, ".env");
        File.WriteAllText(envFile, "# comment\n\nKEY=value\n  # another comment\n");

        var env = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        DotEnvLoader.Load(envFile, env);

        Assert.Single(env);
        Assert.Equal("value", env["KEY"]);
    }

    [Fact]
    public void Load_HandlesQuotedValues()
    {
        var envFile = Path.Combine(_tempDir, ".env");
        File.WriteAllText(envFile, "KEY1=\"hello world\"\nKEY2='single quoted'");

        var env = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        DotEnvLoader.Load(envFile, env);

        Assert.Equal("hello world", env["KEY1"]);
        Assert.Equal("single quoted", env["KEY2"]);
    }

    [Fact]
    public void Load_HandlesExportPrefix()
    {
        var envFile = Path.Combine(_tempDir, ".env");
        File.WriteAllText(envFile, "export MY_VAR=exported_value");

        var env = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        DotEnvLoader.Load(envFile, env);

        Assert.Equal("exported_value", env["MY_VAR"]);
    }

    [Fact]
    public void Load_DoesNotOverwriteByDefault()
    {
        var envFile = Path.Combine(_tempDir, ".env");
        File.WriteAllText(envFile, "KEY=from_file");

        var env = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["KEY"] = "existing"
        };
        DotEnvLoader.Load(envFile, env);

        Assert.Equal("existing", env["KEY"]);
    }

    [Fact]
    public void Load_OverwritesWhenFlagSet()
    {
        var envFile = Path.Combine(_tempDir, ".env");
        File.WriteAllText(envFile, "KEY=from_file");

        var env = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["KEY"] = "existing"
        };
        DotEnvLoader.Load(envFile, env, overwrite: true);

        Assert.Equal("from_file", env["KEY"]);
    }

    [Fact]
    public void Load_NonexistentFile_DoesNothing()
    {
        var env = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        DotEnvLoader.Load(Path.Combine(_tempDir, "missing.env"), env);
        Assert.Empty(env);
    }

    [Fact]
    public void Load_SkipsInvalidLines()
    {
        var envFile = Path.Combine(_tempDir, ".env");
        File.WriteAllText(envFile, "GOOD=value\nno_equals_sign\n=no_key\nALSO_GOOD=ok");

        var env = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        DotEnvLoader.Load(envFile, env);

        Assert.Equal(2, env.Count);
        Assert.Equal("value", env["GOOD"]);
        Assert.Equal("ok", env["ALSO_GOOD"]);
    }

    [Fact]
    public void Load_HandlesValueWithEquals()
    {
        var envFile = Path.Combine(_tempDir, ".env");
        File.WriteAllText(envFile, "CONN_STRING=AccountName=test;AccountKey=abc123==");

        var env = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        DotEnvLoader.Load(envFile, env);

        Assert.Equal("AccountName=test;AccountKey=abc123==", env["CONN_STRING"]);
    }
}
