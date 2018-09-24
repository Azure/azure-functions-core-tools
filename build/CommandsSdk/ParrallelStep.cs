using System;

namespace Build.CommandsSdk
{
    public class ParallelStep : IStep
    {
        private Func<ICommands, ICommands> calls;

        public ParallelStep(Func<ICommands, ICommands> calls)
        {
            this.calls = calls;
        }

        public RunOutcome Run()
        {
            return calls(new ParallelCommands()).Run();
        }
    }
}