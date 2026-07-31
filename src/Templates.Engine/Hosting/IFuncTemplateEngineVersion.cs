// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

namespace Azure.Functions.Cli.Templates.Engine;

/// <summary>
/// Supplies the version the templating engine host reports and that scopes the
/// on-disk hive (<c>&lt;func-home&gt;/template-engine/func/&lt;version&gt;</c>). Kept
/// behind an interface so the value equals the running CLI version in
/// production yet can be pinned in tests.
/// </summary>
internal interface IFuncTemplateEngineVersion
{
    /// <summary>
    /// Semantic version without build metadata (e.g. <c>"5.0.0-preview.2"</c>).
    /// </summary>
    public string Version { get; }
}
