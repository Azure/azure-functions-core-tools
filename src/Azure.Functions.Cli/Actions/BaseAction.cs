using System.Threading.Tasks;
using Fclp;
using Azure.Functions.Cli.Interfaces;
using Fclp.Internals;
using System.Collections.Generic;
using System.Linq;

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

        public abstract Task RunAsync();
    }
}
