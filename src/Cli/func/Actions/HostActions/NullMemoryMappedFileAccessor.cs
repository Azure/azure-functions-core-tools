// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.IO.MemoryMappedFiles;
using Microsoft.Azure.WebJobs.Script.Workers.SharedMemoryDataTransfer;
using Microsoft.Extensions.Logging;

namespace Azure.Functions.Cli.Actions.HostActions;

/// <summary>
/// A null implementation of MemoryMappedFileAccessor that disables shared memory functionality.
/// This can be used for testing or in environments where shared memory is not supported or desired.
/// </summary>
public class NullMemoryMappedFileAccessor : MemoryMappedFileAccessor
{
    public NullMemoryMappedFileAccessor(ILogger<MemoryMappedFileAccessor> logger)
        : base(logger)
    {
        Logger.LogDebug("Using NullMemoryMappedFileAccessor - shared memory data transfer is disabled");
    }

    public override bool TryCreate(string mapName, long size, out MemoryMappedFile mmf)
    {
        mmf = null;
        Logger.LogDebug("NullMemoryMappedFileAccessor: TryCreate called for {MapName} with size {Size} - returning false", mapName, size);
        return false;
    }

    public override bool TryOpen(string mapName, out MemoryMappedFile mmf)
    {
        mmf = null;
        Logger.LogDebug("NullMemoryMappedFileAccessor: TryOpen called for {MapName} - returning false", mapName);
        return false;
    }

    public override void Delete(string mapName, MemoryMappedFile mmf)
    {
        Logger.LogDebug("NullMemoryMappedFileAccessor: Delete called for {MapName} - no action taken", mapName);
        mmf?.Dispose();
    }
}
