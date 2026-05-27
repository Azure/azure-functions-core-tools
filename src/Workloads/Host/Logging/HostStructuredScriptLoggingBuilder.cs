// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Microsoft.Azure.WebJobs.Script;
using Microsoft.Extensions.Logging;

namespace Azure.Functions.Cli.Workloads.Host.Logging;

internal sealed class HostStructuredScriptLoggingBuilder : IConfigureBuilder<ILoggingBuilder>
{
    public void Configure(ILoggingBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.AddHostStructuredLogging();
    }
}
