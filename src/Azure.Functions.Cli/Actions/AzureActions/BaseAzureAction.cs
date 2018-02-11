using Azure.Functions.Cli.Arm;

namespace Azure.Functions.Cli.Actions.AzureActions
{
    abstract class BaseAzureAction : BaseAction
    {
        protected readonly IArmManager _armManager;

        protected BaseAzureAction(IArmManager armManager)
        {
            _armManager = armManager;
        }
    }
}
