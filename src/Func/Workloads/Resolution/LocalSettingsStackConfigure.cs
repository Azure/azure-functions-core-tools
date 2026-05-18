// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Microsoft.Extensions.Options;

namespace Azure.Functions.Cli.Workloads.Resolution;

/// <summary>
/// Sources <see cref="StackOptions.Stack"/> from
/// <c>FUNCTIONS_WORKER_RUNTIME</c> in <c>local.settings.json</c> for the
/// current working directory. Registered as the lower-precedence source;
/// any later <see cref="IConfigureOptions{TOptions}"/> (e.g. project
/// config) overrides it.
/// </summary>
internal sealed class LocalSettingsStackConfigure(ILocalSettingsReader localSettings)
    : IConfigureOptions<StackOptions>
{
    private readonly ILocalSettingsReader _localSettings =
        localSettings ?? throw new ArgumentNullException(nameof(localSettings));

    public void Configure(StackOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        string? runtime = _localSettings.ReadWorkerRuntime(new DirectoryInfo(Environment.CurrentDirectory));
        if (!string.IsNullOrWhiteSpace(runtime))
        {
            options.Stack = runtime;
        }
    }
}
