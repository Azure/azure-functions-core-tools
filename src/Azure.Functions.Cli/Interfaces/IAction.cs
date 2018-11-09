using System.Collections.Generic;
using System.Threading.Tasks;
using Fclp;
using Fclp.Internals;

namespace Azure.Functions.Cli.Interfaces
{
    internal interface IAction
    {
        IEnumerable<ICommandLineOption> MatchedOptions { get; }
        ICommandLineParserResult ParseArgs(string[] args);
        Task RunAsync();
    }
}
