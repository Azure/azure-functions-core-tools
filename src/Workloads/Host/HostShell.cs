// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

namespace Azure.Functions.Cli.Workloads.Host;

internal sealed class HostShell(IFunctionsHostRunner hostRunner)
{
    public const string EnableAuthArgument = "--enable-auth";

    private readonly IFunctionsHostRunner _hostRunner = hostRunner ?? throw new ArgumentNullException(nameof(hostRunner));

    public async Task<int> RunAsync(string[] args, CancellationToken cancellationToken = default)
    {
        (bool enableAuth, string[] hostArgs) = ParseArguments(args);
        await _hostRunner.RunAsync(hostArgs, enableAuth, cancellationToken);
        return 0;
    }

    private static (bool EnableAuth, string[] HostArgs) ParseArguments(string[] args)
    {
        bool enableAuth = false;
        List<string> hostArgs = [];

        foreach (string arg in args)
        {
            if (string.Equals(arg, EnableAuthArgument, StringComparison.Ordinal))
            {
                enableAuth = true;
                continue;
            }

            hostArgs.Add(arg);
        }

        return (enableAuth, [.. hostArgs]);
    }
}
