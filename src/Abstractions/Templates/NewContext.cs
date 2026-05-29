// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Common;

namespace Azure.Functions.Cli.Templates;

/// <summary>
/// Input to <see cref="ITemplateEngineProvider.ApplyAsync"/>. Carries the
/// already-resolved template, function name, working directory, language,
/// and (for Node/Python) the channel-matched workload install directory so
/// engines have everything they need to materialise files without re-running
/// the resolution pipeline.
/// </summary>
/// <param name="WorkingDirectory">The resolved project working directory.</param>
/// <param name="Template">
/// The template the user (or picker) chose. The provider must already have
/// surfaced this from a prior <see cref="ITemplateEngineProvider.ListTemplatesAsync"/>
/// call.
/// </param>
/// <param name="FunctionName">
/// The function name to scaffold. The runner resolves this from
/// <c>--name</c> or the interactive prompt before calling the provider.
/// </param>
/// <param name="Language">
/// Resolved language id. May be <c>null</c> only for engines that
/// genuinely don't need it; both V2 and DotNet do.
/// </param>
/// <param name="Force">
/// When <c>true</c>, overwrite existing files in the working directory
/// (the <c>--force</c> flag).
/// </param>
/// <param name="InstallDirectory">
/// Absolute path to the install directory of the templates content workload
/// the orchestrator selected for this invocation (§4.8.1). Providers must
/// load payload from this directory rather than re-running workload
/// selection — otherwise the orchestrator's channel match is silently
/// discarded. <c>null</c> when the provider is invoked outside the
/// orchestrator; providers then fall back to a best-effort highest-version
/// pick from <see cref="IInstalledTemplatesWorkloads"/>.
/// </param>
/// <param name="UserOptionValues">
/// User-supplied per-prompt overrides resolved from the CLI parse,
/// keyed by the v2 paramId / DotNet parameter name. Engines layer these
/// over each prompt's declared default before applying. <c>null</c> when
/// the user supplied no overrides (the engine's prompt-default path
/// applies as usual).
/// </param>
public sealed record NewContext(
    WorkingDirectory WorkingDirectory,
    FunctionTemplateInfo Template,
    string FunctionName,
    string? Language,
    bool Force,
    string? InstallDirectory = null,
    IReadOnlyDictionary<string, string?>? UserOptionValues = null);
