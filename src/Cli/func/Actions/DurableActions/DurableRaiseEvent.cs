using System;
using System.Threading.Tasks;
using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Interfaces;
using Fclp;

namespace Azure.Functions.Cli.Actions.DurableActions
{
    [Action(Name = "raise-event", Context = Context.Durable, HelpText = "Raise an event to the specified orchestration instance")]
    class DurableRaiseEvent : BaseDurableActionWithId
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
               .WithDescription("Data to pass to the event, either in-line or via a JSON file. For files, prefix the path to the file with @ (e.g. \"@path/to/file.json\").")
               .SetDefault(null)
               .Callback(d => EventData = d);

            return base.ParseArgs(args);
        }

        public override async Task RunAsync()
        {
            object input = DurableManager.RetrieveCommandInputData(EventData);
            await _durableManager.RaiseEvent(ConnectionString, TaskHubName, Id, EventName, input);
        }
    }
}