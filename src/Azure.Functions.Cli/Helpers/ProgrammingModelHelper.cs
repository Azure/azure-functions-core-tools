using System;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using Azure.Functions.Cli.Common;
using Newtonsoft.Json.Linq;

namespace Azure.Functions.Cli.Helpers
{
    public static class ProgrammingModelHelper
    {
        public static IEnumerable<ProgrammingModel> GetProgrammingModels()
        {
            return Enum.GetValues<ProgrammingModel>();
        }

        // Given a worker runtime, this function returns a collection of the programming models that are supported.
        public static IEnumerable<ProgrammingModel> GetSupportedProgrammingModels(string language)
        {
            var allProgrammingModels = GetProgrammingModels();
            if (!string.Equals(language, Constants.Languages.Python))
            {
                return allProgrammingModels.Where(pm => pm != ProgrammingModel.Preview);
            }
            return allProgrammingModels;
        }

        public static ProgrammingModel ResolveProgrammingModel(string programmingModel, string language)
        {
            if (GlobalCoreToolsSettings.CurrentProgrammingModel != null)
            {
                return GlobalCoreToolsSettings.CurrentProgrammingModel.Value;
            }
            // We default to the "Default" programming model if the model parameter is not specified
            if (string.IsNullOrEmpty(programmingModel))
            {
                GlobalCoreToolsSettings.CurrentProgrammingModel = ProgrammingModel.Default;
            }
            else if (GetProgrammingModels().Any(pm => string.Equals(programmingModel, pm.ToString(), StringComparison.InvariantCultureIgnoreCase)))
            {
                GlobalCoreToolsSettings.CurrentProgrammingModel = GetProgrammingModels().First(pm => string.Equals(programmingModel, pm.ToString(), StringComparison.InvariantCultureIgnoreCase));
            }
            // If programmingModel is non-empty and does not match any progrmming model, then we raise an exception
            else
            {
                // TODO: Explicitly define the association between language, worker-runtime, and programming model
                throw new CliArgumentsException($"The programming model {programmingModel} is not supported. Valid options for language {language} are:\n{EnumerationHelper.Join("\n", GetSupportedProgrammingModels(language))}");
            }
            return GlobalCoreToolsSettings.CurrentProgrammingModel.Value;
        }

        // Checks if the existing function application is using the new programming model (irrespective of language)
        public static bool isNewProgrammingModel()
        {
            if (GlobalCoreToolsSettings.CurrentProgrammingModel == ProgrammingModel.Preview)
            {
                return true;
            }
            // If the programming model is not apparent from GlobalCoreToolsSettings, check local.settings.json
            var localSettingsJsonContent = JObject.Parse(
                FileSystemHelpers.ReadAllTextFromFile(
                    Path.Join(Environment.CurrentDirectory, Constants.LocalSettingsJsonFileName)));
            JToken workerIndexingEnabled;
            return localSettingsJsonContent.TryGetValue(Constants.EnableWorkerIndexing, out workerIndexingEnabled)
                && Boolean.Parse(workerIndexingEnabled.ToString());
        }
    }
}