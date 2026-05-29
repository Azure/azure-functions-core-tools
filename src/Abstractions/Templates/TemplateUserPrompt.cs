// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

namespace Azure.Functions.Cli.Templates;

/// <summary>
/// Engine-agnostic shape both V2 (`UserPrompt`s referenced by jobs/actions
/// `paramId`) and DotNet (`dotnet-templates.json` `parameters[]`, hydrated at
/// workload pack time from the upstream `template.json` + `dotnetcli.host.json`)
/// project their per-template option declarations into. Consumed by
/// `TemplateOptionHydrator` to produce per-template <c>Option&lt;T&gt;</c> instances
/// for the stage-B parse of <c>func new --template &lt;id&gt;</c>.
/// </summary>
/// <param name="Id">
/// Prompt id. For V2 this is the <c>UserPrompt.id</c>; for DotNet this is the
/// upstream <c>symbols.&lt;key&gt;</c> name. Kebab-cased by the hydrator to produce the
/// option long form (e.g. <c>authLevel</c> → <c>--auth-level</c>).
/// </param>
/// <param name="Description">
/// Option description rendered under <c>func new --template &lt;id&gt; --help</c>.
/// Sourced from V2 <c>UserPrompt.label</c> or DotNet
/// <c>parameters[].description</c> / <c>parameters[].displayName</c>.
/// </param>
/// <param name="DataType">
/// Logical data type. Recognised values: <c>"string"</c>, <c>"choice"</c>,
/// <c>"bool"</c>, <c>"int"</c>. Unknown values are treated as <c>"string"</c> by
/// the hydrator.
/// </param>
/// <param name="DefaultValue">
/// Default value as a string, or <c>null</c> when no default applies. Mapped to
/// <c>Option.DefaultValueFactory</c>.
/// </param>
/// <param name="Choices">
/// For <see cref="DataType"/> = <c>"choice"</c>, the allowed values. Mapped to
/// <c>Option.AcceptOnlyFromAmong(...)</c>. <c>null</c> for non-choice prompts.
/// </param>
/// <param name="IsRequired">
/// When <c>true</c> and no default is supplied, the parser errors if the option
/// is missing (or, in interactive mode, the orchestrator prompts).
/// </param>
/// <param name="ValidatorRegex">
/// Optional regex the option value must match. Sourced from V2
/// <c>UserPrompt.validator</c> or DotNet <c>parameters[].constraints</c>.
/// When the template carries no validator, the per-stack default from
/// <see cref="Projects.IProjectInitializer.DefaultFunctionNameValidator"/>
/// applies for the function-name prompt (function-name prompts only — other
/// prompts fall back to no validation).
/// </param>
/// <param name="ShortAlias">
/// Optional short option alias (e.g. <c>"-a"</c>). For DotNet, sourced from
/// <c>dotnetcli.host.json</c> <c>symbolInfo[].shortName</c>.
/// </param>
/// <param name="LongAlias">
/// Optional long option alias override. For DotNet, sourced from
/// <c>dotnetcli.host.json</c> <c>symbolInfo[].longName</c>. When set, this
/// replaces the default kebab-cased name derived from <see cref="Id"/>.
/// </param>
public sealed record TemplateUserPrompt(
    string Id,
    string? Description,
    string DataType,
    string? DefaultValue,
    IReadOnlyList<string>? Choices,
    bool IsRequired,
    string? ValidatorRegex,
    string? ShortAlias,
    string? LongAlias);
