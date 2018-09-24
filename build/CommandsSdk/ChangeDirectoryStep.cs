using System;
using System.IO;

namespace Build.CommandsSdk
{
    public class ChangeDirectoryStep : IStep
    {
        private string directory;

        public ChangeDirectoryStep(string directory)
        {
            this.directory = Environment.ExpandEnvironmentVariables(directory);
        }

        public RunOutcome Run()
        {
            Directory.SetCurrentDirectory(directory);
            return RunOutcome.Succeeded;
        }
    }
}