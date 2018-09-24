using System;

namespace Build.CommandsSdk
{
    public interface ICommands
    {
        ICommands Call(string program, string args, int tries = 1, bool stopOnError = true);
        ICommands ChangeDirectory(string directory);
        ICommands ParallelCall(Func<ICommands, ICommands> calls);
        ICommands Copy(string source, string destination);
        ICommands AddStep(Action run, bool stopOnError = true, string name = null);
        ICommands OnSuccess(Action action);
        ICommands OnFail(Action action);
        RunOutcome Run();
    }
}