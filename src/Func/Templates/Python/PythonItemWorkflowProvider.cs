// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

namespace Azure.Functions.Cli.Templates.Python;

/// <summary>
/// Supplies unregistered CLI-owned Python item services without loading stack implementation types.
/// </summary>
internal sealed class PythonItemWorkflowProvider(PythonItemCompatibility compatibility, PythonSelectedItemEffects effects)
    : INativeItemWorkflowProvider
{
    public string Stack => "python";

    public INativeItemCompatibility Compatibility { get; } = compatibility ?? throw new ArgumentNullException(nameof(compatibility));

    public INativeSelectedItemEffects Effects { get; } = effects ?? throw new ArgumentNullException(nameof(effects));
}
