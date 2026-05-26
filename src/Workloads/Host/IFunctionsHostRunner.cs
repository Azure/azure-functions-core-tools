// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

namespace Azure.Functions.Cli.Workloads.Host;

internal interface IFunctionsHostRunner
{
    public Task RunAsync(string[] args, bool enableAuth, CancellationToken cancellationToken);
}
