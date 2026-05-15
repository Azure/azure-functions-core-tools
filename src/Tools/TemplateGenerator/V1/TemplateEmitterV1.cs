// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Text;
using Azure.Functions.Cli.Tools.TemplateGenerator.Common;
using Azure.Functions.Cli.Tools.TemplateGenerator.Model;
using Azure.Functions.Cli.Tools.TemplateGenerator.V1.Model;

namespace Azure.Functions.Cli.Tools.TemplateGenerator.V1;

internal static class TemplateEmitterV1
{
    public static string EmitFunctionTemplate() => EmitterHelpers.PocoHeader + """
        /// <summary>
        /// Represents an Azure Functions v1 (legacy) template definition.
        /// </summary>
        internal sealed class FunctionTemplateV1
        {
            /// <summary>Gets the unique template identifier.</summary>
            public required string Id { get; init; }

            /// <summary>Gets the template name (derived from Id, without the language suffix).</summary>
            public required string Name { get; init; }

            /// <summary>Gets the runtime version.</summary>
            public required string Runtime { get; init; }

            /// <summary>Gets the template files (filename to content).</summary>
            public required global::System.Collections.Generic.IReadOnlyDictionary<string, string> Files { get; init; }

            /// <summary>Gets the function definition.</summary>
            public required FunctionDefinitionV1 Function { get; init; }

            /// <summary>Gets the template metadata.</summary>
            public required TemplateMetadataV1 Metadata { get; init; }
        }

        """;

    public static string EmitFunctionDefinition() => EmitterHelpers.PocoHeader + """
        /// <summary>
        /// Represents the function configuration within a v1 template.
        /// </summary>
        internal sealed class FunctionDefinitionV1
        {
            /// <summary>Gets the function bindings.</summary>
            public required global::System.Collections.Generic.IReadOnlyList<TemplateBindingV1> Bindings { get; init; }

            /// <summary>Gets the script file path, if any.</summary>
            public string? ScriptFile { get; init; }

            /// <summary>Gets whether the function is disabled.</summary>
            public bool? Disabled { get; init; }

            /// <summary>Gets the entry point, if any.</summary>
            public string? EntryPoint { get; init; }
        }

        """;

    public static string EmitBindingDirection() => EmitterHelpers.PocoHeader + """
        /// <summary>
        /// Represents the direction of a v1 function binding.
        /// </summary>
        internal enum BindingDirectionV1
        {
            /// <summary>Input binding.</summary>
            In,

            /// <summary>Output binding.</summary>
            Out,
        }

        """;

    public static string EmitTemplateBinding() => EmitterHelpers.PocoHeader + """
        /// <summary>
        /// Represents a binding in a v1 function template.
        /// </summary>
        internal sealed class TemplateBindingV1
        {
            /// <summary>Gets the binding name.</summary>
            public required string Name { get; init; }

            /// <summary>Gets the binding type.</summary>
            public required string Type { get; init; }

            /// <summary>Gets the binding direction.</summary>
            public required BindingDirectionV1 Direction { get; init; }

            /// <summary>Gets any additional binding properties as extension data.</summary>
            [global::System.Text.Json.Serialization.JsonExtensionData]
            public global::System.Collections.Generic.IDictionary<string, object?>? ExtensionData { get; set; }
        }

        """;

    public static string EmitTemplateMetadata() => EmitterHelpers.PocoHeader + """
        /// <summary>
        /// Represents metadata about a v1 function template.
        /// </summary>
        internal sealed class TemplateMetadataV1
        {
            /// <summary>Gets the default function name.</summary>
            public required string DefaultFunctionName { get; init; }

            /// <summary>Gets the description.</summary>
            public required string Description { get; init; }

            /// <summary>Gets the display name.</summary>
            public required string Name { get; init; }

            /// <summary>Gets the programming language.</summary>
            public required string Language { get; init; }

            /// <summary>Gets the template categories.</summary>
            public required global::System.Collections.Generic.IReadOnlyList<string> Category { get; init; }

            /// <summary>Gets the category style.</summary>
            public required string CategoryStyle { get; init; }

            /// <summary>Gets whether the template is enabled in try mode.</summary>
            public required bool EnabledInTryMode { get; init; }

            /// <summary>Gets the user prompt fields.</summary>
            public global::System.Collections.Generic.IReadOnlyList<string>? UserPrompt { get; init; }

            /// <summary>Gets the template filters.</summary>
            public global::System.Collections.Generic.IReadOnlyList<string>? Filters { get; init; }

            /// <summary>Gets the trigger type name.</summary>
            public string? Trigger { get; init; }
        }

        """;

