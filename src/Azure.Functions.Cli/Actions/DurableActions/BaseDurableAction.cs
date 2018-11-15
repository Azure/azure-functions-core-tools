using Fclp;

namespace Azure.Functions.Cli.Actions.DurableActions
{
    abstract class BaseDurableAction : BaseAction
    {
        protected string ConnectionString;

        protected string TaskHubName;

        protected BaseDurableAction() { }

        public override ICommandLineParserResult ParseArgs(string[] args)
        {
            Parser
                .Setup<string>("connection-string-setting")
                .WithDescription("(Optional) Name of the setting containing the storage connection string to use.")
                .SetDefault(null)
                .Callback(n => ConnectionString = n);
            Parser
                .Setup<string>("task-hub-name")
                .WithDescription("(Optional) Name of the Durable Task Hub to use.")
                .SetDefault(null)
                .Callback(n => TaskHubName = n);

            return base.ParseArgs(args);
        }
    }
}