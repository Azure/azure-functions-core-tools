// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

namespace Azure.Functions.Cli.Templates.Compatibility;

/// <summary>
/// Describes item-template compatibility across all requirements and report-consistency checks, not support for one capability.
/// Only Compatible passes the project-capability check.
/// </summary>
internal enum ItemTemplateCompatibility
{
    /// <summary>
    /// Requirements are absent or invalid, reports conflict, or a required capability is not reported as Supported.
    /// </summary>
    Incompatible,

    /// <summary>
    /// A valid declaration is present, reports are consistent, and every required capability is Supported.
    /// </summary>
    Compatible,
}
