using Xunit.Abstractions;

namespace Func.TestFramework.Commands
{
    public class FuncInitCommand : FuncCommand
    {
        private string _funcPath;
        private string _testName;
        public FuncInitCommand(string funcPath, string testName, ITestOutputHelper log) : base(log)
        {
            _funcPath = funcPath;
            _testName = testName;

        }

        protected override CommandInfo CreateCommand(IEnumerable<string> args)
        {
            var arguments = new List<string> { "init" }.Concat(args).ToList();

            var commandInfo = new CommandInfo()
            {
                FileName = _funcPath,
                Arguments = arguments,
                WorkingDirectory = WorkingDirectory,
                TestName = _testName,
            };
            return commandInfo;
        }
    }
}
