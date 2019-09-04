using System.Threading.Tasks;
using Fclp;
using Azure.Functions.Cli.Interfaces;
using System;
using System.Collections.Generic;
using Fclp.Internals;
using System.Linq;
using Azure.Functions.Cli.Telemetry;
using Azure.Functions.Cli.Helpers;

namespace Azure.Functions.Cli.Actions
{
    abstract class BaseAction : IAction
    {
        protected FluentCommandLineParser Parser { get; private set; }

        public IEnumerable<ICommandLineOption> MatchedOptions { get; private set; }

        public BaseAction()
        {
            Parser = new FluentCommandLineParser();
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

        public virtual void UpdateTelemetryEvent(TelemetryEvent telemetryEvent)
        {
            var languageContext = GlobalCoreToolsSettings.CurrentLanguageOrNull ?? "N/A";
            telemetryEvent.GlobalSettings["language"] = languageContext;
        }

        public abstract Task RunAsync();
    }
}
