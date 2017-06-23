using System.Threading.Tasks;
using Azure.Functions.Cli.Arm;
using Azure.Functions.Cli.Interfaces;

namespace Azure.Functions.Cli.Actions.AzureActions
{
    abstract class BaseAzureAction : BaseAction, IInitializableAction
    {
        protected readonly IArmManager _armManager;
        private readonly bool _requiresLogin;

        protected BaseAzureAction(
            IArmManager armManager,
            bool requiresLogin = true)
        {
            _armManager = armManager;
            _requiresLogin = requiresLogin;
        }

        public async Task Initialize()
        {
            if(_requiresLogin)
                await _armManager.Initialize();
        }
    }
}
