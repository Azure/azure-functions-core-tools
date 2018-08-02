using System;
using System.Collections.Generic;
using System.Text;
using Fclp;

namespace Azure.Functions.Cli.Actions.DurableActions
{
    abstract class BaseDurableAction : BaseAction
    {
        public string Instance { get; set; }

        protected BaseDurableAction() { }

        public override ICommandLineParserResult ParseArgs(string[] args)
        {
            Parser
                .Setup<string>("instance")
                .WithDescription("This specifies the id of an orchestration")
                .SetDefault(null)
                .Callback(i => Instance = i);

            return base.ParseArgs(args);
        }
    }
}
