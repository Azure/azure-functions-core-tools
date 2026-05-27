// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Microsoft.Azure.WebJobs.Script;
using Microsoft.Azure.WebJobs.Script.Description;
using Microsoft.Extensions.DependencyInjection;

namespace Azure.Functions.Cli.Workloads.Host;

internal static class HostFunctionMetadataEmitter
{
    public static void EmitSnapshot(IServiceProvider services, TextWriter? writer = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        IFunctionMetadataManager? metadataManager = services.GetService<IFunctionMetadataManager>();
        if (metadataManager is null)
        {
            return;
        }

        foreach (FunctionMetadata metadata in metadataManager.GetFunctionMetadata())
        {
            HostStructuredEventWriter.WriteFunctionDiscovered(metadata, writer);
        }
    }
}
