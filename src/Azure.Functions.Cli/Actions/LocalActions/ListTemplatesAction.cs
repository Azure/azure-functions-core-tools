using System;
using System.Linq;
using System.Threading.Tasks;
using Azure.Functions.Cli.Interfaces;
using Colors.Net;
using Fclp;
using static Azure.Functions.Cli.Common.OutputTheme;

namespace Azure.Functions.Cli.Actions.LocalActions
{
    [Action(Name = "list", Context = Context.Templates, HelpText = "List available templates.")]
    internal class ListTemplatesAction : BaseAction
    {
        private readonly ITemplatesManager _templatesManager;

        public string Language { get; set; }

        public ListTemplatesAction(ITemplatesManager templatesManager)
        {
            _templatesManager = templatesManager;
        }

        public override ICommandLineParserResult ParseArgs(string[] args)
        {
            Parser
                .Setup<string>('l', "language")
                .WithDescription("Language to list templates for. Default is all languages.")
                .SetDefault(string.Empty)
                .Callback(l => Language = l);

            return base.ParseArgs(args);
        }

        public override async Task RunAsync()
        {
            var templates = await _templatesManager.Templates;
            templates = string.IsNullOrWhiteSpace(Language)
                ? templates
                : templates.Where(t => t.Metadata.Language.Equals(Language, StringComparison.OrdinalIgnoreCase));

            foreach (var languageGrouping in templates.GroupBy(t => t.Metadata.Language))
            {
                ColoredConsole.WriteLine(TitleColor($"{languageGrouping.Key} Templates:"));
                foreach (var template in languageGrouping)
                {
                    ColoredConsole.WriteLine($"  {template.Metadata.Name}");
                }
                ColoredConsole.WriteLine();
            }
        }
    }
}
