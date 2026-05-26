// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Diagnostics;

namespace Azure.Functions.Cli.Commands.Start.Host;

internal sealed class DefaultHostProcessFactory : IHostProcessFactory
{
    public IHostProcess Create(ProcessStartInfo startInfo)
    {
        ArgumentNullException.ThrowIfNull(startInfo);
        return new ProcessHostProcess(new Process { StartInfo = startInfo });
    }
}
