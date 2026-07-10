// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Bundles;
using Azure.Functions.Cli.Templates.Engine;
using Microsoft.TemplateEngine.Abstractions.Constraints;
using Microsoft.TemplateEngine.Edge;
using Xunit;

namespace Azure.Functions.Cli.Tests.Templates.Engine;

public class FuncExtensionBundleConstraintTests : IDisposable
{
    private const string BundleId = BundleHelpers.StableBundleId;
    private const string PreviewBundleId = BundleHelpers.PreviewBundleId;

    private readonly string _root;

    public FuncExtensionBundleConstraintTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "func-bundle-constraint-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch
        {
            // best-effort cleanup of the temp settings location
        }

        GC.SuppressFinalize(this);
    }

    [Fact]
    public void FactoryType_IsFuncExtensionBundle()
    {
        ITemplateConstraintFactory factory = new FuncExtensionBundleConstraintFactory();

        Assert.Equal("func-extension-bundle", factory.Type);
        Assert.Equal(FuncExtensionBundleConstraintFactory.ConstraintType, factory.Type);
    }

    [Theory]
    [InlineData("4.2.0")]
    [InlineData("4.0.0")]
    public async Task Evaluate_VersionInRange_ReturnsAllowed(string resolvedVersion)
    {
        ITemplateConstraint constraint = await CreateConstraintAsync(
            (FuncTemplateEngineHostParameters.BundleChannel, BundleId),
            (FuncTemplateEngineHostParameters.Bundle, resolvedVersion));

        TemplateConstraintResult result = constraint.Evaluate(Args(BundleId, "[4.0.0, 5.0.0)"));

        Assert.Equal(TemplateConstraintResult.Status.Allowed, result.EvaluationStatus);
    }

    [Theory]
    [InlineData("3.9.0")]
    [InlineData("5.0.0")]
    [InlineData("5.1.0")]
    public async Task Evaluate_VersionOutOfRange_ReturnsRestricted(string resolvedVersion)
    {
        ITemplateConstraint constraint = await CreateConstraintAsync(
            (FuncTemplateEngineHostParameters.BundleChannel, BundleId),
            (FuncTemplateEngineHostParameters.Bundle, resolvedVersion));

        TemplateConstraintResult result = constraint.Evaluate(Args(BundleId, "[4.0.0, 5.0.0)"));

        Assert.Equal(TemplateConstraintResult.Status.Restricted, result.EvaluationStatus);
        Assert.False(string.IsNullOrWhiteSpace(result.LocalizedErrorMessage));
    }

    [Fact]
    public async Task Evaluate_MismatchedBundleId_ReturnsRestricted()
    {
        ITemplateConstraint constraint = await CreateConstraintAsync(
            (FuncTemplateEngineHostParameters.BundleChannel, PreviewBundleId),
            (FuncTemplateEngineHostParameters.Bundle, "4.2.0"));

        TemplateConstraintResult result = constraint.Evaluate(Args(BundleId, "[4.0.0, 5.0.0)"));

        Assert.Equal(TemplateConstraintResult.Status.Restricted, result.EvaluationStatus);
    }

    [Fact]
    public async Task Evaluate_MatchedBundleId_IsCaseInsensitive()
    {
        ITemplateConstraint constraint = await CreateConstraintAsync(
            (FuncTemplateEngineHostParameters.BundleChannel, BundleId.ToUpperInvariant()),
            (FuncTemplateEngineHostParameters.Bundle, "4.2.0"));

        TemplateConstraintResult result = constraint.Evaluate(Args(BundleId, "[4.0.0, 5.0.0)"));

        Assert.Equal(TemplateConstraintResult.Status.Allowed, result.EvaluationStatus);
    }

    [Fact]
    public async Task Evaluate_MissingBundleContext_ReturnsRestricted()
    {
        ITemplateConstraint constraint = await CreateConstraintAsync();

        TemplateConstraintResult result = constraint.Evaluate(Args(BundleId, "[4.0.0, 5.0.0)"));

        Assert.Equal(TemplateConstraintResult.Status.Restricted, result.EvaluationStatus);
        Assert.False(string.IsNullOrWhiteSpace(result.LocalizedErrorMessage));
    }

    [Fact]
    public async Task Evaluate_MissingArgs_ReturnsEvaluationFailure()
    {
        ITemplateConstraint constraint = await CreateConstraintAsync(
            (FuncTemplateEngineHostParameters.BundleChannel, BundleId),
            (FuncTemplateEngineHostParameters.Bundle, "4.2.0"));

        TemplateConstraintResult result = constraint.Evaluate(null);

        Assert.Equal(TemplateConstraintResult.Status.NotEvaluated, result.EvaluationStatus);
    }

    [Theory]
    [InlineData("\"[4.0.0, 5.0.0)\"")]
    [InlineData("[4.0.0, 5.0.0)")]
    [InlineData("(,5.0.0)")]
    public async Task Evaluate_BareVersionRange_TargetsImplicitStableId(string args)
    {
        ITemplateConstraint constraint = await CreateConstraintAsync(
            (FuncTemplateEngineHostParameters.BundleChannel, BundleId),
            (FuncTemplateEngineHostParameters.Bundle, "4.2.0"));

        TemplateConstraintResult result = constraint.Evaluate(args);

        Assert.Equal(TemplateConstraintResult.Status.Allowed, result.EvaluationStatus);
    }

    [Fact]
    public async Task Evaluate_BareVersionRange_RestrictedWhenBundleIsPreview()
    {
        // A bare version range implies the stable bundle id, so a project on the
        // preview channel does not satisfy it.
        ITemplateConstraint constraint = await CreateConstraintAsync(
            (FuncTemplateEngineHostParameters.BundleChannel, PreviewBundleId),
            (FuncTemplateEngineHostParameters.Bundle, "4.2.0"));

        TemplateConstraintResult result = constraint.Evaluate("[4.0.0, 5.0.0)");

        Assert.Equal(TemplateConstraintResult.Status.Restricted, result.EvaluationStatus);
    }

    [Fact]
    public async Task Evaluate_VersionOnlyObject_TargetsImplicitStableId()
    {
        ITemplateConstraint constraint = await CreateConstraintAsync(
            (FuncTemplateEngineHostParameters.BundleChannel, BundleId),
            (FuncTemplateEngineHostParameters.Bundle, "4.2.0"));

        TemplateConstraintResult result = constraint.Evaluate("{ \"version\": \"[4.0.0, 5.0.0)\" }");

        Assert.Equal(TemplateConstraintResult.Status.Allowed, result.EvaluationStatus);
    }

    [Fact]
    public async Task Evaluate_ExplicitPreviewId_AllowedWhenBundleMatches()
    {
        ITemplateConstraint constraint = await CreateConstraintAsync(
            (FuncTemplateEngineHostParameters.BundleChannel, PreviewBundleId),
            (FuncTemplateEngineHostParameters.Bundle, "4.2.0"));

        TemplateConstraintResult result = constraint.Evaluate(Args(PreviewBundleId, "[4.0.0, 5.0.0)"));

        Assert.Equal(TemplateConstraintResult.Status.Allowed, result.EvaluationStatus);
    }

    [Theory]
    [InlineData("stable", BundleHelpers.StableBundleId)]
    [InlineData("preview", BundleHelpers.PreviewBundleId)]
    [InlineData("experimental", BundleHelpers.ExperimentalBundleId)]
    [InlineData("PREVIEW", BundleHelpers.PreviewBundleId)]
    public async Task Evaluate_ShortChannelLabelId_ResolvesToChannel(string label, string resolvedBundleId)
    {
        ITemplateConstraint constraint = await CreateConstraintAsync(
            (FuncTemplateEngineHostParameters.BundleChannel, resolvedBundleId),
            (FuncTemplateEngineHostParameters.Bundle, "4.2.0"));

        TemplateConstraintResult result = constraint.Evaluate(Args(label, "[4.0.0, 5.0.0)"));

        Assert.Equal(TemplateConstraintResult.Status.Allowed, result.EvaluationStatus);
    }

    [Fact]
    public async Task Evaluate_ShortLabelId_RestrictedWhenChannelDiffers()
    {
        // args target the preview channel by label; project is on stable.
        ITemplateConstraint constraint = await CreateConstraintAsync(
            (FuncTemplateEngineHostParameters.BundleChannel, BundleId),
            (FuncTemplateEngineHostParameters.Bundle, "4.2.0"));

        TemplateConstraintResult result = constraint.Evaluate(Args("preview", "[4.0.0, 5.0.0)"));

        Assert.Equal(TemplateConstraintResult.Status.Restricted, result.EvaluationStatus);
    }

    [Theory]
    [InlineData("{ \"id\": \"Microsoft.Azure.Functions.ExtensionBundle\" }")]
    [InlineData("{ \"id\": \"stable\", \"version\": \"not-a-range\" }")]
    [InlineData("{ \"id\": \"\", \"version\": \"[4.0.0, 5.0.0)\" }")]
    [InlineData("{ \"id\": \"bogus-channel\", \"version\": \"[4.0.0, 5.0.0)\" }")]
    [InlineData("{ }")]
    [InlineData("not-a-range")]
    [InlineData("[1,2,3]")]
    public async Task Evaluate_InvalidArgs_ReturnsEvaluationFailure(string args)
    {
        ITemplateConstraint constraint = await CreateConstraintAsync(
            (FuncTemplateEngineHostParameters.BundleChannel, BundleId),
            (FuncTemplateEngineHostParameters.Bundle, "4.2.0"));

        TemplateConstraintResult result = constraint.Evaluate(args);

        Assert.Equal(TemplateConstraintResult.Status.NotEvaluated, result.EvaluationStatus);
        Assert.False(string.IsNullOrWhiteSpace(result.LocalizedErrorMessage));
    }

    private static string Args(string id, string versionRange)
        => $"{{ \"id\": \"{id}\", \"version\": \"{versionRange}\" }}";

    private async Task<ITemplateConstraint> CreateConstraintAsync(params (string Key, string Value)[] hostParams)
    {
        Dictionary<string, string> defaults = hostParams.ToDictionary(p => p.Key, p => p.Value, StringComparer.Ordinal);
        DefaultTemplateEngineHost host = new("func", "1.0.0", defaults);
        EngineEnvironmentSettings settings = new(host, settingsLocation: _root);
        ITemplateConstraintFactory factory = new FuncExtensionBundleConstraintFactory();
        return await factory.CreateTemplateConstraintAsync(settings, CancellationToken.None);
    }
}
