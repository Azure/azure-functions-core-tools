using Fclp;

namespace Azure.Functions.Cli.Actions.DurableActions
{
    abstract class BaseDurableAction : BaseAction
    {
        protected string ID { get; set; }

        protected BaseDurableAction() { }

        public override ICommandLineParserResult ParseArgs(string[] args)
        {
            Parser
                .Setup<string>("id")
                .WithDescription("Specifies the id of an orchestration function")
                .SetDefault(null)
                .Callback(i => ID = i);

            return base.ParseArgs(args);
        }
    }
}