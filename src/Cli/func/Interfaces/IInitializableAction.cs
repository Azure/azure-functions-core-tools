using System.Threading.Tasks;

namespace Azure.Functions.Cli.Interfaces
{
    internal interface IInitializableAction : IAction
    {
        Task Initialize();
    }
}
