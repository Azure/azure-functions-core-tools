using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Azure.Functions.Cli.Interfaces;

namespace Azure.Functions.Cli.Common
{
    internal class ProcessManager : IProcessManager
    {
        public IProcessInfo GetCurrentProcess()
        {
            return new ProcessInfo(Process.GetCurrentProcess());
        }

        public IProcessInfo GetProcessById(int processId)
        {
            return new ProcessInfo(Process.GetProcessById(processId));
        }

        public IEnumerable<IProcessInfo> GetProcessesByName(string processName)
        {
            return Process.GetProcessesByName(processName)
                .Select(p => new ProcessInfo(p));
        }
    }
}
