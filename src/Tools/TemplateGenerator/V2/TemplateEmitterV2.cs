// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Globalization;
using System.Text;
using Azure.Functions.Cli.Tools.TemplateGenerator.Common;
using Azure.Functions.Cli.Tools.TemplateGenerator.Model;
using Azure.Functions.Cli.Tools.TemplateGenerator.V2.Model;

namespace Azure.Functions.Cli.Tools.TemplateGenerator.V2;

internal static class TemplateEmitterV2
{
    public static string EmitFunctionTemplate() => EmitterHelpers.PocoHeader + """
        /// <summary>
        /// Represents an Azure Functions v2 (programming-model v2) template definition.
        /// </summary>
        internal sealed class FunctionTemplateV2
        {
            /// <summary>Gets the unique template identifier.</summary>
            public required string Id { get; init; }

            /// <summary>Gets the template name (derived from Id, without the language suffix).</summary>
            public required string Name { get; init; }

            /// <summary>Gets the template display name.</summary>
            public required string DisplayName { get; init; }

            /// <summary>Gets the description (often a localized token reference).</summary>
            public required string Description { get; init; }

            /// <summary>Gets the template author.</summary>
            public required string Author { get; init; }

            /// <summary>Gets the programming model identifier (e.g. "v2").</summary>
            public required string ProgrammingModel { get; init; }

            /// <summary>Gets the programming language (e.g. "python").</summary>
            public required string Language { get; init; }

            /// <summary>Gets the template files (filename to content).</summary>
            public required global::System.Collections.Generic.IReadOnlyDictionary<string, string> Files { get; init; }

            /// <summary>Gets the jobs offered by this template.</summary>
            public required global::System.Collections.Generic.IReadOnlyList<TemplateJob> Jobs { get; init; }

            /// <summary>Gets the action definitions referenced by jobs.</summary>
            public required global::System.Collections.Generic.IReadOnlyList<TemplateAction> Actions { get; init; }
        }

        """;

    public static string EmitTemplateJob() => EmitterHelpers.PocoHeader + """
        /// <summary>
        /// Represents a job (a discrete user-facing operation) within a v2 template.
        /// </summary>
        internal sealed class TemplateJob
        {
            /// <summary>Gets the job display name.</summary>
            public required string Name { get; init; }

            /// <summary>Gets the job type (e.g. CreateNewApp, AppendToFile, CreateNewBlueprint, AppendToBlueprint).</summary>
            public required string Type { get; init; }

            /// <summary>Gets the inputs requested from the user when running this job.</summary>
            public required global::System.Collections.Generic.IReadOnlyList<TemplateJobInput> Inputs { get; init; }

            /// <summary>Gets the names of actions (declared on <see cref="FunctionTemplateV2.Actions"/>) executed by this job, in order.</summary>
            public required global::System.Collections.Generic.IReadOnlyList<string> Actions { get; init; }
        }

        """;

    public static string EmitTemplateJobInput() => EmitterHelpers.PocoHeader + """
        /// <summary>
        /// Represents a single user input within a v2 template job.
        /// </summary>
        internal sealed class TemplateJobInput
        {
            /// <summary>Gets the variable name the input value will be assigned to.</summary>
            public required string AssignTo { get; init; }

            /// <summary>Gets the parameter id (used for prompting/localization).</summary>
            public required string ParamId { get; init; }

            /// <summary>Gets the default value, if any.</summary>
            public string? DefaultValue { get; init; }

            /// <summary>Gets whether the input is required.</summary>
            public required bool Required { get; init; }

            /// <summary>Gets an optional condition that must be satisfied for the input to be requested.</summary>
            public TemplateInputCondition? Condition { get; init; }
        }

        """;

    public static string EmitTemplateInputCondition() => EmitterHelpers.PocoHeader + """
        /// <summary>
        /// Represents a condition that gates a v2 template job input.
        /// </summary>
        internal sealed class TemplateInputCondition
        {
            /// <summary>Gets the name of the value being tested (e.g. "ClientId").</summary>
            public required string Name { get; init; }

            /// <summary>Gets the candidate values.</summary>
            public required global::System.Collections.Generic.IReadOnlyList<string> Values { get; init; }

            /// <summary>Gets the comparison operator (e.g. "IN").</summary>
            public required string Operator { get; init; }
        }

        """;

