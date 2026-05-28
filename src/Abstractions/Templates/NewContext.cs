// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Common;

namespace Azure.Functions.Cli.Templates;

/// <summary>
/// Input to <see cref="ITemplateEngineProvider.ApplyAsync"/>. Carries the
/// already-resolved template, function name, working directory, and language
/// so engines have everything they need to materialise files without
/// re-running the resolution pipeline.
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
public sealed record NewContext(
    WorkingDirectory WorkingDirectory,
    FunctionTemplateInfo Template,
    string FunctionName,
    string? Language,
    bool Force);
