// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Interfaces;
using Fclp;

namespace Azure.Functions.Cli.Actions.DurableActions
{
    [Action(Name = "raise-event", Context = Context.Durable, HelpText = "Raise an event to the specified orchestration instance")]
    internal class DurableRaiseEvent : BaseDurableActionWithId
    {
        private readonly IDurableManager _durableManager;

        public DurableRaiseEvent(IDurableManager durableManager)
        {
            _durableManager = durableManager;
        }

        private string EventName { get; set; }

        private string EventData { get; set; }

        public override ICommandLineParserResult ParseArgs(string[] args)
        {
            Parser
                 .Setup<string>("event-name")
                 .WithDescription("Name of the event to raise")
                 .SetDefault($"Event_{Guid.NewGuid():N}")
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
