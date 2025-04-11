// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using Xunit.Abstractions;

namespace Func.TestFramework.Commands
{
    public class FuncSettingsCommand : FuncCommand
    {
        private string _funcPath;

        public FuncSettingsCommand(string funcPath, ITestOutputHelper log) : base(log)
        {
            _funcPath = funcPath;

        }

        protected override CommandInfo CreateCommand(IEnumerable<string> args)
        {
            var arguments = new List<string> { "settings" }.Concat(args).ToList();

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
