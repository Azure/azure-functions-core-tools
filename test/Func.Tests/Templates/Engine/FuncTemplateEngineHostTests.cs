// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Templates.Engine;

namespace Azure.Functions.Cli.Tests.Templates.Engine;

public class FuncTemplateEngineHostTests
{
    private const string BundleId = "Microsoft.Azure.Functions.ExtensionBundle";

    private static readonly string _root = Path.Combine(Path.GetTempPath(), "func-host-tests");
    private static readonly WorkingDirectory _commandDirectory = WorkingDirectory.FromExplicit(Path.Combine(_root, "command"));
    private static readonly WorkingDirectory _projectRoot = WorkingDirectory.FromExplicit(Path.Combine(_root, "project"));

    public static TheoryData<string, string> ResolvedParameters => new()
    {
        { FuncTemplateEngineHostParameters.ProjectRoot, _projectRoot.Info.FullName },
        { FuncTemplateEngineHostParameters.Stack, "node" },
        { FuncTemplateEngineHostParameters.Language, "TypeScript" },
        { FuncTemplateEngineHostParameters.BundleId, BundleId },
        { FuncTemplateEngineHostParameters.BundleVersion, "4.35.0" },
    };

    public static TheoryData<string> FuncParameters =>
    [
        FuncTemplateEngineHostParameters.ProjectRoot,
        FuncTemplateEngineHostParameters.Stack,
        FuncTemplateEngineHostParameters.Language,
        FuncTemplateEngineHostParameters.BundleId,
        FuncTemplateEngineHostParameters.BundleVersion,
    ];

    [Fact]
    public void TryGetHostParamDefault_WorkingDirectory_ReturnsCommandDirectory()
    {
        using FuncTemplateEngineHost host = new(new TemplateEngineContext(_commandDirectory));

        bool found = host.TryGetHostParamDefault("WorkingDirectory", out string? value);

        found.Should().BeTrue();
        value.Should().Be(_commandDirectory.Info.FullName).And.NotBe(Environment.CurrentDirectory);
    }

    [Fact]
    public void TryGetHostParamDefault_DirectoriesWithTrailingSeparator_ReturnPathsWithoutIt()
    {
        string commandPath = Path.Combine(_root, "typed-command");
        string projectPath = Path.Combine(_root, "typed-project");
        TemplateEngineContext context = new(
            WorkingDirectory.FromExplicit(commandPath + Path.DirectorySeparatorChar),
            new TemplateEngineProjectContext(WorkingDirectory.FromExplicit(projectPath + Path.DirectorySeparatorChar), "node"));
        using FuncTemplateEngineHost host = new(context);

        host.TryGetHostParamDefault("WorkingDirectory", out string? workingDirectory).Should().BeTrue();
        host.TryGetHostParamDefault(FuncTemplateEngineHostParameters.ProjectRoot, out string? projectRoot).Should().BeTrue();

        workingDirectory.Should().Be(new DirectoryInfo(commandPath).FullName);
        projectRoot.Should().Be(new DirectoryInfo(projectPath).FullName);
    }

    [Theory]
    [MemberData(nameof(ResolvedParameters))]
    public void TryGetHostParamDefault_ResolvedContext_ReturnsExactContextValue(string parameter, string expected)
    {
        TemplateEngineContext context = new(
            _commandDirectory,
            new TemplateEngineProjectContext(_projectRoot, "node", "TypeScript"),
            new TemplateEngineBundleContext(BundleId, "4.35.0"));
        using FuncTemplateEngineHost host = new(context);

        bool found = host.TryGetHostParamDefault(parameter, out string? value);

        found.Should().BeTrue();
        value.Should().Be(expected);
    }

    [Theory]
    [MemberData(nameof(FuncParameters))]
    public void TryGetHostParamDefault_UnresolvedContext_ReportsParameterUnavailable(string parameter)
    {
        using FuncTemplateEngineHost host = new(new TemplateEngineContext(_commandDirectory));

        bool found = host.TryGetHostParamDefault(parameter, out string? value);

        found.Should().BeFalse();
        value.Should().BeNull();
    }

    [Fact]
    public void TryGetHostParamDefault_ProjectWithoutLanguage_ReportsLanguageUnavailable()
    {
        TemplateEngineContext context = new(_commandDirectory, new TemplateEngineProjectContext(_projectRoot, "python"));
        using FuncTemplateEngineHost host = new(context);

        bool found = host.TryGetHostParamDefault(FuncTemplateEngineHostParameters.Language, out string? value);

        found.Should().BeFalse();
        value.Should().BeNull();
    }

    [Fact]
    public void Constructor_NullContext_Throws()
    {
        Action act = () => _ = new FuncTemplateEngineHost(null!);

        act.Should().Throw<ArgumentNullException>().WithParameterName("context");
    }
}
