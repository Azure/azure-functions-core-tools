using System;
using System.Collections.Generic;
using System.Linq;
using Build.CommandsSdk;

namespace Build.CommandsSdk
{
    public class StandardCommands : ICommands
    {
        protected IList<(IStep step, bool stopOnError)> script = new List<(IStep, bool)>();
        protected Action _onFail;
        protected Action _onSuccess;
        public ICommands AddStep(Action run, bool stopOnError = true, string name = null)
        {
            script.Add((new ActionStep(run, name), stopOnError));
            return this;
        }

        public ICommands Call(string program, string args, int tries = 1, bool stopOnError = true)
        {
            script.Add((new CmdStep(program, args, tries), stopOnError));
            return this;
        }

        public ICommands ChangeDirectory(string directory)
        {
            script.Add((new ChangeDirectoryStep(directory), true));
            return this;
        }

        public ICommands Copy(string source, string destination)
        {
            script.Add((new CopyStep(source, destination), true));
            return this;
        }

        public ICommands OnFail(Action action)
        {
            this._onFail = action;
            return this;
        }

        public ICommands OnSuccess(Action action)
        {
            this._onSuccess = action;
            return this;
        }

        public ICommands ParallelCall(Func<ICommands, ICommands> calls)
        {
            script.Add((new ParallelStep(calls), true));
            return this;
        }

        public virtual RunOutcome Run()
        {
            if (script.Any())
            {
                foreach ((var step, var stopOnError) in script)
                {
                    var result = step.Run();
                    if (result == RunOutcome.Failed && stopOnError)
                    {
                        this._onFail?.Invoke();
                        return RunOutcome.Failed;
                    }
                }
                this._onSuccess?.Invoke();
            }
            return RunOutcome.Succeeded;
        }
    }
}