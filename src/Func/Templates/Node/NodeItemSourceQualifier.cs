// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Azure.Functions.Cli.Templates.V2;

namespace Azure.Functions.Cli.Templates.Node;

/// <summary>
/// Validates the association between an original Node job body and its selected native source without selecting output paths.
/// </summary>
internal static class NodeItemSourceQualifier
{
    internal static void Validate(NodeItemSource source, string? language)
    {
        ArgumentNullException.ThrowIfNull(source);
        string extension = language switch
        {
            "JavaScript" => ".js",
            "TypeScript" => ".ts",
            _ => throw Invalid("Node source qualification requires the selected canonical JavaScript or TypeScript language."),
        };
        NewTemplate template = source.Template;
        if (template.Jobs is not { Count: 1 } || template.Jobs[0] is not { Type: "CreateNewApp", Actions: { Count: 2 } names }
            || template.Actions is not { Count: 2 } actions || template.Files is not { Count: 1 } files
            || names.Distinct(StringComparer.Ordinal).Count() != 2
            || actions.Any(action => string.IsNullOrWhiteSpace(action.Name))
            || actions.Select(action => action.Name).Distinct(StringComparer.Ordinal).Count() != 2)
        {
            throw Invalid("Node source qualification requires one original CreateNewApp job and an unambiguous single-file action graph.");
        }
        V2Action? read = actions.SingleOrDefault(action => action.Name == names[0]);
        V2Action? write = actions.SingleOrDefault(action => action.Name == names[1]);
        if (read is not { Type: "GetTemplateFileContent", FilePath: not null, AssignTo: not null }
            || write is not { Type: "WriteToFile", FilePath: not null, Source: not null }
            || read.AssignTo != write.Source || string.IsNullOrWhiteSpace(read.AssignTo)
            || read.ContinueOnError || write.ContinueOnError || write.FileContent is not null
            || !IsSegment(read.FilePath) || !files.TryGetValue(read.FilePath, out string? body) || body is null)
        {
            throw Invalid("The original Node write action is not associated with exactly one owned source body.");
        }
        if (HasDaprHelper(template.Id) || (template.Jobs[0].Inputs ?? []).Any(input => input.AssignTo != "$(FUNCTION_NAME_INPUT)"))
        {
            ValidateReviewedBody(template.Id, body);
        }
        string[] parts = write.FilePath.Split('/');
        if (parts.Length < 2 || parts[..^1].Any(part => !IsSegment(part))
            || parts[^1] != "$(FUNCTION_NAME_INPUT)" + extension)
        {
            throw Invalid("The original Node write path must contain a constant relative directory and one language-matched function-name filename.");
        }
        using var configuration = JsonDocument.Parse(source.Configuration);
        JsonElement root = configuration.RootElement;
        string sourceName = NodeItemCompatibility.Text(root, "sourceName");
        JsonElement declaredSymbols = NodeItemCompatibility.Property(root, "symbols");
        JsonElement intent = NodeItemCompatibility.Property(declaredSymbols, "InvocationOutputIntent");
        if (NodeItemCompatibility.Text(intent, "type") != "bind"
            || NodeItemCompatibility.Text(intent, "binding") != "host:func:item-output-intent")
        {
            throw Invalid("The authored Node invocation intent must remain a host binding, not a user parameter.");
        }
        const string suffix = "/.template.config/template.json";
        if (!IsSegment(sourceName) || !source.ConfigurationPath.EndsWith(suffix, StringComparison.Ordinal)
            || source.ContentPath != source.ConfigurationPath[..^suffix.Length] + "/" + sourceName + extension
            || root.TryGetProperty("additionalConfigFiles", out _)
            || (root.TryGetProperty("preferNameDirectory", out JsonElement preferDirectory) && preferDirectory.ValueKind != JsonValueKind.False)
            || (root.TryGetProperty("symbols", out JsonElement renames) && renames.ValueKind == JsonValueKind.Object
                && renames.EnumerateObject().Any(symbol => symbol.Value.ValueKind == JsonValueKind.Object
                    && symbol.Value.TryGetProperty("fileRename", out _))))
        {
            throw Invalid("The selected native Node source does not have the qualified single-file output layout.");
        }
        string expected = body.Replace("$(FUNCTION_NAME_INPUT)", sourceName, StringComparison.Ordinal);
        if (HasDaprHelper(template.Id))
        {
            _ = IsHistoricalEncoding(root);
            if (sourceName != "funcNodeItem" || declaredSymbols.EnumerateObject().Count() != 1)
            {
                throw Invalid("The selected Node Dapr helper must use only its reviewed sourceName and host-bound output intent.");
            }
            string actual = Encoding.UTF8.GetString(source.Content.Span).ReplaceLineEndings("\n");
            if (actual != expected.ReplaceLineEndings("\n"))
            {
                expected = AuthorDaprHelpers(template.Id!, body).Replace("$(FUNCTION_NAME_INPUT)", sourceName, StringComparison.Ordinal);
            }
        }
        expected = QualifyParameters(template, root, expected);
        if (root.TryGetProperty("symbols", out JsonElement symbols) && symbols.TryGetProperty("AuthLevel", out JsonElement auth))
        {
            if (NodeItemCompatibility.Text(auth, "type") != "parameter" || NodeItemCompatibility.Text(auth, "datatype") != "choice"
                || NodeItemCompatibility.Text(auth, "defaultValue") != "anonymous"
                || NodeItemCompatibility.Text(auth, "replaces") != "__AUTH_LEVEL__")
            {
                throw Invalid("The selected native Node HTTP authorization substitution differs from its owned conversion.");
            }
            const string originalAuth = "authLevel: 'anonymous'";
            if (!expected.Contains(originalAuth, StringComparison.Ordinal))
            {
                throw Invalid("The original Node source is missing the declared HTTP authorization value.");
            }
            expected = expected.Replace(originalAuth, "authLevel: '__AUTH_LEVEL__'", StringComparison.Ordinal);
        }
        if (expected.ReplaceLineEndings("\n") != Encoding.UTF8.GetString(source.Content.Span).ReplaceLineEndings("\n"))
        {
            throw Invalid("The selected native Node payload differs from the original write action's owned source body.");
        }
    }

