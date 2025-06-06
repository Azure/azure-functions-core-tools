﻿// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Interfaces;
using Fclp;

namespace Azure.Functions.Cli.Actions.DurableActions
{
    [Action(Name = "get-runtime-status", Context = Context.Durable, HelpText = "Retrieve the status of the specified orchestration instance")]
    internal class DurableGetRuntimeStatus : BaseDurableActionWithId
    {
        private readonly IDurableManager _durableManager;

        public DurableGetRuntimeStatus(IDurableManager durableManager)
        {
            _durableManager = durableManager;
        }

        private bool ShowInput { get; set; }

        private bool ShowOutput { get; set; }

        public override ICommandLineParserResult ParseArgs(string[] args)
        {
            Parser
                .Setup<bool>("show-input")
                .WithDescription("If set to true, the response will contain the input of the function.")
                .SetDefault(false)
                .Callback(n => ShowInput = n);
            Parser
                .Setup<bool>("show-output")
                .WithDescription("If set to true, the response will contain the execution history.")
                .SetDefault(false)
                .Callback(n => ShowOutput = n);

            return base.ParseArgs(args);
        }

        public override async Task RunAsync()
        {
            await _durableManager.GetRuntimeStatus(ConnectionString, TaskHubName, Id, ShowInput, ShowOutput);
        }
    }
}