    public static string EmitTemplateAction() => EmitterHelpers.PocoHeader + """
        /// <summary>
        /// Represents an action definition referenced by a v2 template job.
        /// </summary>
        internal sealed class TemplateAction
        {
            /// <summary>Gets the action name (referenced by jobs).</summary>
            public required string Name { get; init; }

            /// <summary>Gets the action type (e.g. AppendToFile, GetTemplateFileContent, ReplaceTokensInText, ShowMarkdownPreview, WriteToFile).</summary>
            public required string Type { get; init; }

            /// <summary>Gets any additional action properties (the schema varies per action <see cref="Type"/>).</summary>
            [global::System.Text.Json.Serialization.JsonExtensionData]
            public global::System.Collections.Generic.IDictionary<string, object?>? ExtensionData { get; set; }
        }

        """;

    public static IEnumerable<(string HintName, string Source)> EmitStaticData(EquatableArray<TemplateModelV2> templates)
    {
        foreach (TemplateModelV2 template in templates)
        {
            var sb = new StringBuilder(4096);
            EmitterHelpers.AppendHeader(sb);
            sb.AppendLine("internal static partial class KnownTemplates");
            sb.AppendLine("{");
            sb.AppendLine("    internal static partial class V2");
            sb.AppendLine("    {");

            string fieldName = EmitterHelpers.SanitizeIdentifier(template.Name);
            sb.Append("        public static readonly FunctionTemplateV2 ").Append(fieldName).AppendLine(" =");
            EmitTemplate(sb, template, 3);
            sb.AppendLine(";");

            sb.AppendLine("    }");
            sb.AppendLine("}");
            yield return ("KnownTemplates.V2." + fieldName + ".g.cs", sb.ToString());
        }

        {
            var sb = new StringBuilder(1024);
            EmitterHelpers.AppendHeader(sb);
            sb.AppendLine("internal static partial class KnownTemplates");
            sb.AppendLine("{");
            sb.AppendLine("    internal static partial class V2");
            sb.AppendLine("    {");
            sb.AppendLine("        public static readonly global::System.Collections.Generic.IReadOnlyDictionary<string, FunctionTemplateV2> All =");
            sb.AppendLine("            new global::System.Collections.Generic.Dictionary<string, FunctionTemplateV2>");
            sb.AppendLine("            {");

            foreach (TemplateModelV2 template in templates)
            {
                string fieldName = EmitterHelpers.SanitizeIdentifier(template.Name);
                sb.Append("                [").Append(EmitterHelpers.Literal(template.Name)).Append("] = ").Append(fieldName).AppendLine(",");
            }

            sb.AppendLine("            };");
            sb.AppendLine("    }");
            sb.AppendLine("}");
            yield return ("KnownTemplates.V2.g.cs", sb.ToString());
        }
    }

    private static void EmitTemplate(StringBuilder sb, TemplateModelV2 template, int depth)
    {
        string indent = EmitterHelpers.Indent(depth);
        string inner = EmitterHelpers.Indent(depth + 1);

        sb.Append(indent).AppendLine("new FunctionTemplateV2");
        sb.Append(indent).AppendLine("{");
        AppendString(sb, inner, "Id", template.Id);
        AppendString(sb, inner, "Name", template.Name);
        AppendString(sb, inner, "DisplayName", template.DisplayName);
        AppendString(sb, inner, "Description", template.Description);
        AppendString(sb, inner, "Author", template.Author);
        AppendString(sb, inner, "ProgrammingModel", template.ProgrammingModel);
        AppendString(sb, inner, "Language", template.Language);
        EmitFiles(sb, template.Files, depth + 1);
        EmitJobs(sb, template.Jobs, depth + 1);
        EmitActions(sb, template.Actions, depth + 1);
        sb.Append(indent).Append('}');
    }

