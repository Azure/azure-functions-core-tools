using System;
using System.Collections.Generic;
using System.Linq;
using Azure.Functions.Cli.Common;

namespace Azure.Functions.Cli.Helpers
{
    public static class ProgrammingModelHelper
    {
        public static IEnumerable<ProgrammingModel> GetProgrammingModels()
        {
            return Enum.GetValues<ProgrammingModel>();
        }

        public static ProgrammingModel ResolveProgrammingModel(string programmingModel, WorkerRuntime workerRuntime, string language)
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
                throw new CliArgumentsException($"The programming model {programmingModel} is not supported. Valid options for language {language} and worker-runtime {workerRuntime.ToString()} are: {GetProgrammingModels()}");
            }
            return GlobalCoreToolsSettings.CurrentProgrammingModel.Value;
        }
    }
}