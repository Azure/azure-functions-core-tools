// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Spectre.Console;
using static Azure.Functions.Cli.New.OutputTheme;

public static class ConsoleLogger
{
    // Convenience writers (optional): keeps call sites close to old behavior
    public static void WriteTitle(string value) => AnsiConsole.MarkupLine(TitleColor(value));

    public static void WriteVerbose(string value) => AnsiConsole.MarkupLine(VerboseColor(value));

    public static void WriteLink(string value) => AnsiConsole.MarkupLine(LinksColor(value));

    public static void WriteAdditionalInfo(string value) => AnsiConsole.MarkupLine(AdditionalInfoColor(value));

    public static void WriteExample(string value) => AnsiConsole.MarkupLine(ExampleColor(value));

    public static void WriteError(string value) => AnsiConsole.MarkupLine(ErrorColor(value));

    public static void WriteQuestion(string value) => AnsiConsole.MarkupLine(QuestionColor(value));

    public static void WriteWarning(string value) => AnsiConsole.MarkupLine(WarningColor(value));

    public static void WriteQuietWarning(string value) => AnsiConsole.MarkupLine(QuietWarningColor(value));

    public static void WriteHttpFunctionName(string value) => AnsiConsole.MarkupLine(HttpFunctionNameColor(value));

    public static void WriteHttpFunctionUrl(string value) => AnsiConsole.MarkupLine(HttpFunctionUrlColor(value));
}
