// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.ExtensionBundle;
using Colors.Net;
using Fclp;

namespace Azure.Functions.Cli.Actions.LocalActions
{
    // Hidden backward compatibility action
    [Action(Name = "GetExtensionBundlePath", ShowInHelp = false)]
    internal class GetExtensionBundlePath : BaseAction
    {
        public override ICommandLineParserResult ParseArgs(string[] args)
        {
            return base.ParseArgs(args);
        }

        public override async Task RunAsync()
        {
            // Delegate to the main implementation
            var action = new GetBundlePathAction();
            await action.RunAsync();
        }
    }
}
