using System.Threading.Tasks;
using Fclp;

namespace Azure.Functions.Cli.Interfaces
{
    internal interface IAction
    {
        ICommandLineParserResult ParseArgs(string[] args);
        Task RunAsync();
    }
}
