using System;
using System.Collections.Generic;
using System.Linq;

namespace Build
{
    internal class OrchestratorParser
    {
        private readonly IEnumerable<string> skipList = Array.Empty<string>();
        public string TargetToRun { get; } = string.Empty;

        public OrchestratorParser(IEnumerable<string> args)
        {
            if (!args.Any())
            {
                return;
            }

            var targetToRun = args.FirstOrDefault(el => !el.StartsWith("--"));

            if (!string.IsNullOrWhiteSpace(targetToRun) &&
                !targetToRun.StartsWith("skip:"))
            {
                this.TargetToRun = targetToRun;
            }

            skipList = args.Where(a => a.StartsWith("skip:")).Select(a => a.Split(":").Last());
        }

        public bool ShouldSkip(string target) => skipList.Any(i => i.Equals(target, StringComparison.OrdinalIgnoreCase));
    }
}