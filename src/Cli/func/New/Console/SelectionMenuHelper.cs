// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Helpers;
using Spectre.Console;

namespace Azure.Functions.Cli.New
{
    public static class SelectionMenuHelper
    {
        public static WorkerRuntime AskRuntime()
        {
            var runtimes = Enum.GetValues<WorkerRuntime>();

            var selected = AnsiConsole.Prompt(
                new SelectionPrompt<WorkerRuntime>()
                    .Title("Select a worker runtime")
                    .PageSize(10)
                    .UseConverter(GetRuntimeDisplayText)
                    .AddChoices(runtimes));

            return selected;
        }

        public static string AskLanguage()
        {
            WorkerRuntimeLanguageHelper.WorkerToSupportedLanguages.TryGetValue(WorkerRuntime.Node, out var supportedNodeLanguages);

            if (supportedNodeLanguages == null || !supportedNodeLanguages.Any())
            {
                return null;
            }

            var selected = AnsiConsole.Prompt(
                new SelectionPrompt<string>()
                    .Title("Select a programming language")
                    .PageSize(10)
                    .AddChoices(supportedNodeLanguages));

            return selected;
        }

        private static string GetRuntimeDisplayText(WorkerRuntime r) => r switch
        {
            WorkerRuntime.DotnetIsolated => Markup.Escape(".NET (isolated worker model)"),
            WorkerRuntime.Dotnet => Markup.Escape(".NET (in-process model)"),
            WorkerRuntime.Node => "Node",
            WorkerRuntime.Python => "Python",
            WorkerRuntime.Powershell => "Powershell",
            WorkerRuntime.Custom => "Custom",
            _ => r.ToString()
        };
    }
}
