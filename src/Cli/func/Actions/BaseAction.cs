// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Interfaces;
using Fclp;
using Fclp.Internals;

namespace Azure.Functions.Cli.Actions
{
    internal abstract class BaseAction : IAction
    {
        public BaseAction()
        {
            Parser = new FluentCommandLineParser();
            TelemetryCommandEvents = new Dictionary<string, string>();
        }

        protected FluentCommandLineParser Parser { get; private set; }

        public IEnumerable<ICommandLineOption> MatchedOptions { get; private set; }

        public IDictionary<string, string> TelemetryCommandEvents { get; private set; }

        public virtual ICommandLineParserResult ParseArgs(string[] args)
        {
            var parserResult = Parser.Parse(args);
            MatchedOptions = Parser.Options.Except(parserResult.UnMatchedOptions);
            return parserResult;
        }

        public void SetFlag<T>(string longOption, string description, Action<T> callback, bool isRequired = false)
        {
            var flag = Parser.Setup<T>(longOption).WithDescription(description).Callback(callback);
            if (isRequired)
            {
                flag.Required();
            }
        }

        public abstract Task RunAsync();
    }
}
