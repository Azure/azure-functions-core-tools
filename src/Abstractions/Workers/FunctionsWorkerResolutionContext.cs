// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

namespace Azure.Functions.Cli.Workers;

/// <summary>
/// Inputs used by a worker reference to resolve a concrete worker.
/// </summary>
public sealed record FunctionsWorkerResolutionContext
{
    public FunctionsWorkerResolutionContext(IFunctionsWorkerResolver resolver)
    {
        Resolver = resolver ?? throw new ArgumentNullException(nameof(resolver));
    }

    public IFunctionsWorkerResolver Resolver { get; }
}
