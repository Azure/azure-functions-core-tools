// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

namespace Azure.Functions.Cli.Templates.Engine;

/// <summary>
/// Command-facing definition of one template parameter, with its func host aliases and visibility applied.
/// </summary>
/// <param name="CanonicalName">Template symbol name. Invocation values are always keyed by this name.</param>
/// <param name="LongName">Long alias without the <c>--</c> prefix.</param>
/// <param name="ShortName">Short alias without the <c>-</c> prefix, or <c>null</c> when the parameter has none.</param>
/// <param name="Description">Parameter description, or <c>null</c> when the template gives none.</param>
/// <param name="DataType">Template engine data type, such as <c>string</c>, <c>bool</c> or <c>choice</c>.</param>
/// <param name="Choices">Allowed values in declaration order, or empty for other data types.</param>
/// <param name="DefaultValue">
/// Default value, or <c>null</c> when there is none. The engine ignores it for a required parameter, so callers pass it explicitly.
/// </param>
/// <param name="IsRequired">Whether the parameter is unconditionally required.</param>
/// <param name="IsHidden">Whether the parameter is hidden from ordinary details, including when the template disables it.</param>
/// <param name="AlwaysShow">Whether the parameter always appears in combined help.</param>
internal sealed record TemplateParameterDefinition(
    string CanonicalName,
    string LongName,
    string? ShortName,
    string? Description,
    string DataType,
    IReadOnlyList<TemplateParameterChoice> Choices,
    string? DefaultValue,
    bool IsRequired,
    bool IsHidden,
    bool AlwaysShow);

/// <summary>
/// One allowed value of a choice parameter.
/// </summary>
internal sealed record TemplateParameterChoice(string Value, string? DisplayName, string? Description);
