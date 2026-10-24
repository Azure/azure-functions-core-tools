// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

namespace Azure.Functions.Cli.Projects;

/// <summary>
/// Describes a runtime owner's knowledge of one semantic project capability.
/// </summary>
public enum ProjectCapabilitySupport
{
    /// <summary>
    /// The runtime has not established whether the project supports the capability.
    /// </summary>
    Unknown,

    /// <summary>
    /// The runtime reports that the project supports the capability.
    /// </summary>
    Supported,

    /// <summary>
    /// The runtime reports that the project does not support the capability.
    /// </summary>
    Unsupported,
}
