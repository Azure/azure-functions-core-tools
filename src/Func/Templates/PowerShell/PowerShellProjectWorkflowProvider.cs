// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Templates.Engine;
using Azure.Functions.Cli.Templates.Projects;

namespace Azure.Functions.Cli.Templates.PowerShell;

internal sealed class PowerShellProjectWorkflowProvider(PowerShellNativeProjectPreparation preparation) : INativeProjectWorkflowProvider
{
    private readonly PowerShellNativeProjectPreparation _preparation = preparation ?? throw new ArgumentNullException(nameof(preparation));

    public string Stack => "powershell";

    public INativeProjectPreparation Preparation => _preparation;

    public bool Handles(TemplateCatalogEntry entry) => OwnedProjectPreparationSource.Handles(entry, PowerShellNativeProjectPreparation.Definition);
}
