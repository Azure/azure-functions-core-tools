using System.Collections.Generic;
using System.Threading.Tasks;
using Azure.Functions.Cli.Common;

namespace Azure.Functions.Cli.Interfaces
{
    interface ITemplatesManager
    {
        Task<IEnumerable<Template>> Templates { get; }

        Task Deploy(string Name, Template template);
    }
}
