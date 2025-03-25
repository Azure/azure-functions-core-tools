using Fclp;

namespace Azure.Functions.Cli.Actions.DurableActions
{
    internal abstract class BaseDurableActionWithId : BaseDurableAction
    {
        protected string Id { get; set; }

        protected BaseDurableActionWithId() { }

        public override ICommandLineParserResult ParseArgs(string[] args)
        {
            Parser
                .Setup<string>("id")
                .WithDescription("Specifies the id of an orchestration instance")
                .Required()
                .Callback(i => Id = i);

            return base.ParseArgs(args);
        }
    }
}