    private static void EmitFiles(StringBuilder sb, EquatableArray<FileModelV2> files, int depth)
    {
        string indent = EmitterHelpers.Indent(depth);
        string inner = EmitterHelpers.Indent(depth + 1);

        if (files.Length == 0)
        {
            sb.Append(indent).AppendLine("Files = new global::System.Collections.Generic.Dictionary<string, string>(),");
            return;
        }

        sb.Append(indent).AppendLine("Files = new global::System.Collections.Generic.Dictionary<string, string>");
        sb.Append(indent).AppendLine("{");
        foreach (FileModelV2 file in files)
        {
            sb.Append(inner).Append('[').Append(EmitterHelpers.Literal(file.Name)).Append("] = ").Append(EmitterHelpers.Literal(file.Content)).AppendLine(",");
        }

        sb.Append(indent).AppendLine("},");
    }

    private static void EmitJobs(StringBuilder sb, EquatableArray<TemplateJobModelV2> jobs, int depth)
    {
        string indent = EmitterHelpers.Indent(depth);

        if (jobs.Length == 0)
        {
            sb.Append(indent).AppendLine("Jobs = [],");
            return;
        }

        sb.Append(indent).AppendLine("Jobs =");
        sb.Append(indent).AppendLine("[");
        foreach (TemplateJobModelV2 job in jobs)
        {
            EmitJob(sb, job, depth + 1);
            sb.AppendLine(",");
        }

        sb.Append(indent).AppendLine("],");
    }

    private static void EmitJob(StringBuilder sb, TemplateJobModelV2 job, int depth)
    {
        string indent = EmitterHelpers.Indent(depth);
        string inner = EmitterHelpers.Indent(depth + 1);

        sb.Append(indent).AppendLine("new TemplateJob");
        sb.Append(indent).AppendLine("{");
        AppendString(sb, inner, "Name", job.Name);
        AppendString(sb, inner, "Type", job.Type);
        EmitInputs(sb, job.Inputs, depth + 1);
        EmitActionRefs(sb, job.ActionRefs, depth + 1);
        sb.Append(indent).Append('}');
    }

    private static void EmitInputs(StringBuilder sb, EquatableArray<TemplateInputModelV2> inputs, int depth)
    {
        string indent = EmitterHelpers.Indent(depth);

        if (inputs.Length == 0)
        {
            sb.Append(indent).AppendLine("Inputs = [],");
            return;
        }

        sb.Append(indent).AppendLine("Inputs =");
        sb.Append(indent).AppendLine("[");
        foreach (TemplateInputModelV2 input in inputs)
        {
            EmitInput(sb, input, depth + 1);
            sb.AppendLine(",");
        }

        sb.Append(indent).AppendLine("],");
    }

    private static void EmitInput(StringBuilder sb, TemplateInputModelV2 input, int depth)
    {
        string indent = EmitterHelpers.Indent(depth);
        string inner = EmitterHelpers.Indent(depth + 1);

        sb.Append(indent).AppendLine("new TemplateJobInput");
        sb.Append(indent).AppendLine("{");
        AppendString(sb, inner, "AssignTo", input.AssignTo);
        AppendString(sb, inner, "ParamId", input.ParamId);
        AppendOptionalString(sb, inner, "DefaultValue", input.DefaultValue);
        AppendBool(sb, inner, "Required", input.Required);

        if (input.Condition is not null)
        {
            EmitCondition(sb, input.Condition, depth + 1);
        }

        sb.Append(indent).Append('}');
    }

    private static void EmitCondition(StringBuilder sb, TemplateInputConditionModelV2 condition, int depth)
    {
        string indent = EmitterHelpers.Indent(depth);
        string inner = EmitterHelpers.Indent(depth + 1);

        sb.Append(indent).AppendLine("Condition = new TemplateInputCondition");
        sb.Append(indent).AppendLine("{");
        AppendString(sb, inner, "Name", condition.Name);
        AppendStringArray(sb, inner, "Values", condition.Values);
        AppendString(sb, inner, "Operator", condition.Operator);
        sb.Append(indent).AppendLine("},");
    }

    private static void EmitActionRefs(StringBuilder sb, EquatableArray<string> actionRefs, int depth)
    {
        string indent = EmitterHelpers.Indent(depth);

        if (actionRefs.Length == 0)
        {
            sb.Append(indent).AppendLine("Actions = [],");
            return;
        }

        sb.Append(indent).Append("Actions = [");
        for (int i = 0; i < actionRefs.Length; i++)
        {
            if (i > 0)
            {
                sb.Append(", ");
            }

            sb.Append(EmitterHelpers.Literal(actionRefs[i]));
        }

        sb.AppendLine("],");
    }

