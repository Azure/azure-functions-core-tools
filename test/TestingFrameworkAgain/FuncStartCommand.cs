using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SampleTestingUpdate
{
    public class FuncStartCommand : FuncCommand
    {
        public FuncStartCommand(ILogger log, params string[] args) : base(log)
        {
            
        }

        protected override CommandInfo CreateCommand(IEnumerable<string> args)
        {
            var arguments = new List<string> { "start" }.Concat(args).ToList();

            var commandInfo = new CommandInfo()
            {
                FileName = Environment.GetEnvironmentVariable("FUNC_PATH"),
                Arguments = arguments,
                WorkingDirectory = WorkingDirectory,
            };
            return commandInfo;
        }
    }
}
