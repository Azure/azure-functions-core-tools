// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using Xunit.Abstractions;

namespace Func.TestFramework.Commands
{
    public class FuncStartCommand : FuncCommand
    {
        private readonly string CommandName = "start";
        private string _funcPath;
        private string _testName;

        public FuncStartCommand(string funcPath, string testName, ITestOutputHelper log) : base(log)
        {
            _funcPath = funcPath;
            _testName = testName;
        }

        protected override CommandInfo CreateCommand(IEnumerable<string> args)
        {
            var arguments = new List<string> { CommandName }.Concat(args).ToList();

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
