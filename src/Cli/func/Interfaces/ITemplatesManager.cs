using System.Collections.Generic;
using System.Threading.Tasks;
using Azure.Functions.Cli.Common;

namespace Azure.Functions.Cli.Interfaces
{
    interface ITemplatesManager
    {
        Task<IEnumerable<Template>> Templates { get; }
        Task<IEnumerable<NewTemplate>> NewTemplates { get; }
        Task<IEnumerable<UserPrompt>> UserPrompts { get; }

        Task Deploy(string name, string fileName, Template template);
        Task Deploy(TemplateJob job, NewTemplate template, IDictionary<string, string> variables);
    }
}