using System;
using System.Threading.Tasks;
using Azure.Functions.Cli.Arm;
using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Interfaces;

namespace Azure.Functions.Cli.Actions.AzureActions
{
    abstract class BaseAzureAction : BaseAction, IInitializableAction
    {
        protected readonly IArmManager _armManager;

        public BaseAzureAction(IArmManager armManager)
        {
            _armManager = armManager;
        }

        public async Task Initialize()
        {
            try
            {
                await _armManager.Initialize();
            }
            catch (Exception e)
            {
                throw new CliException("Error during login to azure. Please verify your internet connection or try again later.", e);
            }
        }
    }
}
