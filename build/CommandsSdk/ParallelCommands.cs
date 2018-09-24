using System.Linq;
using System.Threading.Tasks;

namespace Build.CommandsSdk
{
    internal class ParallelCommands : StandardCommands
    {
        public override RunOutcome Run()
        {
            var result = Task.WhenAll(this.script.Select(i => Task.Run(() => (run: i.step.Run(), stopOnError: i.stopOnError)))).Result;
            return result.Any(r => r.run == RunOutcome.Failed && r.stopOnError)
                ? RunOutcome.Failed
                : RunOutcome.Succeeded;
        }
    }
}