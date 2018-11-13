using Fclp;

namespace Azure.Functions.Cli.Actions.DurableActions
{
    abstract class BaseDurableAction : BaseAction
    {
        protected string ConnectionString;

        protected BaseDurableAction() { }

        public override ICommandLineParserResult ParseArgs(string[] args)
        {
            Parser
                .Setup<string>("connection-string")
                .WithDescription("(Optional) Storage connection string to use.")
                .SetDefault(null)
                .Callback(n => ConnectionString = n);

            return base.ParseArgs(args);
        }
    }
}