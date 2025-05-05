// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Fclp;

namespace Azure.Functions.Cli.Actions.DurableActions
{
    internal abstract class BaseDurableAction : BaseAction
    {
        protected BaseDurableAction()
        {
        }

        protected string ConnectionString { get; set; }

        protected string TaskHubName { get; set; }

        public override ICommandLineParserResult ParseArgs(string[] args)
        {
            Parser
                .Setup<string>("connection-string-setting")
                .WithDescription("(Optional) Name of the setting containing the storage connection string to use.")
                .SetDefault(null)
                .Callback(n => ConnectionString = n);
            Parser
                .Setup<string>("task-hub-name")
                .WithDescription("(Optional) Name of the Durable Task Hub to use.")
                .SetDefault(null)
                .Callback(n => TaskHubName = n);

            return base.ParseArgs(args);
        }
    }
}
