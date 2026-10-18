// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Text.Json;
using Azure.Functions.Cli.Bundles;
using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Templates.Engine;
using Microsoft.TemplateEngine.Abstractions.Constraints;
using NSubstitute;
using NuGet.Versioning;

namespace Azure.Functions.Cli.Tests.Templates.Engine;

public sealed class BundleBindingConstraintTests
{
    private readonly FuncTemplateConstraintEvaluator _enabled = new(new(), enableBundleBindingConstraints: true);
    private readonly TemplateConstraintEvaluationSnapshot _workloads = new([]);
    private readonly string _path = Path.GetFullPath("bundle-bindings");

    [Fact]
    public void Read_BindingRequirements_PreservesAllExactNamesAndExplicitEmpty()
    {
        var required = Declaration("""["blobTrigger","eventGridTrigger","BLOBTRIGGER"]""");
        var empty = Declaration("[]");

        required.RequiredBindings.Should().Equal("blobTrigger", "eventGridTrigger", "BLOBTRIGGER");
        required.Alternatives.Should().BeEmpty();
        empty.RequiredBindings.Should().BeEmpty();
        empty.Arguments!.Value.GetArrayLength().Should().Be(0);
    }

    [Theory]
    [InlineData("null")]
    [InlineData("{}")]
    [InlineData("\"blob\"")]
    [InlineData("[null]")]
    [InlineData("[3]")]
    [InlineData("[\"\"]")]
    [InlineData("[\" \"]")]
    [InlineData("[[\"blob\"]]")]
    [InlineData("[{\"name\":\"blob\"}]")]
    public void Read_MalformedBindingRequirements_Throws(string args)
    {
        Action read = () => Declaration(args);

        read.Should().Throw<InvalidTemplateMetadataException>();
    }

    [Fact]
    public void Read_MissingBindingArguments_Throws()
    {
        Action read = () => TemplateConstraintDeclarationReader.Read("""{"constraints":{"bindings":{"type":"func-bundle-bindings"}}}""");

        read.Should().Throw<InvalidTemplateMetadataException>();
    }

    [Theory]
    [InlineData("project")]
    [InlineData("PROJECT")]
    public void Read_ProjectBindingConstraint_RejectsEvenEmptyRequirements(string type)
    {
        Action read = () => TemplateConstraintDeclarationReader.Read(
            $$$$"""{"tags":{"type":"{{{{type}}}}"},"constraints":{"bindings":{"type":"func-bundle-bindings","args":[]}}}""");

        read.Should().Throw<InvalidTemplateMetadataException>().WithMessage("*project template*");
    }

    [Fact]
    public void Read_SharedHostPackage_RejectsBindingConstraint()
    {
        Action read = () => TemplateConstraintDeclarationReader.Read(Configuration("[]"), ["FuncTemplate", "Template"]);

        read.Should().Throw<InvalidTemplateMetadataException>();
    }

    [Theory]
    [InlineData("[\"blobTrigger\",\"eventGridTrigger\"]", "Eligible")]
    [InlineData("[\"BLOBTRIGGER\",\"EVENTGRIDTRIGGER\"]", "Eligible")]
    [InlineData("[\"blobTrigger\",\"queueTrigger\"]", "Restricted")]
    [InlineData("[\"unknownFutureBinding\"]", "Restricted")]
    [InlineData("[]", "Eligible")]
    public void Evaluate_AllRequirements_UsesActualSourceSetCaseInsensitively(string args, string expected)
    {
        var bundle = Bundle(["blobTrigger", "eventGridTrigger"]);

        var result = _enabled.Evaluate(Declaration(args), _workloads, bundle);

        result.ToString().Should().Be(expected);
    }

    [Theory]
    [InlineData("[]", "Eligible")]
    [InlineData("[\"httpTrigger\"]", "Restricted")]
    [InlineData("[\"blob\"]", "Restricted")]
    public void Evaluate_NoBundle_OnlyExplicitlyDependencyFreeRequirementsPass(string args, string expected)
    {
        var result = _enabled.Evaluate(Declaration(args), _workloads);

        result.ToString().Should().Be(expected);
    }

