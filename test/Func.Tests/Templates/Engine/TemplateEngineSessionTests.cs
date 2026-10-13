// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Reflection;
using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Templates.Engine;
using Microsoft.TemplateEngine.Abstractions;
using Microsoft.TemplateEngine.Abstractions.Constraints;
using NuGet.Versioning;

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
    public void Constructor_RegistersRunnableProjectsAndFuncConstraintComponents()
    {
        using TemplateEngineSession session = new(CommandContext("command"), Hive);

        // RunnableProjects generator components are registered, so the host can
        // load and render standard template.json packages.
        session.Settings.Components.OfType<IGenerator>().Should().NotBeEmpty();

        // Every member of the func constraint factory component set is
        // registered on the host under the engine interface it is resolved by
        // (e.g. ITemplateConstraintFactory). The component manager caches by
        // that exact interface, so it must be probed with the registered type,
        // not the IIdentifiedComponent base (which is never a cache key).
        MethodInfo tryGetComponent = typeof(IComponentManager)
            .GetMethod(nameof(IComponentManager.TryGetComponent))!;

        foreach ((Type Type, IIdentifiedComponent Instance) in FuncTemplateComponents.Create(new([])))
        {
            object?[] args = [Instance.Id, null];
            bool resolved = (bool)tryGetComponent
                .MakeGenericMethod(Type)
                .Invoke(session.Settings.Components, args)!;

            resolved.Should().BeTrue($"{Instance.GetType().Name} should be resolvable as {Type.Name}");
        }
    }

    [Fact]
    public async Task Constraints_SeparateSessionSnapshots_DoNotShareEligibility()
    {
        var usable = new TemplateConstraintEvaluationSnapshot([new("node-package", NuGetVersion.Parse("1.0"), ["node"], true)]);
        var installedOnly = new TemplateConstraintEvaluationSnapshot([new("node-package", NuGetVersion.Parse("1.0"), ["node"], false)]);
        using TemplateEngineSession node = new(CommandContext("node"), Hive, usable);
        using TemplateEngineSession diagnostic = new(CommandContext("diagnostic"), Hive, installedOnly);
        var nodeFactory = node.Settings.Components.OfType<ITemplateConstraintFactory>().Single(factory => factory.Type == "func-workload");
        var diagnosticFactory = diagnostic.Settings.Components.OfType<ITemplateConstraintFactory>().Single(factory => factory.Type == "func-workload");
        var nodeConstraint = await nodeFactory.CreateTemplateConstraintAsync(node.Settings, CancellationToken.None);
        var diagnosticConstraint = await diagnosticFactory.CreateTemplateConstraintAsync(diagnostic.Settings, CancellationToken.None);

        var nodeResult = nodeConstraint.Evaluate("\"node\"");
        var diagnosticResult = diagnosticConstraint.Evaluate("\"node\"");

        nodeResult.EvaluationStatus.Should().Be(TemplateConstraintResult.CreateAllowed(nodeConstraint).EvaluationStatus);
        diagnosticResult.EvaluationStatus.Should().Be(TemplateConstraintResult.CreateRestricted(diagnosticConstraint, "unavailable").EvaluationStatus);
        nodeFactory.Should().NotBeSameAs(diagnosticFactory);
    }

    [Theory]
    [InlineData("4.20", true)]
    [InlineData("4.10", false)]
    [InlineData(null, false)]
    public async Task Constraints_Bundle_UsesResolvedCommandContext(string? version, bool allowed)
    {
        var context = new TemplateEngineContext(DirectoryUnderRoot("bundle"), bundle: version is null ? null : new("bundle", version));
        using TemplateEngineSession session = new(context, Hive);
        var factory = session.Settings.Components.OfType<ITemplateConstraintFactory>().Single(factory => factory.Type == "func-bundle");
        var constraint = await factory.CreateTemplateConstraintAsync(session.Settings, CancellationToken.None);

        var result = constraint.Evaluate("\"[4.20,5.0)\"");

        result.EvaluationStatus.Should().Be(allowed
            ? TemplateConstraintResult.CreateAllowed(constraint).EvaluationStatus
            : TemplateConstraintResult.CreateRestricted(constraint, "unavailable").EvaluationStatus);
    }

    [Fact]
    public void Constructor_ConflictingBundleSnapshot_Throws()
    {
        var snapshot = new TemplateConstraintEvaluationSnapshot([], "bundle", NuGetVersion.Parse("4.20"));
        Action action = () => new TemplateEngineSession(CommandContext("no-bundle"), Hive, snapshot);

        action.Should().Throw<ArgumentException>().WithParameterName("snapshot");
    }

    [Fact]
    public async Task Constraints_MalformedOrMissingArguments_FailClosed()
    {
        using TemplateEngineSession session = new(CommandContext("invalid"), Hive);
        var factory = session.Settings.Components.OfType<ITemplateConstraintFactory>().Single(factory => factory.Type == "func-workload");
        var constraint = await factory.CreateTemplateConstraintAsync(session.Settings, CancellationToken.None);

        var missing = constraint.Evaluate(null);
        var malformed = constraint.Evaluate("{not json");

        missing.EvaluationStatus.Should().Be(TemplateConstraintResult.Status.NotEvaluated);
        malformed.EvaluationStatus.Should().Be(TemplateConstraintResult.Status.NotEvaluated);
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("{\"constraints\":{\"stack\":{\"type\":\"func-workload\",\"args\":\"node\"}}}")]
    public async Task EvaluateConstraintsAsync_PreCanceledToken_ThrowsForEveryConfiguration(string configuration)
    {
        using TemplateEngineSession session = new(CommandContext("canceled-evaluation"), Hive);
        using CancellationTokenSource cancellation = new();
        await cancellation.CancelAsync();

        Func<Task> action = () => session.EvaluateConstraintsAsync(configuration, cancellation.Token);

        await action.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public void Settings_UseTheSuppliedSettingsLocation()
    {
        using TemplateEngineSession session = new(CommandContext("command"), Hive);

        session.Settings.Paths.GlobalSettingsDir.Should().Be(Hive);
    }

    [Fact]
    public void Settings_SeparateSessionsOnSharedHive_KeepTheirOwnContext()
    {
        using TemplateEngineSession node = new(ProjectContext("node-app", "node"), Hive);
        using TemplateEngineSession python = new(ProjectContext("python-app", "python"), Hive);

        HostParameter(node, FuncTemplateEngineHostParameters.Stack).Should().Be("node");
        HostParameter(python, FuncTemplateEngineHostParameters.Stack).Should().Be("python");
        HostParameter(node, "WorkingDirectory").Should().Be(DirectoryUnderRoot("node-app").Info.FullName);
        HostParameter(python, "WorkingDirectory").Should().Be(DirectoryUnderRoot("python-app").Info.FullName);
        node.Settings.Should().NotBeSameAs(python.Settings);
    }

    [Fact]
    public void Dispose_ThenUse_ThrowsObjectDisposedException()
    {
        TemplateEngineSession session = new(CommandContext("command"), Hive);

        session.Dispose();

        Action readSettings = () => _ = session.Settings;
        Action readPackageManager = () => _ = session.PackageManager;
        readSettings.Should().Throw<ObjectDisposedException>();
        readPackageManager.Should().Throw<ObjectDisposedException>();
    }

    [Fact]
    public void Dispose_CalledTwice_DoesNotThrow()
    {
        TemplateEngineSession session = new(CommandContext("command"), Hive);
        session.Dispose();

        Action act = session.Dispose;

        act.Should().NotThrow();
    }

    [Fact]
    public void Constructor_NullContext_Throws()
    {
        Action act = () => _ = new TemplateEngineSession(null!, Hive);

        act.Should().Throw<ArgumentNullException>().WithParameterName("context");
    }

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    public void Constructor_BlankSettingsLocation_Throws(string settingsLocation)
    {
        Action act = () => _ = new TemplateEngineSession(CommandContext("command"), settingsLocation);

        act.Should().Throw<ArgumentException>().WithParameterName("settingsLocation");
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
