// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System;
using System.Collections.Generic;
using System.Text;

namespace Azure.Functions.Cli.Common;

/// <summary>
/// Represents runtime stack information for a Functions project.
/// </summary>
/// <param name="StackDisplayName">The display name of the runtime stack.</param>
/// <param name="StackName">The name of the runtime stack.</param>
/// <param name="WorkerConfigPath">The file path to the worker.config file.</param>
/// <param name="SupportsBundles">Indicates whether the runtime stack supports extension bundles.</param>
public sealed record RuntimeStackInfo(
    string StackDisplayName,
    string StackName,
    string WorkerConfigPath,
    bool SupportsBundles)
{
}