    public static IEnumerable<(string HintName, string Source)> EmitStaticData(EquatableArray<TemplateModelV1> templates)
    {
        // Per-template file: KnownTemplates.V1.{Name}.g.cs
        foreach (TemplateModelV1 template in templates)
        {
            var sb = new StringBuilder(4096);
            EmitterHelpers.AppendHeader(sb);
            sb.AppendLine("internal static partial class KnownTemplates");
            sb.AppendLine("{");
            sb.AppendLine("    internal static partial class V1");
            sb.AppendLine("    {");

            string fieldName = EmitterHelpers.SanitizeIdentifier(template.Name);
            sb.Append("        public static readonly FunctionTemplateV1 ").Append(fieldName).AppendLine(" =");
            EmitTemplate(sb, template, 3);
            sb.AppendLine(";");

            sb.AppendLine("    }");
            sb.AppendLine("}");
            yield return ("KnownTemplates.V1." + fieldName + ".g.cs", sb.ToString());
        }

        // Aggregate dictionary: KnownTemplates.V1.g.cs
        {
            var sb = new StringBuilder(1024);
            EmitterHelpers.AppendHeader(sb);
            sb.AppendLine("internal static partial class KnownTemplates");
            sb.AppendLine("{");
            sb.AppendLine("    internal static partial class V1");
            sb.AppendLine("    {");
            sb.AppendLine("        public static readonly global::System.Collections.Generic.IReadOnlyDictionary<string, FunctionTemplateV1> All =");
            sb.AppendLine("            new global::System.Collections.Generic.Dictionary<string, FunctionTemplateV1>");
            sb.AppendLine("            {");

            foreach (TemplateModelV1 template in templates)
            {
                string fieldName = EmitterHelpers.SanitizeIdentifier(template.Name);
                sb.Append("                [").Append(EmitterHelpers.Literal(template.Name)).Append("] = ").Append(fieldName).AppendLine(",");
            }

            sb.AppendLine("            };");
            sb.AppendLine("    }");
            sb.AppendLine("}");
            yield return ("KnownTemplates.V1.g.cs", sb.ToString());
        }
    }

    private static void EmitTemplate(StringBuilder sb, TemplateModelV1 template, int depth)
    {
        string indent = EmitterHelpers.Indent(depth);
        string inner = EmitterHelpers.Indent(depth + 1);

        sb.Append(indent).AppendLine("new FunctionTemplateV1");
        sb.Append(indent).AppendLine("{");
        AppendString(sb, inner, "Id", template.Id);
        AppendString(sb, inner, "Name", template.Name);
        AppendString(sb, inner, "Runtime", template.Runtime);
        EmitFiles(sb, template.Files, depth + 1);
        EmitFunction(sb, template.Function, depth + 1);
        EmitMetadata(sb, template.Metadata, depth + 1);
        sb.Append(indent).Append('}');
    }

    private static void EmitFiles(StringBuilder sb, EquatableArray<FileModelV1> files, int depth)
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
        foreach (FileModelV1 file in files)
        {
            sb.Append(inner).Append('[').Append(EmitterHelpers.Literal(file.Name)).Append("] = ").Append(EmitterHelpers.Literal(file.Content)).AppendLine(",");
        }

