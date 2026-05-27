// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using NuGet.Versioning;

namespace Azure.Functions.Cli.Workers;

/// <summary>
/// Creates worker resolvers for one operation.
/// </summary>
internal interface IFunctionsWorkerResolverFactory
{
    public IFunctionsWorkerResolver Create(IReadOnlyDictionary<string, VersionRange> workerVersionRanges);
}
