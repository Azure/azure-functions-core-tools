// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Microsoft.TemplateEngine.Abstractions;

namespace Azure.Functions.Cli.Templates.Engine;

/// <summary>
/// Input for a single post action the engine emitted for an instantiated
/// template. Handlers key off <see cref="PostAction"/>'s <c>ActionId</c>.
/// </summary>
/// <param name="PostAction">The engine-declared post action to run.</param>
/// <param name="OutputBasePath">
/// Absolute path the template was instantiated into. For an append-flow
/// template this is the provider-owned staging directory, not the project.
/// </param>
/// <param name="CreatedFiles">Files the engine created, relative to <paramref name="OutputBasePath"/>.</param>
/// <param name="ProjectDirectory">Absolute path to the user's project — the only tree a handler may modify.</param>
/// <param name="FunctionName">The resolved function name (the engine's source-name replacement).</param>
/// <param name="ParameterValues">Resolved template parameter values, keyed by symbol name.</param>
internal sealed record FuncPostActionContext(
    IPostAction PostAction,
    string OutputBasePath,
    IReadOnlyList<string> CreatedFiles,
    string ProjectDirectory,
    string FunctionName,
    IReadOnlyDictionary<string, string?> ParameterValues);
