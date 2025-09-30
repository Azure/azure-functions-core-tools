// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Helpers;

namespace Azure.Functions.Cli.Commands.Init;

public static class ProjectInitializerFactory
{
    public static IProjectInitializer Get(WorkerRuntime runtime) => runtime switch
    {
        WorkerRuntime.Dotnet => new DotNetProjectInitializer(inProc: true),
        WorkerRuntime.DotnetIsolated => new DotNetProjectInitializer(),
        WorkerRuntime.Node => new NodeProjectInitializer(),

        // WorkerRuntime.Python => new PythonInitWorker(),
        // WorkerRuntime.Powershell => new PowerShellInitWorker(),
        // WorkerRuntime.Custom => new CustomInitWorker(),
        _ => throw new CliException($"Unsupported worker runtime: {runtime}")
    };
}
