// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System;
using System.IO;
using Xunit.Abstractions;

namespace Azure.Functions.Cli.TestFramework.Commands
{
    /// <summary>
    /// Command for executing 'func --version' and version-related operations
    /// </summary>
    public class FuncVersionCommand : FuncCommand
    {
        public FuncVersionCommand(string funcExePath, string testName, ITestOutputHelper log)
            : base(log)
        {
            FuncExePath = funcExePath;
            TestName = testName;
        }

        public string FuncExePath { get; }
        
        public string TestName { get; }

        protected override CommandInfo CreateCommandInfo(System.Collections.Generic.IEnumerable<string> args)
        {
            var commandInfo = new CommandInfo
            {
                FileName = FuncExePath,
                WorkingDirectory = Path.GetDirectoryName(FuncExePath),
                Arguments = args
            };

            return commandInfo;
        }
    }
}