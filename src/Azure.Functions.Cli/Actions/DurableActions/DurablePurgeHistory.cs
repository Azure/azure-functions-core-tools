using System;
using System.Threading.Tasks;
using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Interfaces;
using Fclp;

namespace Azure.Functions.Cli.Actions.DurableActions
{
    [Action(Name = "purge-history", Context = Context.Durable, HelpText = "Purge orchestration instance state, history, and blob storage for orchestrations older than the specified threshold time.")]
    class DurablePurgeHistory : BaseDurableAction
    {

        private readonly IDurableManager _durableManager;

        private DateTime CreatedTimeFrom { get; set; }

        private DateTime CreatedTimeTo { get; set; }

        private string Statuses { get; set; }

        public DurablePurgeHistory(IDurableManager durableManager)
        {
            _durableManager = durableManager;
        }

        public override ICommandLineParserResult ParseArgs(string[] args)
        {
            Parser
                .Setup<DateTime>("created-after")
                .WithDescription("(Optional) Delete the history of instances created after this date/time (UTC). All ISO 8601 formatted datetimes accepted.")
                .SetDefault(DurableManager.CreatedAfterDefault)
                .Callback(n => CreatedTimeFrom = n);
            Parser
                .Setup<DateTime>("created-before")
                .WithDescription("(Optional) Delete the history of instances created before this date/time (UTC). All ISO 8601 formatted datetimes accepted.")
                .SetDefault(DurableManager.CreatedBeforeDefault)
                .Callback(n => CreatedTimeTo = n);
            Parser
                .Setup<string>("runtime-status")
                .WithDescription("(Optional) Delete the history of instances whose status match these. Options: 'completed', 'terminated', 'canceled', 'failed'. Can provide multiple (space separated) statuses. Default: delete instance history regardless of status")
                .SetDefault(string.Empty)
                .Callback(n => Statuses = n);

            return base.ParseArgs(args);
        }

        public override async Task RunAsync()
        {
            await _durableManager.PurgeHistory(ConnectionString, TaskHubName, CreatedTimeFrom, CreatedTimeTo, DurableManager.ParseStatuses(Statuses));
        }


    }
}
