using System;
using System.Threading.Tasks;
using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Interfaces;
using Fclp;

namespace Azure.Functions.Cli.Actions.DurableActions
{
    [Action(Name = "purge-history", Context = Context.Durable, HelpText = "Purge orchestration instance state, history, and blob storage for orchestrations older than the specified threshold time.")]
    class DurablePurgeHistory : BaseAction
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
                .WithDescription("(Optional) Delete the history of instances created after this date/time (UTC). Format: mm/dd/yyyy HH:mm")
                .SetDefault(DateTime.MinValue)
                .Callback(n => CreatedTimeFrom = n);
            Parser
                .Setup<DateTime>("created-before")
                .WithDescription("(Optional) Delete the history of instances created before this date/time (UTC). Format: mm/dd/yyyy HH:mm")
                .SetDefault(DateTime.MaxValue.AddDays(-1)) // subtract one to avoid overflow/timezone error
                .Callback(n => CreatedTimeTo = n);
            Parser
                .Setup<string>("runtime-status")
                // TODO --- this description is probably too long...
                // Also, confirm that the runtime status list is confined to those four statuses (https://github.com/gled4er/durabletask/blob/dee6f2194fe36cb742402fa215d2c472f6b124e8/src/DurableTask.AzureStorage/Tracking/AzureTableTrackingStore.cs#L711)
                .WithDescription("(Optional) Delete the history of instances whose status match these. Options: 'completed', 'terminated', 'canceled', 'failed'. Can provide multiple (space separated) statuses. Default: delete instance history regardless of status")
                .SetDefault(string.Empty)
                .Callback(n => Statuses = n);

            return base.ParseArgs(args);
        }

        public override async Task RunAsync()
        {
            await _durableManager.PurgeHistory(CreatedTimeFrom, CreatedTimeTo, DurableManager.ParseStatuses(Statuses));
        }


    }
}
