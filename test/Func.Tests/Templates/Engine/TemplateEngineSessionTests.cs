// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Templates.Engine;

namespace Azure.Functions.Cli.Tests.Templates.Engine;

public sealed class TemplateEngineSessionTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "func-template-session-tests", Guid.NewGuid().ToString("N"));

    private string Hive => Path.Combine(_root, "hive");

    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch
        {
            // best-effort cleanup of the temp hive
        }
    }

    [Fact]
    public void Settings_UseTheSuppliedSettingsLocation()
    {
        using TemplateEngineSession session = new(CommandContext("command"), Hive, []);

        session.Settings.Paths.GlobalSettingsDir.Should().Be(Hive);
    }

    [Fact]
    public void Settings_SeparateSessionsOnSharedHive_KeepTheirOwnContext()
    {
        using TemplateEngineSession node = new(ProjectContext("node-app", "node"), Hive, []);
        using TemplateEngineSession python = new(ProjectContext("python-app", "python"), Hive, []);

        HostParameter(node, FuncTemplateEngineHostParameters.Stack).Should().Be("node");
        HostParameter(python, FuncTemplateEngineHostParameters.Stack).Should().Be("python");
        HostParameter(node, "WorkingDirectory").Should().Be(DirectoryUnderRoot("node-app").Info.FullName);
        HostParameter(python, "WorkingDirectory").Should().Be(DirectoryUnderRoot("python-app").Info.FullName);
        node.Settings.Should().NotBeSameAs(python.Settings);
    }

    [Fact]
    public void Dispose_ThenUse_ThrowsObjectDisposedException()
    {
        TemplateEngineSession session = new(CommandContext("command"), Hive, []);

        session.Dispose();

        Action readSettings = () => _ = session.Settings;
        Action readPackageManager = () => _ = session.PackageManager;
        readSettings.Should().Throw<ObjectDisposedException>();
        readPackageManager.Should().Throw<ObjectDisposedException>();
    }

    [Fact]
    public void Dispose_CalledTwice_DoesNotThrow()
    {
        TemplateEngineSession session = new(CommandContext("command"), Hive, []);
        session.Dispose();

        Action act = session.Dispose;

        act.Should().NotThrow();
    }

    [Fact]
    public void Constructor_NullContext_Throws()
    {
        Action act = () => _ = new TemplateEngineSession(null!, Hive, []);

        act.Should().Throw<ArgumentNullException>().WithParameterName("context");
    }

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    public void Constructor_BlankSettingsLocation_Throws(string settingsLocation)
    {
        Action act = () => _ = new TemplateEngineSession(CommandContext("command"), settingsLocation, []);

        act.Should().Throw<ArgumentException>().WithParameterName("settingsLocation");
    }

    [Fact]
    public void Constructor_NullComponents_Throws()
    {
        Action act = () => _ = new TemplateEngineSession(CommandContext("command"), Hive, null!);

        act.Should().Throw<ArgumentNullException>().WithParameterName("components");
    }

    private static string? HostParameter(TemplateEngineSession session, string name)
    {
        session.Settings.Host.TryGetHostParamDefault(name, out string? value).Should().BeTrue();
        return value;
    }

    private WorkingDirectory DirectoryUnderRoot(string name) => WorkingDirectory.FromExplicit(Path.Combine(_root, name));

    private TemplateEngineContext CommandContext(string directory) => new(DirectoryUnderRoot(directory));

    private TemplateEngineContext ProjectContext(string directory, string stack)
        => new(DirectoryUnderRoot(directory), new TemplateEngineProjectContext(DirectoryUnderRoot(directory), stack));
}
