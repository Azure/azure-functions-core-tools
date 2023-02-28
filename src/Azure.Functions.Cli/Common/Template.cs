using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Azure.Functions.Cli.Common
{
    internal class Template
    {
        [JsonProperty("id")]
        public string Id { get; set; }

        [JsonProperty("function")]
        public JObject Function { get; set; }

        [JsonProperty("metadata")]
        public TemplateMetadata Metadata { get; set; }

        [JsonProperty("files")]
        public Dictionary<string, string> Files { get; set; }
    }

    internal class TemplateMetadata
    {
        [JsonProperty("name")]
        public string Name { get; set; }

        [JsonProperty("defaultFunctionName")]
        public string DefaultFunctionName { get; set; }

        [JsonProperty("language")]
        public string Language { get; set; }

        [JsonProperty("triggerType")]
        public string TriggerType { get; set; }

        [JsonProperty("userPrompt")]
        public IEnumerable<string> UserPrompt { get; set; }

        [JsonProperty("extensions")]
        public IEnumerable<FunctionExtension> Extensions { get; set; }


    }

    internal class FunctionExtension
    {
        [JsonProperty("id")]
        public string Id { get; set; }

        [JsonProperty("version")]
        public string Version { get; set; }
    }

    internal class NewTemplate
    {
        [JsonProperty("author")]
        public string Author { get; set; }

        [JsonProperty("id")]
        public string Id { get; set; }

        [JsonProperty("name")]
        public string Name { get; set; }

        [JsonProperty("description")]
        public string Description { get; set; }

        [JsonProperty("programmingModel")]
        public string ProgrammingModel { get; set; }

        [JsonProperty("language")]
        public string Language { get; set; }

        [JsonProperty("jobs")]
        public List<TemplateJob> Jobs { get; set; }

        [JsonProperty("actions")]
        public List<TemplateAction> Actions { get; set; }

        [JsonProperty("files")]
        public Dictionary<string, string> Files { get; set; }

    }

    internal class TemplateJob
    {
        [JsonProperty("name")]
        public string Name { get; set; }

        [JsonProperty("input")]
        public TemplateJobInput Input { get; set; }

        [JsonProperty("actions")]
        public List<string> Actions { get; set; }
    }

    internal class TemplateJobInput
    {
        [JsonProperty("userCommand")]
        public string UserCommand { get; set; }

        [JsonProperty("assignTo")]
        public string AssignTo { get; set; }
    }

    internal class TemplateAction
    {
        [JsonProperty("name")]
        public string Name { get; set; }

        [JsonProperty("type")]
        public string ActionType { get; set; }

        [JsonProperty("assignTo")]
        public string AssignTo { get; set; }

        [JsonProperty("paramId")]
        public string ParamId { get; set; }

        [JsonProperty("defaultValue")]
        public string DefaultValue { get; set; }

        [JsonProperty("source")]
        public string Source { get; set; }

        [JsonProperty("filePath")]
        public string FilePath { get; set; }

        [JsonProperty("createIfNotExists")]
        public bool? CreateIfNotExists { get; set; }

        [JsonProperty("continueOnError")]
        public bool? ContinueOnError { get; set; }

        [JsonProperty("errorText")]
        public string ErrorText { get; set; }

        public bool ActionPerformed { get; set; }
    }

    internal class UserPromptList
    {
        [JsonProperty("actions")]
        public List<UserPrompt> Actions { get; set; }
    }

    internal class UserPrompt
    {
        [JsonProperty("name")]
        public string Name { get; set; }

        [JsonProperty("value")]
        public string Value { get; set; }

        [JsonProperty("defaultValue")]
        public string DefaultValue { get; set; }

        [JsonProperty("label")]
        public string Label { get; set; }

        [JsonProperty("help")]
        public string Help { get; set; }

        [JsonProperty("validators")]
        public List<UserPromptValidator> Validators { get; set; }

        [JsonProperty("enum")]
        public List<UserPromptEnum> EnumList { get; set; }
    }

    internal class UserPromptValidator
    {
        [JsonProperty("expression")]
        public string Expression { get; set; }

        [JsonProperty("errorText")]
        public string ErrorText { get; set; }
    }

    internal class UserPromptEnum
    {
        [JsonProperty("value")]
        public string Value { get; set; }

        [JsonProperty("display")]
        public string Display { get; set; }
    }
}