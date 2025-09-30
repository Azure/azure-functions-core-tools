// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.CommandLine;
using System.CommandLine.Help;
using System.CommandLine.Invocation;
using Spectre.Console;

namespace Azure.Functions.Cli;

/// <summary>
/// Provides command line help.
/// </summary>
public sealed class FuncHelpAction : SynchronousCommandLineAction
{
    private readonly HelpAction _defaultHelp;

    public FuncHelpAction(HelpAction action) => _defaultHelp = action;

    public override int Invoke(ParseResult parseResult)
    {
        PrintLogo();

        int result = _defaultHelp.Invoke(parseResult);

        return result;
    }

    private static void PrintLogo()
    {
        // Build the ASCII art using Spectre markup.
        var logo = $@"
                  {AlternateLogoColor("%%%%%%")}
                 {AlternateLogoColor("%%%%%%")}
            @   {AlternateLogoColor("%%%%%%")}    @
          @@   {AlternateLogoColor("%%%%%%")}      @@
       @@@    {AlternateLogoColor("%%%%%%%%%%%", 3)}    @@@
     @@      {AlternateLogoColor("%%%%%%%%%%", 7)}        @@
       @@         {AlternateLogoColor("%%%%", 1)}       @@
         @@      {AlternateLogoColor("%%%")}       @@
           @@    {AlternateLogoColor("%%")}      @@
                {AlternateLogoColor("%%")}
                {AlternateLogoColor("%")}";

        // Color the '@' rails (dim cyan ≈ “DarkCyan”)
        logo = logo.Replace("@", "[cyan dim]@[/]");

        // Write it
        AnsiConsole.MarkupLine(logo);
    }

    // Returns Spectre.Console markup with two-tone coloring for the string.
    // If firstColorCount is set, the first N chars are bright yellow and the rest dim yellow.
    // Otherwise it splits the string in half, like your original logic.
    private static string AlternateLogoColor(string str, int firstColorCount = -1)
    {
        if (str.Length == 1)
        {
            return "[yellow dim]" + str + "[/]";
        }

        int split = firstColorCount != -1 ? firstColorCount : str.Length / 2;
        var a = str.Substring(0, split);
        var b = str.Substring(split);

        // Use “yellow” for the bright half and “yellow dim” for the darker half
        return $"[yellow]{a}[/][yellow dim]{b}[/]";
    }
}
