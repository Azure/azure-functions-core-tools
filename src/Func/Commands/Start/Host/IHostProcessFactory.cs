// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Diagnostics;

namespace Azure.Functions.Cli.Commands.Start.Host;

internal interface IHostProcessFactory
{
    public IHostProcess Create(ProcessStartInfo startInfo);
}
