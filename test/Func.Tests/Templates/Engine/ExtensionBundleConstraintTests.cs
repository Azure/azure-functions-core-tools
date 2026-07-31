// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Templates.Engine;
using Microsoft.TemplateEngine.Abstractions.Constraints;

namespace Azure.Functions.Cli.Tests.Templates.Engine;

/// <summary>
/// Unit tests for the custom <c>func-extension-bundle</c> constraint: allowed
/// when the project's bundle satisfies the template requirement, restricted
/// with a host.json call-to-action otherwise.
/// </summary>
public class ExtensionBundleConstraintTests
{
    private const string BundleId = "Microsoft.Azure.Functions.ExtensionBundle";
    private const string Args = $$"""{ "id": "{{BundleId}}", "version": "[4.0.0, )" }""";

    [Fact]
    public void Evaluate_BundlePresentAndInRange_Allowed()
    {
        var accessor = new FuncExtensionBundleContextAccessor
        {
            Current = new FuncExtensionBundleContext(BundleId, "4.5.0"),
        };
        var constraint = new ExtensionBundleConstraint("func-extension-bundle", accessor);

        TemplateConstraintResult result = constraint.Evaluate(Args);

        result.EvaluationStatus.Should().Be(TemplateConstraintResult.Status.Allowed);
    }

    [Fact]
    public void Evaluate_NoBundleConfigured_RestrictedWithHostJsonCallToAction()
    {
        var accessor = new FuncExtensionBundleContextAccessor { Current = null };
        var constraint = new ExtensionBundleConstraint("func-extension-bundle", accessor);

        TemplateConstraintResult result = constraint.Evaluate(Args);

        result.EvaluationStatus.Should().Be(TemplateConstraintResult.Status.Restricted);
        result.CallToAction.Should().Contain("host.json").And.Contain(BundleId);
    }

    [Fact]
    public void Evaluate_BundleVersionBelowRange_Restricted()
    {
        var accessor = new FuncExtensionBundleContextAccessor
        {
            Current = new FuncExtensionBundleContext(BundleId, "3.9.0"),
        };
        var constraint = new ExtensionBundleConstraint("func-extension-bundle", accessor);

        TemplateConstraintResult result = constraint.Evaluate(Args);

        result.EvaluationStatus.Should().Be(TemplateConstraintResult.Status.Restricted);
        result.LocalizedErrorMessage.Should().Contain("3.9.0");
        result.CallToAction.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void Evaluate_BundleIdMismatch_Restricted()
    {
        var accessor = new FuncExtensionBundleContextAccessor
        {
            Current = new FuncExtensionBundleContext("Contoso.Other.Bundle", "4.5.0"),
        };
        var constraint = new ExtensionBundleConstraint("func-extension-bundle", accessor);

        TemplateConstraintResult result = constraint.Evaluate(Args);

        result.EvaluationStatus.Should().Be(TemplateConstraintResult.Status.Restricted);
    }

    [Fact]
    public void Evaluate_MissingVersionArg_Restricted()
    {
        var accessor = new FuncExtensionBundleContextAccessor
        {
            Current = new FuncExtensionBundleContext(BundleId, "4.5.0"),
        };
        var constraint = new ExtensionBundleConstraint("func-extension-bundle", accessor);

        TemplateConstraintResult result = constraint.Evaluate($$"""{ "id": "{{BundleId}}" }""");

        result.EvaluationStatus.Should().Be(TemplateConstraintResult.Status.Restricted);
    }
}