    [Fact]
    public void Evaluate_NotComposed_RemainsNotEvaluatedEvenWithMatchingSource()
    {
        FuncTemplateConstraintEvaluator disabled = new(new());

        var result = disabled.Evaluate(Declaration("[\"blob\"]"), _workloads, Bundle(["blob"]));

        result.Should().Be(FuncTemplateConstraintOutcome.NotEvaluated);
    }

    [Theory]
    [InlineData("func-bundle-binding")]
    [InlineData("Func-bundle-bindings")]
    [InlineData("func-future")]
    public void Evaluate_UnknownConstraintType_RemainsNotEvaluated(string type)
    {
        var declaration = TemplateConstraintDeclarationReader.Read(
            $$$$"""{"constraints":{"unknown":{"type":"{{{{type}}}}","args":["blob"]}}}""")[0];

        var result = _enabled.Evaluate(declaration, _workloads, Bundle(["blob"]));

        result.Should().Be(FuncTemplateConstraintOutcome.NotEvaluated);
    }

    [Theory]
    [InlineData("another.bundle", "4.20.0", "bundle-bindings")]
    [InlineData("selected.bundle", "4.21.0", "bundle-bindings")]
    [InlineData("selected.bundle", "4.20.0", "another-bundle")]
    public void Evaluate_ChangedSelectedIdentityVersionOrPath_RejectsStaleCapabilities(string id, string version, string path)
    {
        var original = Bundle(["blob"]);
        var changed = new TemplateEngineBundleContext(id, version, Path.GetFullPath(path), original.Bindings);

        var result = _enabled.Evaluate(Declaration("[\"blob\"]"), _workloads, changed);

        result.Should().Be(FuncTemplateConstraintOutcome.Failed);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Evaluate_NoCapturedMetadata_FailsEvenWhenWorkloadRowsSuggestBundleAvailability(bool hasPath)
    {
        var bundle = new TemplateEngineBundleContext("selected.bundle", "4.20.0", hasPath ? _path : null);
        var availability = new TemplateConstraintEvaluationSnapshot(
            [new("selected.bundle", NuGetVersion.Parse("4.20.0"), ["blob"], true)]);

        var result = _enabled.Evaluate(Declaration("[\"blob\"]"), availability, bundle);

        result.Should().Be(FuncTemplateConstraintOutcome.Failed);
    }

    [Theory]
    [InlineData("selected.bundle", "4.20.0", "Eligible")]
    [InlineData("another.bundle", "4.20.0", "Restricted")]
    [InlineData("selected.bundle", "4.10.0", "Restricted")]
    public void EvaluateAll_BindingsDoNotWeakenSeparateIdentityAndVersionRequirements(
        string id, string version, string expected)
    {
        var source = new TemplateEngineBundleContext(id, version, _path);
        var bundle = new TemplateEngineBundleContext(id, version, _path, new(source, ["blob"]));
        var declarations = TemplateConstraintDeclarationReader.Read("""
            {"constraints":{
              "bindings":{"type":"func-bundle-bindings","args":["blob"]},
              "bundle":{"type":"func-bundle","args":{"id":"selected.bundle","version":"[4.20,5.0)"}}
            }}
            """);

        var result = _enabled.EvaluateAll(declarations, _workloads, bundle);

        result.ToString().Should().Be(expected);
    }

    [Fact]
    public void Capabilities_MutableInput_IsCopied()
    {
        List<string> names = ["blob"];
        var bundle = Bundle(names);

        names.Clear();
        names.Add("queue");

        bundle.Bindings!.Contains("blob").Should().BeTrue();
        bundle.Bindings.Contains("queue").Should().BeFalse();
    }

    [Fact]
    public void BundleContext_SerializedCommandOutput_DoesNotExposeInternalPathOrCapabilities()
    {
        var bundle = Bundle(["blob"]);

        using var json = JsonDocument.Parse(JsonSerializer.Serialize(bundle));

        json.RootElement.EnumerateObject().Select(property => property.Name).Should().Equal("Id", "Version", "ResolvedVersion");
    }

    [Fact]
    public void Evaluate_UnvalidatedBindingDeclaration_ThrowsRatherThanAllowing()
    {
        var declaration = new TemplateConstraintDeclaration("bindings", "func-bundle-bindings", null, []);
        Action evaluate = () => _enabled.Evaluate(declaration, _workloads, Bundle(["blob"]));

        evaluate.Should().Throw<ArgumentException>().WithParameterName("declaration");
    }

    [Fact]
    public void Evaluate_EmptyRequirements_DoNotDependOnUnreadableMetadataButKeepOtherConstraints()
    {
        var source = new TemplateEngineBundleContext("selected.bundle", "4.20.0", _path);
        var bundle = new TemplateEngineBundleContext(source.Id, source.Version, source.Path, new(source, [], "unreadable"));
        var declarations = TemplateConstraintDeclarationReader.Read("""
            {"constraints":{
              "bindings":{"type":"func-bundle-bindings","args":[]},
              "bundle":{"type":"func-bundle","args":{"id":"another.bundle"}}
            }}
            """);

        _enabled.Evaluate(declarations[0], _workloads, bundle).Should().Be(FuncTemplateConstraintOutcome.Eligible);
        _enabled.EvaluateAll(declarations, _workloads, bundle).Should().Be(FuncTemplateConstraintOutcome.Restricted);
    }

    [Fact]
    public void EvaluateAll_FailedMetadataAndUnknownConstraint_NeverBecomesEligible()
    {
        var bundle = new TemplateEngineBundleContext("selected.bundle", "4.20.0", _path);
        var declarations = TemplateConstraintDeclarationReader.Read("""
            {"constraints":{
              "bindings":{"type":"func-bundle-bindings","args":["blob"]},
              "future":{"type":"func-future","args":[]}
            }}
            """);

        _enabled.EvaluateAll(declarations, _workloads, bundle).Should().Be(FuncTemplateConstraintOutcome.Failed);
        _enabled.EvaluateAll(declarations.Reverse().ToArray(), _workloads, bundle).Should().Be(FuncTemplateConstraintOutcome.Failed);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task EvaluateConstraintsAsync_SessionComposition_IsExplicit(bool enabled)
    {
        var context = new TemplateEngineContext(new(new DirectoryInfo(_path), WasExplicit: true), bundle: Bundle(["blob"]),
            enableBundleBindingConstraints: enabled);
        using TemplateEngineSession session = new(context, Path.Combine(_path, "settings"), _workloads);

        var result = await session.EvaluateConstraintsAsync(Configuration("[\"blob\"]"), CancellationToken.None);

        result.Status.Should().Be(enabled ? TemplateConstraintEvaluationStatus.Eligible : TemplateConstraintEvaluationStatus.NotEvaluated);
        session.Settings.Components.OfType<ITemplateConstraintFactory>().Count(factory => factory.Type == "func-bundle-bindings")
            .Should().Be(enabled ? 1 : 0);
    }

    [Theory]
    [InlineData(null, "Failed")]
    [InlineData("{", "Failed")]
    [InlineData("{\"extensions\":[]}", "Restricted")]
    [InlineData("{\"extensions\":[{\"bindings\":[\"blob\"]}]}", "Eligible")]
    public async Task EvaluateConstraintsAsync_LocalReaderThroughNativeFactory_PreservesFailureCategory(
        string? metadata, string expected)
    {
        var files = Substitute.For<IFileSystem>();
        files.ReadAllTextIfExistsAsync(Path.Combine(_path, "4.20.0", "bin", "extensions.json"), Arg.Any<CancellationToken>()).Returns(metadata);
        TemplateEngineBundleContextReader reader = new(files);
        var bundle = await reader.ReadAsync(new ExtensionBundleResolution.Resolved("selected.bundle", "4.20.0", _path, null), CancellationToken.None);
        var context = new TemplateEngineContext(new(new DirectoryInfo(_path), WasExplicit: true), bundle: bundle, enableBundleBindingConstraints: true);
        using TemplateEngineSession session = new(context, Path.Combine(_path, "settings"), _workloads);

        var result = await session.EvaluateConstraintsAsync(Configuration("[\"blob\"]"), CancellationToken.None);

        result.Status.ToString().Should().Be(expected);
        if (expected != "Eligible")
        {
            result.Diagnostics.Should().ContainSingle().Which.Message.Should().NotBeNullOrWhiteSpace();
        }
    }

    [Fact]
    public async Task EvaluateConstraintsAsync_MissingBindings_ReportsExactMissingRequirement()
    {
        var context = new TemplateEngineContext(new(new DirectoryInfo(_path), WasExplicit: true), bundle: Bundle(["blob"]), enableBundleBindingConstraints: true);
        using TemplateEngineSession session = new(context, Path.Combine(_path, "settings"), _workloads);

        var result = await session.EvaluateConstraintsAsync(Configuration("[\"blob\",\"queue\"]"), CancellationToken.None);

        result.Status.Should().Be(TemplateConstraintEvaluationStatus.Restricted);
        result.Diagnostics.Should().ContainSingle().Which.Message.Should().Contain("queue");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("null")]
    [InlineData("[3]")]
    public async Task NativeFactory_InvalidArguments_FailsExplicitly(string? args)
    {
        using TemplateEngineSession session = new(new(new(new DirectoryInfo(_path), WasExplicit: true), enableBundleBindingConstraints: true),
            Path.Combine(_path, "settings"), _workloads);
        var factory = session.Settings.Components.OfType<ITemplateConstraintFactory>().Single(item => item.Type == "func-bundle-bindings");
        var constraint = await factory.CreateTemplateConstraintAsync(session.Settings, CancellationToken.None);

        var result = constraint.Evaluate(args);

        result.EvaluationStatus.Should().Be(TemplateConstraintResult.Status.NotEvaluated);
        result.LocalizedErrorMessage.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public void Capabilities_InvalidContracts_Throw()
    {
        var source = new TemplateEngineBundleContext("bundle", "4.0", _path);
        Action nullSource = () => new TemplateBundleBindingCapabilities(null!, []);
        Action nullNames = () => new TemplateBundleBindingCapabilities(source, null!);
        Action noPath = () => new TemplateBundleBindingCapabilities(new("bundle", "4.0"), []);
        Action invalidName = () => new TemplateBundleBindingCapabilities(source, [""]);
        Action failedWithNames = () => new TemplateBundleBindingCapabilities(source, ["blob"], "unreadable");

        nullSource.Should().Throw<ArgumentNullException>();
        nullNames.Should().Throw<ArgumentNullException>();
        noPath.Should().Throw<ArgumentException>();
        invalidName.Should().Throw<ArgumentException>();
        failedWithNames.Should().Throw<ArgumentException>();
    }

    private TemplateEngineBundleContext Bundle(IReadOnlyCollection<string> bindings)
    {
        var source = new TemplateEngineBundleContext("selected.bundle", "4.20.0", _path);
        return new(source.Id, source.Version, source.Path, new(source, bindings));
    }

    private static TemplateConstraintDeclaration Declaration(string args) => TemplateConstraintDeclarationReader.Read(Configuration(args))[0];

    private static string Configuration(string args) => JsonSerializer.Serialize(new
    {
        constraints = new { bindings = new { type = "func-bundle-bindings", args = JsonSerializer.Deserialize<JsonElement>(args) } },
    });
}
