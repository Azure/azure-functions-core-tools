// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Templates.Engine;
using NuGet.Versioning;

namespace Azure.Functions.Cli.Tests.Templates.Engine;

public sealed class FuncTemplateConstraintEvaluatorTests
{
    private readonly FuncTemplateConstraintEvaluator _evaluator = new(new());

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Evaluate_WorkloadUsability_DeterminesEligibility(bool usable)
    {
        var declaration = TemplateConstraintDeclarationReader.Read("""{"constraints":{"stack":{"type":"func-workload","args":{"id":"node","version":"[1.0,2.0)"}}}}""")[0];
        TemplateConstraintWorkload[] workloads = [new("node-package", NuGetVersion.Parse("1.5.0"), ["node"], usable)];

        var result = _evaluator.Evaluate(declaration, workloads);

        result.Should().Be(usable ? FuncTemplateConstraintOutcome.Eligible : FuncTemplateConstraintOutcome.Restricted);
    }

    [Fact]
    public void Evaluate_LaterUsableAlternative_ReturnsEligible()
    {
        var declaration = TemplateConstraintDeclarationReader.Read("""{"constraints":{"stack":{"type":"func-workload","args":["node","python"]}}}""")[0];
        TemplateConstraintWorkload[] workloads = [new("node-package", NuGetVersion.Parse("1.0"), ["node"], false), new("python-package", NuGetVersion.Parse("1.0"), ["python"], true)];

        var result = _evaluator.Evaluate(declaration, workloads);

        result.Should().Be(FuncTemplateConstraintOutcome.Eligible);
    }

    [Fact]
    public void Evaluate_InstalledOnlyInRangeEntry_DoesNotBecomeFallback()
    {
        var declaration = TemplateConstraintDeclarationReader.Read("""{"constraints":{"stack":{"type":"func-workload","args":{"id":"node","version":"[1.0,2.0)"}}}}""")[0];
        TemplateConstraintWorkload[] workloads = [new("node-package", NuGetVersion.Parse("2.0"), ["node"], true), new("node-package", NuGetVersion.Parse("1.5"), ["node"], false)];

        var result = _evaluator.Evaluate(declaration, workloads);

        result.Should().Be(FuncTemplateConstraintOutcome.Restricted);
    }

    [Theory]
    [InlineData("4.20.0-preview.2", true)]
    [InlineData("4.10.0", false)]
    [InlineData(null, false)]
    public void Evaluate_BundleContext_ReturnsExpected(string? version, bool eligible)
    {
        var declaration = TemplateConstraintDeclarationReader.Read("""{"constraints":{"bundle":{"type":"func-bundle","args":"[4.20,5.0)"}}}""")[0];

        var result = _evaluator.Evaluate(declaration, [], "bundle", version is null ? null : NuGetVersion.Parse(version));

        result.Should().Be(eligible ? FuncTemplateConstraintOutcome.Eligible : FuncTemplateConstraintOutcome.Restricted);
    }

    [Fact]
    public void Evaluate_UnknownType_ReturnsNotEvaluated()
    {
        var declaration = TemplateConstraintDeclarationReader.Read("""{"constraints":{"future":{"type":"future-type","args":{}}}}""")[0];

        var result = _evaluator.Evaluate(declaration, []);

        result.Should().Be(FuncTemplateConstraintOutcome.NotEvaluated);
    }

    [Fact]
    public void Constructor_NullMatcher_Throws()
    {
        Action action = () => new FuncTemplateConstraintEvaluator(null!);

        action.Should().Throw<ArgumentNullException>().WithParameterName("matcher");
    }

    [Fact]
    public void Evaluate_NullDeclaration_Throws()
    {
        Action action = () => _evaluator.Evaluate(null!, []);

        action.Should().Throw<ArgumentNullException>().WithParameterName("declaration");
    }

    [Fact]
    public void Evaluate_NullWorkloads_Throws()
    {
        var declaration = TemplateConstraintDeclarationReader.Read("""{"constraints":{"node":{"type":"func-workload","args":"node"}}}""")[0];
        Action action = () => _evaluator.Evaluate(declaration, (IReadOnlyCollection<TemplateConstraintWorkload>)null!);

        action.Should().Throw<ArgumentNullException>().WithParameterName("workloads");
    }

    [Fact]
    public void Evaluate_FuncDeclarationWithoutAlternatives_Throws()
    {
        var declaration = new TemplateConstraintDeclaration("node", "func-workload", null, []);
        Action action = () => _evaluator.Evaluate(declaration, []);

        action.Should().Throw<ArgumentException>().WithParameterName("declaration");
    }

    [Theory]
    [InlineData("4.20", true)]
    [InlineData(null, false)]
    public void EvaluateAll_SeveralDeclarations_RequiresEveryDeclaration(string? bundleVersion, bool eligible)
    {
        var declarations = TemplateConstraintDeclarationReader.Read("""{"constraints":{"node":{"type":"func-workload","args":"node"},"bundle":{"type":"func-bundle","args":"[4.20,5.0)"}}}""");
        TemplateConstraintWorkload[] workloads = [new("node", NuGetVersion.Parse("1.0"), [], true)];

        var result = _evaluator.EvaluateAll(declarations, workloads, "bundle", bundleVersion is null ? null : NuGetVersion.Parse(bundleVersion));

        result.Should().Be(eligible ? FuncTemplateConstraintOutcome.Eligible : FuncTemplateConstraintOutcome.Restricted);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void EvaluateAll_UnknownAndRestricted_ReturnsNotEvaluatedRegardlessOfOrder(bool reverse)
    {
        var declarations = TemplateConstraintDeclarationReader.Read("""{"constraints":{"node":{"type":"func-workload","args":"node"},"future":{"type":"future-type"}}}""");

        var result = _evaluator.EvaluateAll(reverse ? declarations.Reverse().ToArray() : declarations, []);

        result.Should().Be(FuncTemplateConstraintOutcome.NotEvaluated);
    }

    [Fact]
    public void EvaluateAll_EmptyDeclarations_ReturnsEligible()
    {
        _evaluator.EvaluateAll([], []).Should().Be(FuncTemplateConstraintOutcome.Eligible);
    }

    [Fact]
    public void EvaluateAll_NullDeclarations_Throws()
    {
        Action action = () => _evaluator.EvaluateAll(null!, []);

        action.Should().Throw<ArgumentNullException>().WithParameterName("declarations");
    }
}