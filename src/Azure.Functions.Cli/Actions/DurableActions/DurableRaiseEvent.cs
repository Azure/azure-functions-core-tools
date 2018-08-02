using System;
using System.Threading.Tasks;
using Fclp;
using Azure.Functions.Cli.Interfaces;
using Microsoft.Azure.WebJobs;
using DurableTask.Core;
using Colors.Net;
using static Azure.Functions.Cli.Common.OutputTheme;
using static Colors.Net.StringStaticMethods;
using Azure.Functions.Cli.Common;


namespace Azure.Functions.Cli.Actions.DurableActions
{
    [Action(Name = "raise-event", Context = Context.Durable, HelpText = "Raises an event to a specified orchestration instance")]
    class DurableRaiseEvent : BaseDurableAction
    {
        public string EventName { get; set; }

        public object EventData { get; set; }

        private readonly DurableManager _durableManager;

        public DurableRaiseEvent(DurableManager durableManager)
        {
            _durableManager = durableManager;
        }

        public override ICommandLineParserResult ParseArgs(string[] args)
        {
            Parser
                 .Setup<string>("event-name")
                 .WithDescription("This specifies the name of an event to raise")
                 .SetDefault($"Event_{(Guid.NewGuid().ToString("N"))}")
                 .Callback(n => EventName = n);

            Parser
                .Setup<string>("event-data")
                .WithDescription("This is the data to be passed wtih an event")
                .SetDefault(null)
                .Callback(d => EventData = d);

            return base.ParseArgs(args);
        }

        public override async Task RunAsync()
        {
            await _durableManager.RaiseEvent(Instance, EventName, EventData);
        }
    }
}