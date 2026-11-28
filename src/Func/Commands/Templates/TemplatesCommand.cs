// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Hosting;

namespace Azure.Functions.Cli.Commands.Templates;

/// <summary>
/// Groups project-independent template package management commands.
/// </summary>
internal sealed class TemplatesCommand : FuncCliCommand, IBuiltInCommand
{
    public TemplatesCommand(TemplatesInstallCommand install, TemplatesUninstallCommand uninstall)
        : base("templates", "Install and uninstall template packages.")
    {
        ArgumentNullException.ThrowIfNull(install);
        ArgumentNullException.ThrowIfNull(uninstall);
        Subcommands.Add(install);
        Subcommands.Add(uninstall);
    }
}
