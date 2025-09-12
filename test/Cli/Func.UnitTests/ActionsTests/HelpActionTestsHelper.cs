// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Text;
using Azure.Functions.Cli.Actions;
using Fclp;
using CLIArg = Azure.Functions.Cli.Common.CliArgument; // alias to avoid any ambiguity

namespace Azure.Functions.Cli.UnitTests.ActionsTests
{
    internal static class HelpActionTestsHelper
    {
        internal static TypeAttributePair Make(Type t)
            => new TypeAttributePair { Type = t, Attribute = t.GetCustomAttributes(typeof(ActionAttribute), inherit: false).Cast<ActionAttribute>().First() };

        internal static string CaptureConsoleOutput(Action action)
        {
            var originalOut = Console.Out;
            var sb = new StringBuilder();
            using var writer = new StringWriter(sb);
            Console.SetOut(writer);
            try
            {
                action();
            }
            finally
            {
                Console.SetOut(originalOut);
            }

            return sb.ToString();
        }

        [Action(Name = "parent", Context = Context.None, SubContext = Context.None, HelpText = "Parent command help.")]
        internal class ParentAction : BaseAction
        {
            public override ICommandLineParserResult ParseArgs(string[] args)
            {
                Parser.Setup<string>("alpha").WithDescription("Alpha option.");
                return base.ParseArgs(args);
            }

            public override IEnumerable<CLIArg> GetPositionalArguments() => new[]
            {
                new CLIArg { Name = "path", Description = "Path to resource" },
                new CLIArg { Name = "name", Description = "Name of entity" }
            };

            public override Task RunAsync() => Task.CompletedTask;
        }

        [Action(Name = "parent child", ParentCommandName = "parent", Context = Context.None, SubContext = Context.None, HelpText = "Child command help.")]
        internal class ChildAction : BaseAction
        {
            public override ICommandLineParserResult ParseArgs(string[] args)
            {
                Parser.Setup<bool>("flag").WithDescription("Flag option.");
                return base.ParseArgs(args);
            }

            public override IEnumerable<CLIArg> GetPositionalArguments() => Enumerable.Empty<CLIArg>();

            public override Task RunAsync() => Task.CompletedTask;
        }
    }
}
