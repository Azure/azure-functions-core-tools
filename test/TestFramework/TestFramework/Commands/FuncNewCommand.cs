using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Xunit.Abstractions;

namespace TestFramework.Commands
{
    public class FuncNewCommand : FuncCommand
    {
        private string _funcPath;

        public FuncNewCommand(string funcPath, ITestOutputHelper log) : base(log)
        {
            _funcPath = funcPath;

        }

        protected override CommandInfo CreateCommand(IEnumerable<string> args)
        {
            var arguments = new List<string> { "new" }.Concat(args).ToList();

            var commandInfo = new CommandInfo()
            {
                FileName = _funcPath,
                Arguments = arguments,
                WorkingDirectory = WorkingDirectory,
            };
            return commandInfo;
        }
    }
}
