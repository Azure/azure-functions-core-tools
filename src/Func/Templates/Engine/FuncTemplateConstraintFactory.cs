// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Text;
using System.Text.Json;
using Microsoft.TemplateEngine.Abstractions;
using Microsoft.TemplateEngine.Abstractions.Constraints;

namespace Azure.Functions.Cli.Templates.Engine;

internal sealed class FuncTemplateConstraintFactory(string type, FuncTemplateConstraintEvaluator evaluator, TemplateConstraintEvaluationSnapshot snapshot, TemplateEngineBundleContext? bundle = null) : ITemplateConstraintFactory
{
    private static readonly Guid _workloadConstraintId = new("d10ef758-4363-4359-a749-2a241ff892fd");
    private static readonly Guid _bundleConstraintId = new("1349d42c-b8b8-4611-ac8a-1426cc0bc169");
    private static readonly Guid _bundleBindingsConstraintId = new("f718bcad-8f82-423d-a7b0-526479f69151");

    private readonly FuncTemplateConstraintEvaluator _evaluator = evaluator ?? throw new ArgumentNullException(nameof(evaluator));
    private readonly TemplateConstraintEvaluationSnapshot _snapshot = snapshot ?? throw new ArgumentNullException(nameof(snapshot));

    public string Type { get; } = type is "func-workload" or "func-bundle" or "func-bundle-bindings"
        ? type : throw new ArgumentException("Unsupported func constraint type.", nameof(type));

    /// <summary>
    /// Provides a stable func-owned registration identity so the engine can resolve each constraint component.
    /// </summary>
    public Guid Id => Type switch
    {
        "func-workload" => _workloadConstraintId,
        "func-bundle" => _bundleConstraintId,
        _ => _bundleBindingsConstraintId,
    };

    public Task<ITemplateConstraint> CreateTemplateConstraintAsync(IEngineEnvironmentSettings environmentSettings, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(environmentSettings);
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult<ITemplateConstraint>(new Constraint(Type, _evaluator, _snapshot, bundle));
    }

    private sealed class Constraint(string type, FuncTemplateConstraintEvaluator evaluator, TemplateConstraintEvaluationSnapshot snapshot, TemplateEngineBundleContext? bundle) : ITemplateConstraint
    {
        public string Type => type;

        public string DisplayName => type;

        public TemplateConstraintResult Evaluate(string? args)
        {
            if (args is null)
            {
                return TemplateConstraintResult.CreateEvaluationFailure(this, "Constraint arguments are required.", "Correct the constraint arguments in template.json.");
            }

            try
            {
                using var arguments = JsonDocument.Parse(args, new() { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip });
                using var stream = new MemoryStream();
                using (Utf8JsonWriter writer = new(stream))
                {
                    writer.WriteStartObject();
                    writer.WriteStartObject("constraints");
                    writer.WriteStartObject("engine");
                    writer.WriteString("type", Type);
                    writer.WritePropertyName("args");
                    arguments.RootElement.WriteTo(writer);
                    writer.WriteEndObject();
                    writer.WriteEndObject();
                    writer.WriteEndObject();
                }

                TemplateConstraintDeclaration declaration = TemplateConstraintDeclarationReader.Read(Encoding.UTF8.GetString(stream.ToArray()))[0];
                if (Type == "func-bundle-bindings")
                {
                    (FuncTemplateConstraintOutcome outcome, string? diagnostic) = evaluator.EvaluateBundleBindings(declaration, bundle);
                    return outcome switch
                    {
                        FuncTemplateConstraintOutcome.Eligible => TemplateConstraintResult.CreateAllowed(this),
                        FuncTemplateConstraintOutcome.Restricted => TemplateConstraintResult.CreateRestricted(this, diagnostic!,
                            "Use a resolved extension bundle supplying every required binding, or choose another template."),
                        _ => TemplateConstraintResult.CreateEvaluationFailure(this, diagnostic!, "Repair the bundle context and start a new template flow."),
                    };
                }

                return evaluator.Evaluate(declaration, snapshot, bundle) == FuncTemplateConstraintOutcome.Eligible
                    ? TemplateConstraintResult.CreateAllowed(this)
                    : TemplateConstraintResult.CreateRestricted(this, $"The supplied command context does not satisfy {Type}.", "Review the required workload or bundle and start a new template flow.");
            }
            catch (InvalidTemplateMetadataException exception)
            {
                return TemplateConstraintResult.CreateEvaluationFailure(this, exception.Message, "Correct the constraint arguments in template.json.");
            }
            catch (JsonException exception)
            {
                return TemplateConstraintResult.CreateEvaluationFailure(this, exception.Message, "Correct the constraint arguments in template.json.");
            }
        }
    }
}