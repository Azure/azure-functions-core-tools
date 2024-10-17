using System.Collections.Generic;
using System.Diagnostics;

namespace Azure.Functions.Cli.Interfaces
{
    internal interface IProcessManager
    {
        IEnumerable<IProcessInfo> GetProcessesByName(string processName);
        IProcessInfo GetCurrentProcess();
        IProcessInfo GetProcessById(int processId);

        /// <summary>
        /// Register a child process spawned by the current process.
        /// </summary>
        /// <param name="childProcess"></param>
        /// <returns>True if the process was registered, else False.</returns>
        bool RegisterChildProcess(Process childProcess);

        // Kill all child processes spawned by the current process.
        void KillChildProcesses();
    }
}
