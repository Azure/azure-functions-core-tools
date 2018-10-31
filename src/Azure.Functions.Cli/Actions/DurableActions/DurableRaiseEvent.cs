using System;
using System.Threading.Tasks;
using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Interfaces;
using Fclp;

namespace Azure.Functions.Cli.Actions.DurableActions
{
    [Action(Name = "raise-event", Context = Context.Durable, HelpText = "Raise an event to the specified orchestration instance")]
    class DurableRaiseEvent : BaseDurableAction
    {
        private string EventName { get; set; }

        private string EventData { get; set; }

        private readonly IDurableManager _durableManager;

        public DurableRaiseEvent(IDurableManager durableManager)
        {
            _durableManager = durableManager;
        }

        public override ICommandLineParserResult ParseArgs(string[] args)
        {
            Parser
                 .Setup<string>("event-name")
                 .WithDescription("Name of the event to raise")
                 .SetDefault($"Event_{(Guid.NewGuid().ToString("N"))}")
                 .Callback(n => EventName = n);
            Parser
               .Setup<string>("event-data")
               .WithDescription("Data to pass to the event")
               .SetDefault(null)
               .Callback(d => EventData = d);

            return base.ParseArgs(args);
        }

        public override async Task RunAsync()
        {
            if (string.IsNullOrEmpty(ID))
            {
                throw new CliArgumentsException("Must specify the id of the orchestration instance you wish to raise an event for.",
                    new CliArgument { Name = "id", Description = "ID of the orchestration instance to raise an event for." });
            }

            await _durableManager.RaiseEvent(ID, EventName, EventData);
        }
    }
}