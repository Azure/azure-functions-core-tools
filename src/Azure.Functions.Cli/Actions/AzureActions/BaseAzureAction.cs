using System.Threading.Tasks;
using Azure.Functions.Cli.Arm;
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
        { }
    }
}
