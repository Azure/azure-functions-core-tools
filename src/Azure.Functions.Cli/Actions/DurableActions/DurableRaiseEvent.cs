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
    class DurableRaiseEvent : BaseAction
    {
        public string Version { get; set; }

        public string Instance { get; set; }

        public string EventName { get; set; }

        public object EventData { get; set; }


        private readonly ISecretsManager _secretsManager;
        private readonly DurableManager durableManager;

        public DurableRaiseEvent(ISecretsManager secretsManager)
        {
            _secretsManager = secretsManager;
            durableManager = new DurableManager(secretsManager);
        }


        public override ICommandLineParserResult ParseArgs(string[] args)
        {
            var parser = new FluentCommandLineParser();
            parser
                .Setup<string>("version")
                .WithDescription("This shows up in the help next to the version option")
                .SetDefault(string.Empty)
                .Callback(v => Version = v);

            parser
                .Setup<string>("instance")
                .WithDescription("This specifies the id of an orchestration")
                .SetDefault(null)
                .Callback(i => Instance = i);

            parser
                 .Setup<string>("event-name")
                 .WithDescription("This specifies the name of an event to raise")
                 .SetDefault($"Event_{(Guid.NewGuid().ToString("N"))}")
                 .Callback(n => EventName = n);

            parser
                .Setup<string>("event-data")
                .WithDescription("This is the data to be passed wtih an event")
                .SetDefault(null)
                .Callback(d => EventData = d);

            return parser.Parse(args);
        }

        public override async Task RunAsync()
        {
            await durableManager.RaiseEvent(Instance, EventName, EventData);
        }
    }
}