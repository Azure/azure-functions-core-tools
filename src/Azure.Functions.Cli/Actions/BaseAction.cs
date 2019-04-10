using System.Threading.Tasks;
using Fclp;
using Azure.Functions.Cli.Interfaces;
using System;

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
