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

        // Given a worker runtime, this function returns a collection of the programming models that are supported.
        public static IEnumerable<ProgrammingModel> GetSupportedProgrammingModels(WorkerRuntime workerRuntime)
        {
            switch (workerRuntime)
            {
                case WorkerRuntime.python:
                    return new List<ProgrammingModel>() { ProgrammingModel.V1, ProgrammingModel.V2 };
                case WorkerRuntime.node:
                    return new List<ProgrammingModel>() { ProgrammingModel.V3, ProgrammingModel.V4 };
                default:
                    return new List<ProgrammingModel>() { ProgrammingModel.V1 };
            }
        }

        public static ProgrammingModel ResolveProgrammingModel(string programmingModel, WorkerRuntime workerRuntime, string language)
        {
            if (GlobalCoreToolsSettings.CurrentProgrammingModel != null)
            {
                return GlobalCoreToolsSettings.CurrentProgrammingModel.Value;
            }
            // We default to the "Default" programming model for that language if the model parameter is not specified
            if (string.IsNullOrEmpty(programmingModel))
            {
                if (workerRuntime == WorkerRuntime.node)
                {
                    GlobalCoreToolsSettings.CurrentProgrammingModel = ProgrammingModel.V3;
                }
                else if (workerRuntime == WorkerRuntime.python)
                {
                    GlobalCoreToolsSettings.CurrentProgrammingModel = ProgrammingModel.V2;
                }
                else
                {
                    GlobalCoreToolsSettings.CurrentProgrammingModel = ProgrammingModel.V1;
                }
            }
            else if (GetProgrammingModels().Any(pm => string.Equals(programmingModel, pm.ToString(), StringComparison.InvariantCultureIgnoreCase)))
            {
                GlobalCoreToolsSettings.CurrentProgrammingModel = GetProgrammingModels().First(pm => string.Equals(programmingModel, pm.ToString(), StringComparison.InvariantCultureIgnoreCase));
            }
            // If programmingModel is non-empty and does not match any progrmming model, then we raise an exception
            else
            {
                // TODO: Explicitly define the association between language, worker-runtime, and programming model
                throw new CliArgumentsException($"The programming model {programmingModel} is not supported. Valid options for language {language} and worker-runtime {workerRuntime.ToString()} are:\n{EnumerationHelper.Join("\n", GetSupportedProgrammingModels(workerRuntime))}");
            }
            return GlobalCoreToolsSettings.CurrentProgrammingModel.Value;
        }
    }
}