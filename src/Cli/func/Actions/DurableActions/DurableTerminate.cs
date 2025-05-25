// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Interfaces;
using Fclp;

namespace Azure.Functions.Cli.Actions.DurableActions
{
    [Action(Name = "terminate", Context = Context.Durable, HelpText = "Terminate the specified orchestration instance")]
    internal class DurableTerminate : BaseDurableActionWithId
    {
        private readonly IDurableManager _durableManager;

        public DurableTerminate(IDurableManager durableManager)
        {
            _durableManager = durableManager;
        }

        private string Reason { get; set; }

        public override ICommandLineParserResult ParseArgs(string[] args)
        {
            Parser
                 .Setup<string>("reason")
                 .WithDescription("Reason for terminating the orchestration")
                 .Callback(r => Reason = r);

            return base.ParseArgs(args);
        }

        public override async Task RunAsync()
        {
            await _durableManager.Terminate(ConnectionString, TaskHubName, Id, Reason);
        }
    }
}
