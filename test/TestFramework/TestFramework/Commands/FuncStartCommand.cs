using Func.TestFramework;
using Func.TestFramework.Commands;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Xunit.Abstractions;

namespace TestFramework.Commands
{
    public class FuncStartCommand: FuncCommand
    {
        private string _funcPath;
        private string? _methodName;

        public FuncStartCommand(string funcPath, ITestOutputHelper log, string? methodName) : base(log)
        {
            _funcPath = funcPath;
            _methodName = methodName;

        }

        protected override CommandInfo CreateCommand(IEnumerable<string> args)
        {
            var arguments = new List<string> { "start" }.Concat(args).ToList();

            var commandInfo = new CommandInfo()
            {
                FileName = _funcPath,
                Arguments = arguments,
                WorkingDirectory = WorkingDirectory,
                TestName = _methodName,
            };
            return commandInfo;
        }
    }
}
