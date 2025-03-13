using System;
using System.Threading.Tasks;
using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.ExtensionBundle;
using Colors.Net;
using Fclp;

namespace Azure.Functions.Cli.Actions.LocalActions
{
    [Action(Name = "GetExtensionBundlePath", ShowInHelp = false)]
    internal class GetExtensionBundlePath : BaseAction
    {
        public string Language { get; set; }

        public override ICommandLineParserResult ParseArgs(string[] args)
        {
            return base.ParseArgs(args);
        }

        public override async Task RunAsync()
        {
            var extensionBundleManager = ExtensionBundleHelper.GetExtensionBundleManager();
            if (extensionBundleManager.IsExtensionBundleConfigured())
            {
                try
                {
                    string bundlePath = await extensionBundleManager.GetExtensionBundlePath();
                    if (string.IsNullOrEmpty(bundlePath))
                    {
                        throw new CliException("Unable to locate extension bundle.");
                    }
                    else
                    {
                        ColoredConsole.WriteLine(bundlePath);
                    }
                }
                catch (Exception e)
                {
                    throw new CliException("Unable to locate extension bundle.", e);
                }


            }
            else
            {
                ColoredConsole.WriteLine("Extension bundle not configured.");
            }
        }
    }
}
