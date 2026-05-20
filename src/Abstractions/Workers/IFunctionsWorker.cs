// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

namespace Azure.Functions.Cli.Workers;

/// <summary>
/// Describes a resolved Functions worker runtime.
/// </summary>
public interface IFunctionsWorker
{
    public FunctionsWorkerId Id { get; }

    public string WorkerRuntime { get; }

    public string WorkerConfigPath { get; }

    public string Version { get; }
}
