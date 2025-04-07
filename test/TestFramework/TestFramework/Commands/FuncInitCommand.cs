using Func.TestFramework;
using Func.TestFramework.Commands;
using Xunit.Abstractions;

namespace TestFramework.Commands
{
    public class FuncInitCommand : FuncCommand
    {
        private string _funcPath;
        public FuncInitCommand(string funcPath, ITestOutputHelper log) : base(log)
        {
            _funcPath = funcPath;

        }

        protected override CommandInfo CreateCommand(IEnumerable<string> args)
        {
            var arguments = new List<string> { "init" }.Concat(args).ToList();

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


