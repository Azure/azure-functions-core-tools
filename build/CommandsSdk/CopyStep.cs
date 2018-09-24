using System;
using System.Runtime.InteropServices;

namespace Build.CommandsSdk
{
    public class CopyStep : IStep
    {
        private string source;
        private string destination;
        private CmdStep _step;

        public CopyStep(string source, string destination, int tries = 1)
        {
            this.source = source;
            this.destination = destination;
            var program = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
                ? "ROBOCOPY"
                : "cp";
            var args = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
                ? $"\"{source}\" \"{destination}\" /E /IS"
                : $"-R \"{source}\" \"{destination}\"";

            this._step = new CmdStep(program, args, tries);
        }

        public RunOutcome Run() => this._step.Run();
    }
}