        sb.Append(indent).AppendLine("},");
    }

    private static void EmitFunction(StringBuilder sb, FunctionModelV1 function, int depth)
    {
        string indent = EmitterHelpers.Indent(depth);
        string inner = EmitterHelpers.Indent(depth + 1);

        sb.Append(indent).AppendLine("Function = new FunctionDefinitionV1");
        sb.Append(indent).AppendLine("{");

        if (function.Bindings.Length == 0)
        {
            sb.Append(inner).AppendLine("Bindings = [],");
        }
        else
        {
            sb.Append(inner).AppendLine("Bindings =");
            sb.Append(inner).AppendLine("[");
            foreach (BindingModelV1 binding in function.Bindings)
            {
                EmitBinding(sb, binding, depth + 2);
                sb.AppendLine(",");
            }

            sb.Append(inner).AppendLine("],");
        }

        AppendOptionalString(sb, inner, "ScriptFile", function.ScriptFile);
        AppendOptionalBool(sb, inner, "Disabled", function.Disabled);
        AppendOptionalString(sb, inner, "EntryPoint", function.EntryPoint);

        sb.Append(indent).AppendLine("},");
    }

    private static void EmitBinding(StringBuilder sb, BindingModelV1 binding, int depth)
    {
        string indent = EmitterHelpers.Indent(depth);
        string inner = EmitterHelpers.Indent(depth + 1);

        sb.Append(indent).AppendLine("new TemplateBindingV1");
        sb.Append(indent).AppendLine("{");

        AppendString(sb, inner, "Name", binding.Name);
        AppendString(sb, inner, "Type", binding.Type);
        sb.Append(inner).Append("Direction = BindingDirectionV1.").Append(binding.Direction switch
        {
            BindingDirectionKindV1.In => "In",
            BindingDirectionKindV1.Out => "Out",
            // V1 template bindings only use "in"/"out"; Trigger is only valid in bindings.json, not templates.json.
            _ => throw new global::System.InvalidOperationException($"Unsupported binding direction in template: {binding.Direction}"),
        }).AppendLine(",");

        if (binding.ExtensionData.Length > 0)
        {
            sb.Append(inner).AppendLine("ExtensionData = new global::System.Collections.Generic.Dictionary<string, object?>");
            sb.Append(inner).AppendLine("{");
            string dictInner = EmitterHelpers.Indent(depth + 2);
            foreach (BindingExtensionEntryV1 entry in binding.ExtensionData)
            {
                sb.Append(dictInner).Append('[').Append(EmitterHelpers.Literal(entry.Key)).Append("] = ");
                EmitExtensionValue(sb, entry.Value);
                sb.AppendLine(",");
            }

            sb.Append(inner).AppendLine("},");
        }

        sb.Append(indent).Append('}');
    }

    private static void EmitExtensionValue(StringBuilder sb, BindingExtensionValueV1 value)
    {
        switch (value.Kind)
        {
            case BindingExtensionValueKindV1.String:
                sb.Append(value.StringValue is null ? "null" : EmitterHelpers.Literal(value.StringValue));
                break;
            case BindingExtensionValueKindV1.Bool:
                sb.Append(value.BoolValue == true ? "true" : "false");
                break;
            case BindingExtensionValueKindV1.StringArray:
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

    private static void EmitMetadata(StringBuilder sb, MetadataModelV1 metadata, int depth)
    {
        string indent = EmitterHelpers.Indent(depth);
        string inner = EmitterHelpers.Indent(depth + 1);

        sb.Append(indent).AppendLine("Metadata = new TemplateMetadataV1");
        sb.Append(indent).AppendLine("{");

        AppendString(sb, inner, "DefaultFunctionName", metadata.DefaultFunctionName);
        AppendString(sb, inner, "Description", metadata.Description);
        AppendString(sb, inner, "Name", metadata.Name);
        AppendString(sb, inner, "Language", metadata.Language);
        AppendStringArray(sb, inner, "Category", metadata.Category);
        AppendString(sb, inner, "CategoryStyle", metadata.CategoryStyle);
        AppendBool(sb, inner, "EnabledInTryMode", metadata.EnabledInTryMode);
        AppendOptionalStringArray(sb, inner, "UserPrompt", metadata.UserPrompt);
        AppendOptionalStringArray(sb, inner, "Filters", metadata.Filters);
        AppendOptionalString(sb, inner, "Trigger", metadata.Trigger);

        sb.Append(indent).AppendLine("},");
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

    private static void AppendOptionalBool(StringBuilder sb, string indent, string name, bool? value)
    {
        if (value.HasValue)
        {
            AppendBool(sb, indent, name, value.Value);
        }
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

    private static void AppendOptionalStringArray(StringBuilder sb, string indent, string name, EquatableArray<string>? values)
    {
        if (values.HasValue)
        {
            AppendStringArray(sb, indent, name, values.Value);
        }
    }
}
