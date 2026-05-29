// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

namespace Azure.Functions.Cli.Templates;

/// <summary>
/// The CLI's engine-agnostic view of a single template surfaced by an
/// <see cref="ITemplateEngineProvider"/>. Used by <c>func new</c> (template
/// selection, option hydration, dispatch) and <c>func new --list</c>
/// (catalog rendering).
/// </summary>
/// <param name="Id">
/// Stack-unique template id (e.g. <c>"HttpTrigger"</c>). For V2 this is the
/// <c>NewTemplate.id</c>; for DotNet this is <c>dotnet-templates.json</c>'s
/// <c>id</c> (= <c>shortNames[0]</c>). The value the user passes to
/// <c>--template &lt;id&gt;</c>.
/// </param>
/// <param name="Stack">
/// Canonical owning stack (e.g. <c>"node"</c>, <c>"python"</c>, <c>"dotnet"</c>).
/// </param>
/// <param name="EngineId">
/// Dispatch key resolved from the workload payload's directory layout.
/// One of <see cref="EngineIds.V2"/> or <see cref="EngineIds.DotNet"/>.
/// Never surfaced to the user.
/// </param>
/// <param name="DisplayName">
/// Human-friendly name (e.g. <c>"HTTP trigger"</c>). Rendered in the
/// interactive picker and as the second column of <c>func new --list</c>.
/// </param>
/// <param name="Description">
/// One-line description used for <c>--help</c> and the <c>DESCRIPTION</c> column
/// of <c>func new --list</c>. May be <c>null</c> when the template carries no
/// description.
/// </param>
/// <param name="DefaultFunctionName">
/// Default value for <c>--name</c> when the user does not supply one. May be
/// <c>null</c> when the template has no opinion (the runner falls back to
/// <see cref="Id"/>).
/// </param>
/// <param name="Languages">
/// Canonical languages this template applies to (e.g. <c>["javascript"]</c>,
/// <c>["csharp", "fsharp"]</c> for a DotNet template that ships C# + F# variants
/// sharing a <c>groupIdentity</c>). Empty for stack-default (single-language)
/// templates.
/// </param>
/// <param name="Metadata">
/// Schema-driven metadata that powers option hydration and the bundle gates.
/// </param>
public sealed record FunctionTemplateInfo(
    string Id,
    string Stack,
    string EngineId,
    string DisplayName,
    string? Description,
    string? DefaultFunctionName,
    IReadOnlyList<string> Languages,
    TemplateMetadata Metadata);
