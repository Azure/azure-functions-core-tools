// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Text.Json;
using NuGet.Versioning;

namespace Azure.Functions.Cli.Templates.Engine;

/// <summary>
/// Retains a validated declaration without interpreting unknown constraint types.
/// </summary>
internal sealed record TemplateConstraintDeclaration(string Label, string Type, JsonElement? Arguments, IReadOnlyList<TemplateConstraintAlternative> Alternatives);

/// <summary>
/// Describes one workload or bundle requirement in a func constraint.
/// </summary>
internal sealed record TemplateConstraintAlternative(string? Identity, VersionRange? Version);