    private static void EmitActions(StringBuilder sb, EquatableArray<TemplateActionModelV2> actions, int depth)
    {
        string indent = EmitterHelpers.Indent(depth);

        if (actions.Length == 0)
        {
            sb.Append(indent).AppendLine("Actions = [],");
            return;
        }

        sb.Append(indent).AppendLine("Actions =");
        sb.Append(indent).AppendLine("[");
        foreach (TemplateActionModelV2 action in actions)
        {
            EmitAction(sb, action, depth + 1);
            sb.AppendLine(",");
        }

        sb.Append(indent).AppendLine("],");
    }

    private static void EmitAction(StringBuilder sb, TemplateActionModelV2 action, int depth)
    {
        string indent = EmitterHelpers.Indent(depth);
        string inner = EmitterHelpers.Indent(depth + 1);

        sb.Append(indent).AppendLine("new TemplateAction");
        sb.Append(indent).AppendLine("{");
        AppendString(sb, inner, "Name", action.Name);
        AppendString(sb, inner, "Type", action.Type);

        if (action.ExtensionData.Length > 0)
        {
            sb.Append(inner).AppendLine("ExtensionData = new global::System.Collections.Generic.Dictionary<string, object?>");
            sb.Append(inner).AppendLine("{");
            string dictInner = EmitterHelpers.Indent(depth + 2);
            foreach (ActionExtensionEntryV2 entry in action.ExtensionData)
            {
                sb.Append(dictInner).Append('[').Append(EmitterHelpers.Literal(entry.Key)).Append("] = ");
                EmitExtensionValue(sb, entry.Value);
                sb.AppendLine(",");
            }

            sb.Append(inner).AppendLine("},");
        }

        sb.Append(indent).Append('}');
    }

    private static void EmitExtensionValue(StringBuilder sb, ActionExtensionValueV2 value)
    {
        switch (value.Kind)
        {
            case ActionExtensionValueKindV2.String:
                sb.Append(value.StringValue is null ? "null" : EmitterHelpers.Literal(value.StringValue));
                break;
            case ActionExtensionValueKindV2.Bool:
                sb.Append(value.BoolValue == true ? "true" : "false");
                break;
            case ActionExtensionValueKindV2.Number:
                sb.Append(value.NumberValue!.Value.ToString("R", CultureInfo.InvariantCulture));
                break;
            case ActionExtensionValueKindV2.Null:
                sb.Append("null");
                break;
            case ActionExtensionValueKindV2.StringArray:
                sb.Append("new string[] { ");
                if (value.ArrayValue.HasValue)
                {
                    for (int i = 0; i < value.ArrayValue.Value.Length; i++)
                    {
                        if (i > 0)
                        {
                            sb.Append(", ");
                        }

                        sb.Append(EmitterHelpers.Literal(value.ArrayValue.Value[i]));
                    }
                }

                sb.Append(" }");
                break;
        }
    }

    private static void AppendString(StringBuilder sb, string indent, string name, string value)
    {
        sb.Append(indent).Append(name).Append(" = ").Append(EmitterHelpers.Literal(value)).AppendLine(",");
    }

    private static void AppendOptionalString(StringBuilder sb, string indent, string name, string? value)
    {
        if (value is not null)
        {
            AppendString(sb, indent, name, value);
        }
    }

    private static void AppendBool(StringBuilder sb, string indent, string name, bool value)
    {
        sb.Append(indent).Append(name).Append(" = ").Append(value ? "true" : "false").AppendLine(",");
    }

    private static void AppendStringArray(StringBuilder sb, string indent, string name, EquatableArray<string> values)
    {
        sb.Append(indent).Append(name).Append(" = [");
        for (int i = 0; i < values.Length; i++)
        {
            if (i > 0)
            {
                sb.Append(", ");
            }

            sb.Append(EmitterHelpers.Literal(values[i]));
        }

        sb.AppendLine("],");
    }
}
