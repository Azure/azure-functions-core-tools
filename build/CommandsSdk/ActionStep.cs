using System;

namespace Build.CommandsSdk
{
    public class ActionStep : IStep
    {
        private readonly Action _run;
        private readonly string _name;
        public ActionStep(Action run, string name)
        {
            this._run = run;
            this._name = name ?? "Unnamed action step.";
        }

        public RunOutcome Run()
        {
            try
            {
                StaticLogger.WriteLine($"========> {this._name}");
                this._run();
                StaticLogger.WriteLine($"<======== {this._name}");
                return RunOutcome.Succeeded;
            }
            catch (Exception e)
            {
                StaticLogger.WriteErrorLine($"Error in step: {this._name}, error: {e.ToString()}");
                return RunOutcome.Failed;
            }
        }

    }

    public class ActionStep<T> : ActionStep
    {
        public ActionStep(Action<T> run, T param, string name)
            : base(() => { run(param); }, name)
        {
        }
    }
}