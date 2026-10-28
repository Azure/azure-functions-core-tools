// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

namespace Azure.Functions.Cli.Templates.Compatibility;

/// <summary>
/// Describes item-template compatibility across all requirements and report-consistency checks, not support for one capability.
/// </summary>
internal enum ItemTemplateCompatibility
{
    Unknown,
    Compatible,
    Incompatible,
}
