// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Interfaces;

namespace Azure.Functions.Cli.Actions.DurableActions
{
    [Action(Name = "delete-task-hub", Context = Context.Durable, HelpText = "Delete all storage artifacts in the durable task hub")]
    internal class DurableDeleteTaskHub : BaseDurableAction
    {
        private readonly IDurableManager _durableManager;

        public DurableDeleteTaskHub(IDurableManager durableManager)
        {
            _durableManager = durableManager;
        }

        public override async Task RunAsync()
        {
            await _durableManager.DeleteTaskHub(ConnectionString, TaskHubName);
        }
    }
}
