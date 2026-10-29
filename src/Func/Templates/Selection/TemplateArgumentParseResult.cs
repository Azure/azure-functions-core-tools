// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Collections.Immutable;
using Azure.Functions.Cli.Templates.Engine;

namespace Azure.Functions.Cli.Templates.Selection;

/// <summary>
/// Records a candidate parse without treating missing required input as invalid explicit input.
/// </summary>
internal sealed record TemplateArgumentParseResult(
    TemplateDescriptor Template,
    ImmutableDictionary<string, string> ValuesByCanonicalName,
    ImmutableHashSet<string> ExplicitParameters,
    ImmutableDictionary<string, ImmutableArray<string>> AliasesByCanonicalName,
    ImmutableArray<TemplateParameterDefinition> MissingRequiredParameters,
    ImmutableArray<string> Diagnostics)
{
    public bool IsArgumentCompatible => Diagnostics.IsEmpty;

    public bool IsComplete => IsArgumentCompatible && MissingRequiredParameters.IsEmpty;
}
