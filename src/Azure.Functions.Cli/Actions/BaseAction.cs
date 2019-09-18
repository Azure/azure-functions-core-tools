using System.Threading.Tasks;
using Fclp;
using Azure.Functions.Cli.Interfaces;
using System;
using System.Collections.Generic;
using Fclp.Internals;
using System.Linq;
using Azure.Functions.Cli.Telemetry;
using Azure.Functions.Cli.Helpers;
using Azure.Functions.Cli.Common;
using Colors.Net;

namespace Azure.Functions.Cli.Actions
{
    abstract class BaseAction : IAction
    {
        protected FluentCommandLineParser Parser { get; private set; }

        public IEnumerable<ICommandLineOption> MatchedOptions { get; private set; }

        public IDictionary<string, string> TelemetryCommandEvents { get; private set; }

        public BaseAction()
        {
            Parser = new FluentCommandLineParser();
            TelemetryCommandEvents = new Dictionary<string, string>();
        }

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
