// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Text;
using System.Text.RegularExpressions;
using Azure.Functions.Cli.Templates.Engine;

namespace Azure.Functions.Cli.Templates.DotNet;

/// <summary>
/// Checks function IDs from the six source-qualified auxiliary item layouts after native name substitution.
/// </summary>
internal static class DotNetAuxiliaryNameValidation
{
    internal static string? Validate(string identity, NativeTemplatePreparationSnapshot snapshot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(identity);
        ArgumentNullException.ThrowIfNull(snapshot);
        if (snapshot.ExpandedFiles.Count != 1 || snapshot.Effects.CreationResult.PrimaryOutputs.Count != 1
            || NativeTemplateSourceSnapshot.Normalize(snapshot.ExpandedFiles.Single().Key)
                != NativeTemplateSourceSnapshot.Normalize(snapshot.Effects.CreationResult.PrimaryOutputs[0].Path))
        {
            return "The qualified .NET auxiliary item did not expand to its single declared C# source.";
        }
        string source = Encoding.UTF8.GetString(snapshot.ExpandedFiles.Single().Value.Span).Replace("\r\n", "\n", StringComparison.Ordinal);
        string variant = identity["Azure.Function.CSharp.Isolated.".Length..];
        string prefix = variant switch
        {
            "DurableFunctionsEntityClass.1.x" or "SignalR.3.x" => "public class ",
            "DurableFunctionsEntityFunction.1.x" or "DurableFunctionsOrchestration.1.x" => "public static class ",
            "DaprPublishOutputBinding" or "DaprServiceInvocationTrigger" => "    public static class ",
            _ => throw new ArgumentException("Supply a qualified auxiliary item identity.", nameof(identity)),
        };
        string[] declarations = [.. source.Split('\n').Where(line => line.StartsWith(prefix, StringComparison.Ordinal))];
        if (declarations.Length != 1)
        {
            return Ambiguous();
        }
        string type = declarations[0][prefix.Length..];
        if (variant == "DurableFunctionsEntityClass.1.x")
        {
            const string entityBase = " : TaskEntity<int>";
            if (!type.EndsWith(entityBase, StringComparison.Ordinal))
            {
                return Ambiguous();
            }
            type = type[..^entityBase.Length];
        }
        string[] attributes = [.. source.Split('\n').Select(line => line.TrimStart())
            .Where(line => line.StartsWith("[Function(", StringComparison.Ordinal))];
        string[] expected = variant switch
        {
            "DurableFunctionsEntityClass.1.x" or "DurableFunctionsEntityFunction.1.x" => [$"[Function(nameof({type}))]"],
            "DurableFunctionsOrchestration.1.x" =>
                [$"[Function(nameof({type}))]", $"[Function(\"{type}-SayHello\")]", $"[Function(\"{type}-HttpStart\")]"],
            "SignalR.3.x" => [$"[Function(\"{type}\")]"],
            "DaprPublishOutputBinding" => [$"[Function(\"{type}\")]", $"[Function(\"{type}-TopicTrigger\")]"],
            "DaprServiceInvocationTrigger" => [$"[Function(\"{type}\")]", $"[Function(\"{type}-Invoke\")]"],
            _ => throw new ArgumentException("Supply a qualified auxiliary item identity.", nameof(identity)),
        };
        if (!attributes.SequenceEqual(expected, StringComparer.Ordinal)
            || (variant == "DaprServiceInvocationTrigger"
                && !source.Contains($"Route = \"{type}/invoke/{{appId}}/{{methodName}}\"", StringComparison.Ordinal)))
        {
            return Ambiguous();
        }
        string[] names = variant switch
        {
            "DurableFunctionsOrchestration.1.x" => [type, type + "-SayHello", type + "-HttpStart"],
            "DaprPublishOutputBinding" => [type, type + "-TopicTrigger"],
            "DaprServiceInvocationTrigger" => [type, type + "-Invoke"],
            _ => [type],
        };
        foreach (string name in names)
        {
            if (!Regex.IsMatch(name, "\\A[a-z][a-z0-9_-]{0,127}\\z", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)
                || name.Equals("host", StringComparison.OrdinalIgnoreCase))
            {
                return $"Generated function '{name}' is not a valid Azure Functions name. Supply --name starting with an ASCII letter, "
                    + "using only ASCII letters, digits, underscores or hyphens, and leave room within 128 characters for generated helper suffixes. 'host' is reserved.";
            }
        }
        return null;
    }

    private static string Ambiguous() => "The qualified .NET auxiliary function names could not be derived unambiguously from native output. No files were created.";
}
