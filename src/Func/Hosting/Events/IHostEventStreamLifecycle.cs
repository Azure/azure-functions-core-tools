// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

namespace Azure.Functions.Cli.Hosting.Events;

internal interface IHostEventStreamLifecycle
{
    public Task RequestShutdownAsync(CancellationToken cancellationToken);

    public Task<int> WaitForExitAsync(CancellationToken cancellationToken);
}
