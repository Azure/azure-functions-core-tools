// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Common;
using Fclp;
using Fclp.Internals;

namespace Azure.Functions.Cli.Interfaces
{
    internal interface IAction
    {
        internal IEnumerable<ICommandLineOption> MatchedOptions { get; }

        internal IDictionary<string, string> TelemetryCommandEvents { get; }

        internal ICommandLineParserResult ParseArgs(string[] args);

        internal Task RunAsync();

        internal IEnumerable<CliArgument> GetPositionalArguments();
    }
}
