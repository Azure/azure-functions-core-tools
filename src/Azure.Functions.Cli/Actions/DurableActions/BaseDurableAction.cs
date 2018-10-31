using Fclp;

namespace Azure.Functions.Cli.Actions.DurableActions
{
    abstract class BaseDurableAction : BaseAction
    {
        protected string Id { get; set; }

        protected BaseDurableAction() { }

        public override ICommandLineParserResult ParseArgs(string[] args)
        {
            Parser
                .Setup<string>("id")
                .WithDescription("Specifies the id of an orchestration function")
                .SetDefault(null)
                .Callback(i => Id = i);

            return base.ParseArgs(args);
        }
    }
}