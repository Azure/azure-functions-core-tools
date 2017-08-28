﻿using System;
using System.Linq;
using System.Threading.Tasks;
using Colors.Net;
using Fclp;
using Azure.Functions.Cli.Arm;
using Azure.Functions.Cli.Interfaces;
using Azure.Functions.Cli.Common;

namespace Azure.Functions.Cli.Actions.AzureActions
{
    [Action(Name = "set", Context = Context.Azure, SubContext = Context.Account, HelpText = "Set the active subscription")]
    [Action(Name = "set", Context = Context.Azure, SubContext = Context.Subscriptions, HelpText = "Set the active subscription")]
    class SetAzureAccountAction : BaseAzureAccountAction
    {
        private string Subscription { get; set; }

        public SetAzureAccountAction(IArmManager armManager, ISettings settings)
            : base(armManager, settings)
        {
        }

        public override ICommandLineParserResult ParseArgs(string[] args)
        {
            if (args.Any())
            {
                Subscription = args.First();
            }
            else
            {
                throw new CliArgumentsException("Must specify subscription id.",
                    new CliArgument { Name = nameof(Subscription), Description = "Subscription Id" });
            }

            return base.ParseArgs(args);
        }

        public override async Task RunAsync()
        {
            var tenants = await _armManager.GetTenants();
            var validSub = tenants
                .Select(t => t.Subscriptions)
                .SelectMany(s => s)
                .Any(s => s.SubscriptionId.Equals(Subscription, StringComparison.OrdinalIgnoreCase));
            var tenant = tenants
                .FirstOrDefault(t => t.Subscriptions.Any(s => s.SubscriptionId.Equals(Subscription, StringComparison.OrdinalIgnoreCase)));

            if (validSub && tenant != null)
            {
                Settings.CurrentSubscription = Subscription;
                Settings.CurrentTenant = tenant.TenantId;
            }
            else
            {
                ColoredConsole.Error.WriteLine($"Unable to find {Subscription}");
            }
            await PrintAccountsAsync();
        }
    }
}
