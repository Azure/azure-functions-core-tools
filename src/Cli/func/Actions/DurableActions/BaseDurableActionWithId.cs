// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Fclp;

namespace Azure.Functions.Cli.Actions.DurableActions
{
    internal abstract class BaseDurableActionWithId : BaseDurableAction
    {
        protected BaseDurableActionWithId()
        {
        }

        protected string Id { get; set; }

        public override ICommandLineParserResult ParseArgs(string[] args)
        {
            Parser
                .Setup<string>("id")
                .WithDescription("Specifies the id of an orchestration instance")
                .Required()
                .Callback(i => Id = i);

            return base.ParseArgs(args);
        }
    }
}
