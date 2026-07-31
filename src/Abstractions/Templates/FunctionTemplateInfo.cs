// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

namespace Azure.Functions.Cli.Templates;

/// <summary>
/// The CLI's engine-agnostic view of a single template. Used by
/// <c>func new</c> (template selection, option hydration) and
/// <c>func new --list</c> (catalog rendering).
/// </summary>
/// <param name="Id">
/// Stack-unique template id (e.g. <c>"HttpTrigger"</c>). The value the user
/// passes to <c>--template &lt;id&gt;</c>.
/// </param>
/// <param name="Stack">
/// Canonical owning stack (e.g. <c>"node"</c>, <c>"python"</c>, <c>"dotnet"</c>).
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
    string DisplayName,
    string? Description,
    string? DefaultFunctionName,
    IReadOnlyList<string> Languages,
    TemplateMetadata Metadata)
{
    /// <summary>
    /// Every <c>shortName</c> the template declares, including the canonical
    /// <see cref="Id"/> and legacy suffixed aliases (e.g. <c>http</c>,
    /// <c>HttpTrigger</c>, <c>HttpTrigger-TypeScript</c>). <c>func new
    /// --template</c> resolves case-insensitively against this set so old
    /// scripts keep working (D8/D19). Defaults to just <see cref="Id"/> when
    /// the catalog surfaces no additional aliases.
    /// </summary>
    public IReadOnlyList<string> ShortNames { get; init; } = [];
}
