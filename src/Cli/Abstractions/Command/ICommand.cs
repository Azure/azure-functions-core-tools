// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

// Copied from: https://github.com/dotnet/sdk/blob/4a81a96a9f1bd661592975c8269e078f6e3f18c9/src/Cli/Microsoft.DotNet.Cli.Utils/ICommand.cs
namespace Azure.Functions.Cli.Abstractions
{
    public interface ICommand
    {
        public string CommandName { get; }

        public string CommandArgs { get; }

        public CommandResult Execute();

        public ICommand WorkingDirectory(string projectDirectory);

        public ICommand EnvironmentVariable(string name, string? value);

        public ICommand CaptureStdOut();

        public ICommand CaptureStdErr();

        public ICommand ForwardStdOut(TextWriter? to = null, bool onlyIfVerbose = false, bool ansiPassThrough = true);

        public ICommand ForwardStdErr(TextWriter? to = null, bool onlyIfVerbose = false, bool ansiPassThrough = true);

        public ICommand OnOutputLine(Action<string> handler);

        public ICommand OnErrorLine(Action<string> handler);

        public ICommand SetCommandArgs(string commandArgs);
    }
}
