using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Azure.Functions.Cli.Interfaces;

namespace Azure.Functions.Cli.Common
{
    internal class ProcessManager : IProcessManager
    {
        private IList<Process> _childProcesses;
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

        public void KillChildProcesses()
        {
            if (_childProcesses == null)
            {
                return;
            }

            foreach (var childProcess in _childProcesses)
            {
                if (!childProcess.HasExited)
                {
                    childProcess.Kill();
                }
            }
        }

        public bool RegisterChildProcess(Process childProcess)
        {
            _childProcesses ??= new List<Process>();

            // be graceful if someone calls this method with the same process multiple times.
            if (_childProcesses.Any(p=>p.Id == childProcess.Id))
            {
                return false;
            }

            _childProcesses.Add(childProcess);
            return true;
        }
    }
}
