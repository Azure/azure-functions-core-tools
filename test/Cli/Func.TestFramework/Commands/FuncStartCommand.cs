
using Xunit.Abstractions;

namespace Func.TestFramework.Commands
{
    public class FuncStartCommand : FuncCommand
    {
        private string _funcPath;
        private string _testName;

        public FuncStartCommand(string funcPath, ITestOutputHelper log, string testName = "") : base(log)
        {
            _funcPath = funcPath;
            _testName = testName;

        }

        protected override CommandInfo CreateCommand(IEnumerable<string> args)
        {
            var arguments = new List<string> { "start" }.Concat(args).ToList();

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