    internal static string? ValidateDaprInvocation(NodeItemSource source, string? name)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (!HasDaprHelper(source.Template.Id))
        {
            return null;
        }
        using var configuration = JsonDocument.Parse(source.Configuration);
        string sourceName = NodeItemCompatibility.Text(configuration.RootElement, "sourceName");
        string original = source.Template.Files!.Single().Value.Replace("$(FUNCTION_NAME_INPUT)", sourceName, StringComparison.Ordinal);
        if (original.ReplaceLineEndings("\n") == Encoding.UTF8.GetString(source.Content.Span).ReplaceLineEndings("\n"))
        {
            throw new NodeItemHelperCompatibilityException(
                $"The installed Node template '{source.Template.Id}' from '{source.Contract.Package?.Identifier}@{source.Contract.Package?.Version}' "
                + "has a fixed auxiliary function name or HTTP route and cannot create name-scoped Dapr helpers safely.");
        }
        string suffix = source.Template.Id == "DaprPublishOutputBinding-JavaScript" ? "_Topic" : "_Invoke";
        int maximum = 128 - suffix.Length;
        return name is null || name.Length > maximum
            ? $"Choose a function name of at most {maximum} characters for '{source.Template.Id}'; its '{suffix}' helper must fit the original 128-character function-name limit. Nothing was generated."
            : null;
    }

    private static bool HasDaprHelper(string? identity)
        => identity is "DaprPublishOutputBinding-JavaScript" or "DaprServiceInvocationTrigger-JavaScript";

    private static string AuthorDaprHelpers(string identity, string body)
        => identity == "DaprPublishOutputBinding-JavaScript"
            ? body.Replace("app.generic('DaprTopicTriggerNodeV4', {", "app.generic('$(FUNCTION_NAME_INPUT)_Topic', {", StringComparison.Ordinal)
            : body.Replace("app.http('InvokeOutputBinding', {", "app.http('$(FUNCTION_NAME_INPUT)_Invoke', {", StringComparison.Ordinal)
                .Replace("route: \"invoke/{appId}/{methodName}\"", "route: \"$(FUNCTION_NAME_INPUT)/invoke/{appId}/{methodName}\"", StringComparison.Ordinal)
                .Replace("http://localhost:7071/api/invoke/functionapp/{yourFunctionName}",
                    "http://localhost:7071/api/$(FUNCTION_NAME_INPUT)/invoke/functionapp/$(FUNCTION_NAME_INPUT)", StringComparison.Ordinal)
                .Replace("--method {yourFunctionName}", "--method $(FUNCTION_NAME_INPUT)", StringComparison.Ordinal);

    private static string QualifyParameters(NewTemplate template, JsonElement root, string body)
    {
        V2Input[] inputs = [.. (template.Jobs![0].Inputs ?? []).Where(input => input.AssignTo != "$(FUNCTION_NAME_INPUT)")];
        if (inputs.Length == 0)
        {
            return body;
        }
        bool historical = IsHistoricalEncoding(root);
        JsonElement symbols = NodeItemCompatibility.Property(root, "symbols");
        HashSet<string> expectedSymbols = ["InvocationOutputIntent"];
        foreach (V2Input input in inputs)
        {
            expectedSymbols.Add(input.ParamId!);
            if (!historical)
            {
                expectedSymbols.Add(input.ParamId + (input.ParamId == "cosmosTrigger-createLease" ? "-NodeBoolean" : "-NodeString"));
            }
        }
        if (!expectedSymbols.SetEquals(symbols.EnumerateObject().Select(symbol => symbol.Name)))
        {
            throw Invalid("The selected native Node property symbols differ from its owned conversion.");
        }
        foreach (V2Input input in inputs)
        {
            (string property, string literal) = PropertyMapping(template, input);
            string token = $"__FUNC_NODE_{input.ParamId}__";
            bool isBoolean = input.ParamId == "cosmosTrigger-createLease";
            JsonElement parameter = NodeItemCompatibility.Property(symbols, input.ParamId!);
            if (NodeItemCompatibility.Text(parameter, "type") != "parameter"
                || NodeItemCompatibility.Text(parameter, "datatype") != (isBoolean ? "choice" : "string"))
            {
                throw Invalid("The selected native Node property parameter changed type.");
            }
            if (parameter.TryGetProperty("replaces", out _) || parameter.TryGetProperty("forms", out _))
            {
                throw Invalid("The selected native Node parameter cannot replace content outside its qualified literal conversion.");
            }
            HashSet<string> parameterProperties = ["type", "datatype", "description", "isRequired", "defaultValue"];
            if (isBoolean)
            {
                parameterProperties.Add("choices");
            }
            if (!parameterProperties.SetEquals(parameter.EnumerateObject().Select(declared => declared.Name)))
            {
                throw Invalid("The selected native Node parameter differs from its reviewed encoding.");
            }
            if (isBoolean)
            {
                string[] choices = [.. NodeItemCompatibility.Array(parameter, "choices").Select(choice => NodeItemCompatibility.Text(choice, "choice"))];
                if (!choices.SequenceEqual(["true", "false"]))
                {
                    throw Invalid("The selected native Node Boolean substitution differs from its owned conversion.");
                }
            }
            if (historical)
            {
                continue;
            }
            JsonElement derived = NodeItemCompatibility.Property(symbols, input.ParamId + (isBoolean ? "-NodeBoolean" : "-NodeString"));
            if (parameter.TryGetProperty("replaces", out _) || derived.EnumerateObject().Count() != 4
                || NodeItemCompatibility.Text(derived, "type") != "derived"
                || NodeItemCompatibility.Text(derived, "valueSource") != input.ParamId
                || NodeItemCompatibility.Text(derived, "valueTransform") != (isBoolean ? "lowerCase" : "nodeString")
                || NodeItemCompatibility.Text(derived, "replaces") != token)
            {
                throw Invalid("The selected native Node literal substitution differs from its owned conversion.");
            }
            string declaration = $"    {property}: {literal},";
            string pattern = "(?m)^" + Regex.Escape(declaration) + "(?=\\r?$)";
            if (Regex.Matches(body, pattern).Count != 1)
            {
                throw Invalid("The original Node parameter property is missing, changed or ambiguous.");
            }
            body = Regex.Replace(body, pattern, _ => $"    {property}: {(isBoolean ? token : "'" + token + "'")},");
        }
        return body;
    }

    private static bool IsHistoricalEncoding(JsonElement root)
    {
        const string expectedForms = """
            {"nodeString":{"identifier":"chain","steps":["jsonEncode","nodeStringContent","nodeApostrophe"]},
             "nodeStringContent":{"identifier":"replace","pattern":"^\"|\"$","replacement":""},
             "nodeApostrophe":{"identifier":"replace","pattern":"'","replacement":"\\u0027"}}
            """;
        if (!root.TryGetProperty("forms", out JsonElement declaredForms))
        {
            return true;
        }
        using var forms = JsonDocument.Parse(expectedForms);
        if (!JsonElement.DeepEquals(declaredForms, forms.RootElement))
        {
            throw Invalid("The selected native Node string escaping differs from its owned conversion.");
        }
        return false;
    }

    internal static void ValidateArguments(NodeItemSource source, IReadOnlyDictionary<string, string?> parameters)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(parameters);
        using var configuration = JsonDocument.Parse(source.Configuration);
        if (configuration.RootElement.TryGetProperty("forms", out _))
        {
            return;
        }
        foreach (V2Input input in (source.Template.Jobs![0].Inputs ?? []).Where(input => input.AssignTo != "$(FUNCTION_NAME_INPUT)"))
        {
            (_, string literal) = PropertyMapping(source.Template, input);
            bool isBoolean = input.ParamId == "cosmosTrigger-createLease";
            string expected = isBoolean ? literal : literal[1..^1];
            if (!parameters.TryGetValue(input.ParamId!, out string? value)
                || !string.Equals(value, expected, isBoolean ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
            {
                throw new NodeItemParameterCompatibilityException(
                    $"The installed Node template '{source.Template.Id}' from '{source.Contract.Package?.Identifier}@{source.Contract.Package?.Version}' "
                    + $"cannot apply parameter '{input.ParamId}' to this requested value.");
            }
        }
    }

    private static (string Property, string Literal) PropertyMapping(NewTemplate template, V2Input input)
        => (template.Id!.Split('-')[0], input.ParamId, input.AssignTo) switch
        {
            ("TimerTrigger", "timerTrigger-schedule", "$(SCHEDULE_INPUT)") => ("schedule", "'0 */5 * * * *'"),
            ("QueueTrigger", "queueTrigger-connection", "$(CONNECTION_INPUT)") => ("connection", "''"),
            ("QueueTrigger", "queueTrigger-name", "$(QUEUE_NAME_INPUT)") => ("queueName", "'js-queue-items'"),
            ("BlobTrigger" or "EventGridBlobTrigger", "blobTrigger-connection", "$(CONNECTION_INPUT)") => ("connection", "''"),
            ("BlobTrigger" or "EventGridBlobTrigger", "blobTrigger-path", "$(PATH_INPUT)") => ("path", "'samples-workitems/{name}'"),
            ("EventHubTrigger", "eventHubTrigger-name", "$(EVENT_HUB_NAME_INPUT)") => ("eventHubName", "'samples-workitems'"),
            ("ServiceBusQueueTrigger" or "ServiceBusTopicTrigger", "serviceBusTrigger-connection", "$(CONNECTION_INPUT)") => ("connection", "''"),
            ("ServiceBusQueueTrigger", "serviceBusTrigger-name", "$(QUEUE_NAME_INPUT)") => ("queueName", "'myinputqueue'"),
            ("ServiceBusTopicTrigger", "serviceBusTrigger-topic", "$(TOPIC_NAME_INPUT)") => ("topicName", "'mytopic'"),
            ("ServiceBusTopicTrigger", "serviceBusTrigger-subscription", "$(SUBSCRIPTION_NAME_INPUT)") => ("subscriptionName", "'mysubscription'"),
            ("CosmosDBTrigger", "cosmosTrigger-connection", "$(CONNECTION_INPUT)") => ("connectionStringSetting", "''"),
            ("CosmosDBTrigger", "cosmosTrigger-database", "$(DATABASE_INPUT)") => ("databaseName", "''"),
            ("CosmosDBTrigger", "cosmosTrigger-collection", "$(COLLECTION_INPUT)") => ("collectionName", "''"),
            ("CosmosDBTrigger", "cosmosTrigger-createLease", "$(CREATE_LEASE_INPUT)") => ("createLeaseCollectionIfNotExists", "true"),
            _ => throw Invalid("The original Node prompt has no reviewed property association."),
        };

    private static void ValidateReviewedBody(string? identity, string body)
    {
        string expected = identity switch
        {
            "DaprPublishOutputBinding-JavaScript" => "BB0CD1A84EB4D34590A7319738AF495E8EA44A1FA018EAE5F0851703A661DB4B",
            "DaprServiceInvocationTrigger-JavaScript" => "3A5A71262E434CBBD82516C0F831C3F40F0BD49FC3DA16476A76D82479B9DF71",
            "BlobTrigger-JavaScript" => "773499147F9E697C074458C1B4D4152F32870FFEEE5A0301AC2EE4F95F135B02",
            "BlobTrigger-TypeScript" => "BD456F4A0FCAE70EF4EC73CD364215B3B76F07A3D3337150114488750C891527",
            "CosmosDBTrigger-JavaScript" => "A90F64FD9F3CB713EB54CAAC2F7235806C39246DB30FE4604AA41699DB1DAEF3",
            "CosmosDBTrigger-TypeScript" => "6F59394D49AB665B37BAA32F68B0E92CFD6C152EC563815B6BEEE2EEAD00EBAC",
            "EventGridBlobTrigger-JavaScript" => "D9F6EE6440831BE28725966D1C29D0D214E8F6D578EA1D8515E4C95CA6A1975A",
            "EventGridBlobTrigger-TypeScript" => "DE9FDAB6587A18ECA548E6E49E9A14A616CB9C98E379BDF93ABB5F41F9B3D35A",
            "EventHubTrigger-JavaScript" => "E6F56D5053168C3768D607F66472F9A66B6CEAF4B050209396D545C723A859D1",
            "EventHubTrigger-TypeScript" => "E5546062A26396BF0290A8AC481004E714E6BA786DB3532EAD6B6AEF59BCB75A",
            "QueueTrigger-JavaScript" => "CF8C0BCE5551F941D5D6854109C76E7AA7B145311178C2B39594E7242191637D",
            "QueueTrigger-TypeScript" => "3C76C6362E0CE9979776DA0E442CD648BD3A4163B1947A75F16F67725C06EE4E",
            "ServiceBusQueueTrigger-JavaScript" => "D9229D627355FFE8502D3CFE42024F1C36C95856CA05E1239866CD00043C7A12",
            "ServiceBusQueueTrigger-TypeScript" => "C8022455CE0534CF3EF838859165AB694F922238F79847DFEB522DFB62805015",
            "ServiceBusTopicTrigger-JavaScript" => "E5A29EEBB6F74659B3BFDBB849A81C88E0F5C82A2B2675DEBD1F294DC8F1208F",
            "ServiceBusTopicTrigger-TypeScript" => "F369B1B548DF93C16B22F5F79F3032AEFCD7B6B807A1DAC406E52C73B04E8A11",
            "TimerTrigger-JavaScript" => "CAF047F47111295F4DF1AC15C7791082855ED7727952CF889FE9A501C8A7E5A1",
            "TimerTrigger-TypeScript" => "FFACD7BBBF87FDF7518D188A5A6E75CF44398F05CA5768155626A04EAE8A29A5",
            _ => throw Invalid("The original Node parameter source has no reviewed body association."),
        };
        string actual = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(body.Replace("\r\n", "\n", StringComparison.Ordinal))));
        if (actual != expected)
        {
            throw Invalid("The original Node parameter source changed. Review its complete body/declaration association before authoring substitutions.");
        }
    }

    private static bool IsSegment(string value) => !string.IsNullOrWhiteSpace(value) && value is not ("." or "..")
        && value.All(character => char.IsAsciiLetterOrDigit(character) || character is '_' or '-' or '.');

    private static InvalidDataException Invalid(string message) => new(message);
}

internal sealed class NodeItemParameterCompatibilityException(string message) : Exception(message);

internal sealed class NodeItemHelperCompatibilityException(string message) : Exception(message);
