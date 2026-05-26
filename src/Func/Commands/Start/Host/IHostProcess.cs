// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

namespace Azure.Functions.Cli.Commands.Start.Host;

internal interface IHostProcess : IAsyncDisposable
{
    public TextReader StandardOutput { get; }

    public TextReader StandardError { get; }

    public TextWriter StandardInput { get; }

    public bool HasExited { get; }

    public int ExitCode { get; }

    public void Start();

    public Task WaitForExitAsync(CancellationToken cancellationToken);

    public void KillTree();
}
