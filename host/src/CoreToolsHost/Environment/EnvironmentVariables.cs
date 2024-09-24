// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

namespace CoreToolsHost;

internal static class EnvironmentVariables
{
    /// <summary>
    /// The worker runtime. Example value: "dotnet-isolated"
    /// </summary>
    internal const string FunctionsWorkerRuntime = "FUNCTIONS_WORKER_RUNTIME";

    /// <summary>
    /// Determines if Functions InProc NET8 is enabled for a .NET 8 in-proc app
    /// </summary>
    internal const string FunctionsInProcNet8Enabled = "FUNCTIONS_INPROC_NET8_ENABLED";
}
