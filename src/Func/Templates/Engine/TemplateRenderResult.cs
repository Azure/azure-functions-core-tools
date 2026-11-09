// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Collections.Immutable;

namespace Azure.Functions.Cli.Templates.Engine;

/// <summary>
/// Immutable output generated privately by the Template Engine, before files are written to the project.
/// </summary>
internal sealed record TemplateRenderResult(
    string TemplateIdentity,
    ImmutableDictionary<string, ImmutableArray<byte>> Files,
    ImmutableArray<string> Directories,
    ImmutableArray<(Guid ActionId, ImmutableDictionary<string, string> Arguments, bool ContinueOnError)> PostActions);
