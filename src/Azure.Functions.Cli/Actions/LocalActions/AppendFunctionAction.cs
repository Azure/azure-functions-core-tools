using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Helpers;
using Azure.Functions.Cli.Interfaces;
using Fclp;
using Microsoft.Azure.WebJobs.Extensions.Http;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using static Azure.Functions.Cli.Common.Constants;

namespace Azure.Functions.Cli.Actions.LocalActions
{
    [Action(Name = "append", Context = Context.Function, HelpText = "Append a new function from a template.")]
    [Action(Name = "append", HelpText = "Append a new function from a template.")]
    internal class AppendFunctionAction : BaseAction
    {
        private ITemplatesManager _templatesManager;
        private readonly ISecretsManager _secretsManager;
        private readonly CreateFunctionAction _createFunctionAction;
        private string Langauge { get; set; }

        public AppendFunctionAction(ITemplatesManager templatesManager, ISecretsManager secretsManager)
        {
            _templatesManager = templatesManager;
            _secretsManager = secretsManager;
            _createFunctionAction = new CreateFunctionAction(_templatesManager, _secretsManager);
        }

        public override ICommandLineParserResult ParseArgs(string[] args)
        {
            _createFunctionAction.ParseArgs(args);
            return base.ParseArgs(args);
        }

        public async override Task RunAsync()
        {
            if (_createFunctionAction.ValidateInputs())
            {
                return;
            }

            await _createFunctionAction.UpdateLanguageAndRuntime();
            Langauge = _createFunctionAction.Language;
            if (!IsNewPythonProgrammingModel())
            {
                if (string.Equals(Langauge, Languages.Python, StringComparison.InvariantCultureIgnoreCase))
                {
                    // todo: Message needs to be changed when we do the switch. Create a ticket to track. 
                    throw new CliException($"The 'func append' is not supported by Python V1 model.");
                }

                throw new CliException($"The 'func append' is not supported by {Langauge}. Please use `func new` command.");
            }
            
            await _createFunctionAction.RunAsync();
        }

        private bool IsNewPythonProgrammingModel()
        {
            return PythonHelpers.IsNewPythonProgrammingModel(_createFunctionAction.Language);
        }
    }
}
