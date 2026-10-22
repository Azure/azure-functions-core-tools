// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Templates.Engine;

namespace Azure.Functions.Cli.Templates;

internal sealed record NewExecutionRequest(
    WorkingDirectory WorkingDirectory,
    string? TemplateReference,
    string? Name,
    string? ExplicitLanguage,
    bool Force,
    bool NonInteractive,
    bool DryRun,
    bool List,
    bool JsonOutput,
    IReadOnlyList<string> RawTemplateTokens,
    IReadOnlySet<string> ReservedAliases)
{
    public NativeItemOutputIntent OutputIntent { get; init; } = NativeItemOutputIntent.LiteralOutput;

    public INativeItemCompatibility? ItemCompatibility { get; init; }

    public INativeSelectedItemEffects? SelectedItemEffects { get; init; }
}

internal sealed record NewExecutionResult(string Kind, bool Succeeded)
{
    public TemplateEngineContext? Context { get; init; }

    public IReadOnlyList<string> Diagnostics { get; init; } = [];

    public IReadOnlyList<TemplateCatalogEntry> Templates { get; init; } = [];

    public IReadOnlyList<TemplateCandidateParseResult> ArgumentResults { get; init; } = [];

    public TemplateCatalogEntry? SelectedTemplate { get; init; }

    public TemplateInvocationResult? Invocation { get; init; }

    public NativeItemCompatibilityResult? ItemName { get; init; }

    internal bool HasCompletedCreation => Kind == "created" && Succeeded && Invocation?.Status == TemplateInvocationStatus.Success;

    internal bool HasReturnedCreationResult => HasCompletedCreation || (Kind == "invocationFailed"
        && (Invocation?.HasCommittedOutput == true || Invocation?.Effects?.Kind is "applied" or "partialFailure" or "incomplete"));

    public static NewExecutionResult Reject(string kind, params string[] diagnostics) => new(kind, false) { Diagnostics = diagnostics };
}
