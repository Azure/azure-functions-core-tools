// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Templates.Engine;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace Azure.Functions.Cli.Tests.Templates.Engine;

public class FuncTemplateEngineSessionTests : IDisposable
{
    private readonly string _root;

    public FuncTemplateEngineSessionTests()
    {
        _root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    [Fact]
    public void TouchingSession_RelocatesHiveAndNeverTouchesDotnetNewState()
    {
        var funcHome = Path.Combine(_root, "func-home");
        var userProfile = Path.Combine(_root, "user-profile");
        Directory.CreateDirectory(userProfile);

        var paths = new FuncTemplateEnginePaths(funcHome, userProfile, "1.0.0-test");
        using var host = new FuncTemplateEngineHost(NullLoggerFactory.Instance, Version(), []);
        using var session = new FuncTemplateEngineSession(host, paths);

        // Accessing the members forces the lazy engine environment + package
        // manager to build — the only place the engine writes settings dirs.
        _ = session.Settings;
        _ = session.PackageManager;

        Directory.Exists(Path.Combine(userProfile, ".templateengine")).Should()
            .BeFalse("the engine must not fall back to the shared dotnet new settings dir");
        Directory.Exists(paths.HostVersionSettingsDir).Should()
            .BeTrue("the relocated hive is created under the func home instead");
    }

    [Fact]
    public void Settings_IsBuiltLazilyAndCachedAcrossAccesses()
    {
        var paths = new FuncTemplateEnginePaths(Path.Combine(_root, "h"), Path.Combine(_root, "p"), "1.0.0-test");
        using var host = new FuncTemplateEngineHost(NullLoggerFactory.Instance, Version(), []);
        using var session = new FuncTemplateEngineSession(host, paths);

        var first = session.Settings;
        var second = session.Settings;

        first.Should().BeSameAs(second);
    }

    [Fact]
    public void Members_ThrowAfterDispose()
    {
        var paths = new FuncTemplateEnginePaths(Path.Combine(_root, "h"), Path.Combine(_root, "p"), "1.0.0-test");
        using var host = new FuncTemplateEngineHost(NullLoggerFactory.Instance, Version(), []);
        var session = new FuncTemplateEngineSession(host, paths);

        session.Dispose();

        FluentActions.Invoking(() => _ = session.Settings).Should().Throw<ObjectDisposedException>();
    }

    [Fact]
    public void Constructor_ThrowsForNullHost()
    {
        var paths = new FuncTemplateEnginePaths(Path.Combine(_root, "h"), Path.Combine(_root, "p"), "1.0.0-test");

        FluentActions.Invoking(() => new FuncTemplateEngineSession(null!, paths)).Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Constructor_ThrowsForNullPaths()
    {
        using var host = new FuncTemplateEngineHost(NullLoggerFactory.Instance, Version(), []);

        FluentActions.Invoking(() => new FuncTemplateEngineSession(host, null!)).Should().Throw<ArgumentNullException>();
    }

    private static IFuncTemplateEngineVersion Version(string value = "1.0.0-test")
    {
        var version = Substitute.For<IFuncTemplateEngineVersion>();
        version.Version.Returns(value);
        return version;
    }
}
