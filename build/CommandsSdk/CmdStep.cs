using System;

namespace Build.CommandsSdk
{
    public class CmdStep : IStep
    {
        private readonly int _tries;
        private readonly string _args;
        private readonly string _program;

        public CmdStep(string program, string args, int tries)
        {
            this._program = program;
            this._args = args;
            this._tries = tries;
        }

        public virtual RunOutcome Run()
        {
            var statusCode = InternalRun();
            return statusCode != 0
                ? RunOutcome.Failed
                : RunOutcome.Succeeded;
        }

        protected int InternalRun()
        {
            var program = Environment.ExpandEnvironmentVariables(this._program);
            var args = Environment.ExpandEnvironmentVariables(this._args);
            var executable = new InternalCmd(program, args, streamOutput: true);
            var statusCode = executable.Run(StaticLogger.WriteLine, StaticLogger.WriteErrorLine);
            return statusCode;
        }
    }
}