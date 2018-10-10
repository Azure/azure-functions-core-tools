using System.Linq;
using Azure.Functions.Cli.Actions.AzureActions;
using Fclp;

namespace Azure.Functions.Cli.Actions.AuthActions
{
    abstract class BaseAuthAction : BaseAzureAction
    {
        protected BaseAuthAction() { }
    }
}