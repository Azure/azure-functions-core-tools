// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

namespace FunctionsCustomHost;

internal static class EnvironmentVariables
{
    /// <summary>
    /// Set value to "1" will prevent the log entries to have the prefix "LanguageWorkerConsoleLog".
    /// Set this to see logs when you are debugging FunctionsCustomHost locally with WebHost.
    /// </summary>
    internal const string DisableLogPrefix = "AZURE_FUNCTIONS_FUNCTIONSNETHOST_DISABLE_LOGPREFIX";

    /// <summary>
    /// Set value to "1" for enabling additional trace logs in FunctionsCustomHost.
    /// </summary>
    internal const string EnableTraceLogs = "AZURE_FUNCTIONS_FUNCTIONSNETHOST_TRACE";

    /// <summary>
    /// Application pool Id for the placeholder app. Only available in Windows(when running in IIS).
    /// </summary>
    internal const string AppPoolId  = "APP_POOL_ID";

    /// <summary>
    /// The worker runtime version. Example value: "8.0" (for a .NET8 placeholder)
    /// </summary>
    internal const string FunctionsWorkerRuntimeVersion = "FUNCTIONS_WORKER_RUNTIME_VERSION";

    /// <summary>
    /// The worker runtime. Example value: "dotnet-isolated"
    /// </summary>
    internal const string FunctionsWorkerRuntime = "FUNCTIONS_WORKER_RUNTIME";

    /// <summary>
    /// Determines if Functions InProc NET8 is enabled for a .NET 8 in-proc app
    /// </summary>
    internal const string FunctionsInProcNet8Enabled = "FUNCTIONS_INPROC_NET8_ENABLED";
}
