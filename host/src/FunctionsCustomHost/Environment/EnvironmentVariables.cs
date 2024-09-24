// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

namespace FunctionsCustomHost;

internal static class EnvironmentVariables
{
    /// <summary>
    /// Set value to "1" for enabling additional trace logs in FunctionsCustomHost.
    /// </summary>
    internal const string EnableTraceLogs = "AZURE_FUNCTIONS_FUNCTIONCUSTOMHOST_TRACE";

    /// <summary>
    /// The worker runtime. Example value: "dotnet-isolated"
    /// </summary>
    internal const string FunctionsWorkerRuntime = "FUNCTIONS_WORKER_RUNTIME";

    /// <summary>
    /// Determines if Functions InProc NET8 is enabled for a .NET 8 in-proc app
    /// </summary>
    internal const string FunctionsInProcNet8Enabled = "FUNCTIONS_INPROC_NET8_ENABLED";
}
