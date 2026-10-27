// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Templates.Engine;
using NuGet.Versioning;

namespace Azure.Functions.Cli.Tests.Templates.Engine;

public sealed class TemplateConstraintEvaluationSnapshotTests
{
    private readonly FuncTemplateConstraintEvaluator _evaluator = new(new());

    [Fact]
    public void Constructor_CallerChangesWorkloads_RetainsCapturedEligibility()
    {
        var declaration = TemplateConstraintDeclarationReader.Read("""{"constraints":{"stack":{"type":"func-workload","args":"node"}}}""")[0];
        List<TemplateConstraintWorkload> workloads = [new("node-package", NuGetVersion.Parse("1.0"), ["node"], true)];
        var snapshot = new TemplateConstraintEvaluationSnapshot(workloads);

        workloads.Clear();
        var result = _evaluator.Evaluate(declaration, snapshot);

        result.Should().Be(FuncTemplateConstraintOutcome.Eligible);
        snapshot.Workloads.Should().ContainSingle();
    }

    [Fact]
    public void Constructor_CallerChangesAliases_RetainsCapturedIdentities()
    {
        var declaration = TemplateConstraintDeclarationReader.Read("""{"constraints":{"stack":{"type":"func-workload","args":"node"}}}""")[0];
        List<string> aliases = ["node"];
        TemplateConstraintWorkload[] workloads = [new("node-package", NuGetVersion.Parse("1.0"), aliases, true)];
        var snapshot = new TemplateConstraintEvaluationSnapshot(workloads);

        aliases.Clear();
        aliases.Add("python");
        var result = _evaluator.Evaluate(declaration, snapshot);

        result.Should().Be(FuncTemplateConstraintOutcome.Eligible);
        snapshot.Workloads[0].Aliases.Should().Equal("node");
        snapshot.Workloads[0].Should().NotBeSameAs(workloads[0]);
    }

    [Fact]
    public void Constructor_CallerReplacesRecord_DoesNotPromoteInstalledOnlyCandidate()
    {
        var declaration = TemplateConstraintDeclarationReader.Read("""{"constraints":{"stack":{"type":"func-workload","args":"node"}}}""")[0];
        TemplateConstraintWorkload[] workloads = [new("node-package", NuGetVersion.Parse("1.0"), ["node"], false)];
        var snapshot = new TemplateConstraintEvaluationSnapshot(workloads);

        workloads[0] = workloads[0] with { IsUsable = true };
        var result = _evaluator.Evaluate(declaration, snapshot);

        result.Should().Be(FuncTemplateConstraintOutcome.Restricted);
        snapshot.Workloads[0].IsUsable.Should().BeFalse();
    }

    [Fact]
    public void Constructor_CapturedCollections_RejectMutation()
    {
        var snapshot = new TemplateConstraintEvaluationSnapshot([new("node", NuGetVersion.Parse("1.0"), ["node"], true)]);

        Action changeWorkloads = () => ((IList<TemplateConstraintWorkload>)snapshot.Workloads).Clear();
        Action changeAliases = () => ((IList<string>)snapshot.Workloads[0].Aliases).Clear();

        changeWorkloads.Should().Throw<NotSupportedException>();
        changeAliases.Should().Throw<NotSupportedException>();
    }

    [Fact]
    public void Constructor_BundleContext_ProvidesCapturedEvaluationInput()
    {
        var declarations = TemplateConstraintDeclarationReader.Read("""{"constraints":{"bundle":{"type":"func-bundle","args":{"id":"bundle","version":"[4.20,5.0)"}}}}""");
        var version = NuGetVersion.Parse("4.20.0");
        var snapshot = new TemplateConstraintEvaluationSnapshot([], "bundle", version);

        var result = _evaluator.EvaluateAll(declarations, snapshot);

        result.Should().Be(FuncTemplateConstraintOutcome.Eligible);
        snapshot.BundleId.Should().Be("bundle");
        snapshot.BundleVersion.Should().Be(version);
    }

    [Fact]
    public void Constructor_EmptyInput_DoesNotInventWorkloadsOrBundle()
    {
        var snapshot = new TemplateConstraintEvaluationSnapshot([]);

        snapshot.Workloads.Should().BeEmpty();
        snapshot.BundleId.Should().BeNull();
        snapshot.BundleVersion.Should().BeNull();
    }

    [Fact]
    public void Constructor_NullWorkloads_Throws()
    {
        Action action = () => new TemplateConstraintEvaluationSnapshot(null!);

        action.Should().Throw<ArgumentNullException>().WithParameterName("workloads");
    }

    [Fact]
    public void Constructor_NullWorkloadEntry_Throws()
    {
        Action action = () => new TemplateConstraintEvaluationSnapshot([null!]);

        action.Should().Throw<ArgumentException>().WithParameterName("workloads");
    }

    [Fact]
    public void Constructor_NullAliases_Throws()
    {
        Action action = () => new TemplateConstraintEvaluationSnapshot([new("node", NuGetVersion.Parse("1.0"), null!, true)]);

        action.Should().Throw<ArgumentException>().WithParameterName("workloads");
    }

    [Fact]
    public void Evaluate_NullSnapshot_Throws()
    {
        var declaration = TemplateConstraintDeclarationReader.Read("""{"constraints":{"stack":{"type":"func-workload","args":"node"}}}""")[0];
        Action action = () => _evaluator.Evaluate(declaration, (TemplateConstraintEvaluationSnapshot)null!);

        action.Should().Throw<ArgumentNullException>().WithParameterName("snapshot");
    }

    [Fact]
    public void EvaluateAll_NullSnapshot_Throws()
    {
        Action action = () => _evaluator.EvaluateAll([], (TemplateConstraintEvaluationSnapshot)null!);

        action.Should().Throw<ArgumentNullException>().WithParameterName("snapshot");
    }
}