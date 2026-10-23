// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

namespace Azure.Functions.Cli.Workloads.Node.Tests;

internal sealed class LoadedNodeNpmInstaller(Func<string, CancellationToken, Task<(int ExitCode, string Stderr)>> run) : NodeNpmInstaller
{
    private readonly Func<string, CancellationToken, Task<(int ExitCode, string Stderr)>> _run =
        run ?? throw new ArgumentNullException(nameof(run));

    public int Calls { get; private set; }

    public override Task<(int ExitCode, string Stderr)> InstallRequiredAsync(string workingDirectory, CancellationToken cancellationToken)
    {
        Calls++;
        return _run(workingDirectory, cancellationToken);
    }
}
