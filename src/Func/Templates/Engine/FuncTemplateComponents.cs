// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Microsoft.TemplateEngine.Abstractions;

namespace Azure.Functions.Cli.Templates.Engine;

/// <summary>
/// Provides func-owned TemplateEngine components.
/// </summary>
internal static class FuncTemplateComponents
{
    public static IReadOnlyList<(Type Type, IIdentifiedComponent Instance)> AllComponents { get; } = [];
}
