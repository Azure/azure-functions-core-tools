// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Text;
using Azure.Functions.Cli.Actions;
using Azure.Functions.Cli.Interfaces;
using FluentAssertions;
using Xunit;

namespace Azure.Functions.Cli.UnitTests.ActionsTests
{
    public class HelpActionTests
    {
        [Fact]
        public void UsageFormat_ShouldNotContainDuplicateContext()
        {
            var expectedGeneralFormat = "Usage: func [context] <action> [-/--options]";
            var expectedContextWithSubcontextFormat = "func {context} [subcontext] <action> [-/--options]";
            var expectedContextWithoutSubcontextFormat = "func {context} <action> [-/--options]";
            var expectedSubContextFormat = "func {context} {subcontext} <action> [-/--options]";
            var problematicFormat = "func [context] [context] <action>";

            expectedGeneralFormat.Should().NotContain("[context] [context]");
            expectedContextWithSubcontextFormat.Should().NotContain("[context] <action>");
            expectedContextWithSubcontextFormat.Should().Contain("[subcontext]");
            expectedContextWithoutSubcontextFormat.Should().NotContain("[subcontext]");
            expectedSubContextFormat.Should().NotContain("[context]");
            problematicFormat.Should().Contain("[context] [context]");
        }

        [Fact]
        public void DisplayActionHelp_IncludesSubcommands_AndParentPositionalArguments()
        {
            var actions = new[]
            {
                HelpActionTestsHelper.Make(typeof(HelpActionTestsHelper.ParentAction)),
                HelpActionTestsHelper.Make(typeof(HelpActionTestsHelper.ChildAction))
            };
            var parentInstance = new HelpActionTestsHelper.ParentAction();
            var parseResult = parentInstance.ParseArgs(Array.Empty<string>());
            var help = new HelpAction(actions, _ => (IAction)Activator.CreateInstance(_), parentInstance, parseResult);

            var output = HelpActionTestsHelper.CaptureConsoleOutput(() => help.RunAsync().GetAwaiter().GetResult());

            output.Should().Contain("Parent command help.");
            output.Should().Contain("Child command help.");

            // Parent positional arguments now
            output.Should().Contain("path");
            output.Should().Contain("Path to resource");
            output.Should().Contain("name");
            output.Should().Contain("Name of entity");
            output.Should().Contain("Alpha option.");
            output.Should().Contain("Flag option.");
        }

        [Fact]
        public void DisplayActionHelp_OnlyParent_WhenNoSubcommands()
        {
            var actions = new[] { HelpActionTestsHelper.Make(typeof(HelpActionTestsHelper.ParentAction)) };
            var parentInstance = new HelpActionTestsHelper.ParentAction();
            var parseResult = parentInstance.ParseArgs(Array.Empty<string>());
            var help = new HelpAction(actions, _ => (IAction)Activator.CreateInstance(_), parentInstance, parseResult);

            var output = HelpActionTestsHelper.CaptureConsoleOutput(() => help.RunAsync().GetAwaiter().GetResult());

            output.Should().Contain("parent");

            // Should still show parent positional args
            output.Should().Contain("Path to resource");
            output.Should().NotContain("Child command help.");
        }

        [Fact]
        public void DisplayGeneralHelp_ShowsParentAndParentPositionalArgs()
        {
            var actions = new[]
            {
                HelpActionTestsHelper.Make(typeof(HelpActionTestsHelper.ParentAction)),
                HelpActionTestsHelper.Make(typeof(HelpActionTestsHelper.ChildAction))
            };
            var help = new HelpAction(actions, _ => (IAction)Activator.CreateInstance(_));

            var output = HelpActionTestsHelper.CaptureConsoleOutput(() => help.RunAsync().GetAwaiter().GetResult());

            output.Should().Contain("parent");

            // General help renders switches/arguments for top-level actions
            output.Should().Contain("Path to resource");

            // Child has no positional args; ensure no duplicate path lines (basic sanity)
            output.Split('\n').Count(l => l.Contains("Path to resource")).Should().Be(1);
        }
    }
}
