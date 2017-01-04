using System.Threading.Tasks;
using Fclp;
using Azure.Functions.Cli.Interfaces;

namespace Azure.Functions.Cli.Actions
{
    abstract class BaseAction : IAction
    {
        protected FluentCommandLineParser Parser { get; private set; }
        public BaseAction()
        {
            Parser = new FluentCommandLineParser();
        }

        public virtual ICommandLineParserResult ParseArgs(string[] args)
        {
            return Parser.Parse(args);
        }

        public abstract Task RunAsync();
    }
}
