using System;
using System.Threading.Tasks;
using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Interfaces;
using Fclp;


namespace Azure.Functions.Cli.Actions.DurableActions
{
    [Action(Name = "get-instances", Context = Context.Durable, HelpText = "Retrieve the status of all orchestration instances. Supports paging via 'top' parameter.")]
    class DurableGetInstances : BaseDurableAction
    {
        private readonly IDurableManager _durableManager;

        private DateTime CreatedTimeFrom { get; set; }

        private DateTime CreatedTimeTo { get; set; }

        private string Statuses { get; set; }

        private int Top { get; set; }

        private string ContinuationToken { get; set; }

        public DurableGetInstances(IDurableManager durableManager)
        {
            _durableManager = durableManager;
        }

        public override ICommandLineParserResult ParseArgs(string[] args)
        {
            Parser
                .Setup<DateTime>("created-after")
                .WithDescription("(Optional) Retrieve the instances created after this date/time (UTC). All ISO 8601 formatted datetimes accepted.")
                .SetDefault(DurableManager.CreatedAfterDefault)
                .Callback(n => CreatedTimeFrom = n);
            Parser
                .Setup<DateTime>("created-before")
                .WithDescription("(Optional) Retrieve the instances created before this date/time (UTC). All ISO 8601 formatted datetimes accepted.")
                .SetDefault(DurableManager.CreatedBeforeDefault)
                .Callback(n => CreatedTimeTo = n);
            Parser
                .Setup<string>("runtime-status")
                .WithDescription("(Optional) Retrieve the instances whose status match these ('running', 'completed', etc.). Can provide multiple (space separated) statuses.")
                .SetDefault(string.Empty)
                .Callback(n => Statuses = n);
            Parser
                .Setup<int>("top")
                .WithDescription("(Optional) Number of records retrieved per request.")
                .SetDefault(10)
                .Callback(n => Top = n);
            Parser
                .Setup<string>("continuation-token")
                .WithDescription("(Optional) A token to indicate which page/section of the requests to retrieve.")
                .SetDefault(string.Empty)
                .Callback(n => ContinuationToken = n);

            return base.ParseArgs(args);
        }

        public override async Task RunAsync()
        {
            await _durableManager.GetInstances(ConnectionString, TaskHubName, CreatedTimeFrom, 
                CreatedTimeTo, DurableManager.ParseStatuses(Statuses), Top, ContinuationToken);
        }
    }
}
