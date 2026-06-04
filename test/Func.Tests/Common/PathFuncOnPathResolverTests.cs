// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Common;
using NSubstitute;
using Xunit;

namespace Azure.Functions.Cli.Tests.Common;

public class PathFuncOnPathResolverTests : IDisposable
{
    private readonly string _root;

    public PathFuncOnPathResolverTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "func-path-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    [Fact]
    public void EmptyPath_ReturnsNull()
    {
        IProcessEnvironment env = Substitute.For<IProcessEnvironment>();
        env.Get("PATH").Returns((string?)null);

        var resolver = new PathFuncOnPathResolver(env);

        Assert.Null(resolver.ResolveFuncOnPath());
    }

    [Fact]
    public void NoFuncOnAnyPathEntry_ReturnsNull()
    {
        string dir = MakeDir("empty");
        IProcessEnvironment env = Substitute.For<IProcessEnvironment>();
        env.Get("PATH").Returns(dir);

        var resolver = new PathFuncOnPathResolver(env);

        Assert.Null(resolver.ResolveFuncOnPath());
    }

    [Fact]
    public void FuncFoundInFirstDir_ReturnsThatPath()
    {
        string dir = MakeDir("with-func");
        string expected = WriteFuncExecutable(dir);
        IProcessEnvironment env = Substitute.For<IProcessEnvironment>();
        env.Get("PATH").Returns(dir);

        var resolver = new PathFuncOnPathResolver(env);

        Assert.Equal(Path.GetFullPath(expected), resolver.ResolveFuncOnPath());
    }

    [Fact]
    public void EarlierEntryWins_OverLater()
    {
        string first = MakeDir("first");
        string second = MakeDir("second");
        string expected = WriteFuncExecutable(first);
        WriteFuncExecutable(second);

        IProcessEnvironment env = Substitute.For<IProcessEnvironment>();
        env.Get("PATH").Returns(string.Join(Path.PathSeparator, first, second));

        var resolver = new PathFuncOnPathResolver(env);

        Assert.Equal(Path.GetFullPath(expected), resolver.ResolveFuncOnPath());
    }

    [Fact]
    public void MalformedEntries_AreSkipped()
    {
        string good = MakeDir("good");
        string expected = WriteFuncExecutable(good);
        IProcessEnvironment env = Substitute.For<IProcessEnvironment>();
        env.Get("PATH").Returns(string.Join(Path.PathSeparator, string.Empty, "   ", good));

        var resolver = new PathFuncOnPathResolver(env);

        Assert.Equal(Path.GetFullPath(expected), resolver.ResolveFuncOnPath());
    }

    [Fact]
    public void Windows_HonoursPathExt()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        string dir = MakeDir("npm-v4");
        string expected = Path.Combine(dir, "func.cmd");
        File.WriteAllText(expected, "@echo off");

        IProcessEnvironment env = Substitute.For<IProcessEnvironment>();
        env.Get("PATH").Returns(dir);
        env.Get("PATHEXT").Returns(".COM;.EXE;.CMD");

        var resolver = new PathFuncOnPathResolver(env);

        Assert.Equal(Path.GetFullPath(expected), resolver.ResolveFuncOnPath());
    }

    [Fact]
    public void Unix_NonExecutable_IsSkipped()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        string dir = MakeDir("not-exec");
        string nonExec = Path.Combine(dir, "func");
        File.WriteAllText(nonExec, "#!/bin/sh");
        File.SetUnixFileMode(nonExec, UnixFileMode.UserRead);

        IProcessEnvironment env = Substitute.For<IProcessEnvironment>();
        env.Get("PATH").Returns(dir);

        var resolver = new PathFuncOnPathResolver(env);

        Assert.Null(resolver.ResolveFuncOnPath());
    }

    [Fact]
    public void NullEnvironment_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => new PathFuncOnPathResolver(null!));
    }

    private string MakeDir(string name)
    {
        string dir = Path.Combine(_root, name);
        Directory.CreateDirectory(dir);
        return dir;
    }

    private static string WriteFuncExecutable(string dir)
    {
        string name = OperatingSystem.IsWindows() ? "func.exe" : "func";
        string path = Path.Combine(dir, name);
        File.WriteAllText(path, string.Empty);
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserExecute);
        }
        return path;
    }
}
