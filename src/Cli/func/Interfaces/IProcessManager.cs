// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Diagnostics;

namespace Azure.Functions.Cli.Interfaces
{
    internal interface IProcessManager
    {
        internal IEnumerable<IProcessInfo> GetProcessesByName(string processName);

        internal IProcessInfo GetCurrentProcess();

        internal IProcessInfo GetProcessById(int processId);

        /// <summary>
        /// Register a child process spawned by the current process.
        /// </summary>
        /// <param name="childProcess">Child process.</param>
        /// <returns>True if the process was registered, else False.</returns>
        internal bool RegisterChildProcess(Process childProcess);

        // Kill all child processes spawned by the current process.
        internal void KillChildProcesses();
    }
}
