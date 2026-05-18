// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Microsoft.Extensions.Options;

namespace Azure.Functions.Cli.Workloads.Resolution;

/// <summary>
/// Sources <see cref="StackOptions.Stack"/> from the <c>stack</c> field of
/// <c>.func/config.json</c> for the current working directory. Registered
/// after <see cref="LocalSettingsStackConfigure"/> so a project-pinned
/// stack wins over <c>FUNCTIONS_WORKER_RUNTIME</c>.
/// </summary>
internal sealed class FuncProjectStackConfigure(IFuncProjectConfigReader projectConfig)
    : IConfigureOptions<StackOptions>
{
    private readonly IFuncProjectConfigReader _projectConfig =
        projectConfig ?? throw new ArgumentNullException(nameof(projectConfig));

    public void Configure(StackOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        FuncProjectConfig? config = _projectConfig.Read(new DirectoryInfo(Environment.CurrentDirectory));
        if (!string.IsNullOrWhiteSpace(config?.Stack))
        {
            options.Stack = config.Stack;
        }
    }
}
