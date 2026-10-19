// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

namespace Azure.Functions.Cli.Tests.Common;

internal sealed class LinuxFifoFactAttribute : FactAttribute
{
    public LinuxFifoFactAttribute()
    {
        if (!OperatingSystem.IsLinux())
        {
            Skip = "Requires a real Linux FIFO; this platform cannot provide Linux execution evidence.";
        }
    }
}
