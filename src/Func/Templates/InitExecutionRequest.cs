// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Configuration;
using Azure.Functions.Cli.Templates.Engine;

namespace Azure.Functions.Cli.Templates;

internal sealed record InitExecutionRequest(
    WorkingDirectory WorkingDirectory,
    string? TemplateReference,
    string? Name,
    string? ExplicitStack,
    string? ExplicitLanguage,
    bool Force,
    bool NonInteractive,
    bool DryRun,
    bool JsonOutput,
    IReadOnlyList<string> RawTemplateTokens,
    IReadOnlySet<string> ReservedAliases)
{
    internal Acquisition.NativeTemplateSelection? ArtifactBinding { get; init; }

    internal bool RefreshCapabilities { get; init; }

    internal bool ConfirmCreation { get; init; }
}

internal sealed record InitExecutionResult(string Kind, bool Succeeded)
{
    public TemplateEngineContext? Context { get; init; }

    public IReadOnlyList<string> Diagnostics { get; init; } = [];

    public IReadOnlyList<TemplateCatalogEntry> Templates { get; init; } = [];

    public IReadOnlyList<TemplateCatalogEntry> UnavailableTemplates { get; init; } = [];

    public IReadOnlyList<TemplateCandidateParseResult> ArgumentResults { get; init; } = [];

    public TemplateCatalogEntry? SelectedTemplate { get; init; }

    public TemplateInvocationResult? Preview { get; init; }

    public TemplateInvocationResult? Invocation { get; init; }

    public InitExistingProjectConfiguration? Configuration { get; init; }

    public static InitExecutionResult Reject(string kind, params string[] diagnostics) => new(kind, false) { Diagnostics = diagnostics };
}

internal sealed record InitExistingProjectConfiguration(string Root, string Stack, string Language, bool Repair, bool Written = false)
{
    public string Path => CliConfigurationPathsOptions.GetProjectConfigPath(new(Root));
}
