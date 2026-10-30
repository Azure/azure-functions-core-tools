// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Console;
using NSubstitute;

namespace Azure.Functions.Cli.Tests.Console;

public sealed class UnexpectedErrorReporterTests
{
    [Fact]
    public void Report_EmptyMessage_UsesExceptionTypeWithoutExposingStack()
    {
        var interaction = Substitute.For<IInteractionService>();
        var reporter = new UnexpectedErrorReporter(interaction);
        var exception = new FileNotFoundException(string.Empty, "System.Diagnostics.Process");

        reporter.Report(exception, verbose: false);

        interaction.Received().WriteError("An unexpected error occurred: FileNotFoundException");
        interaction.DidNotReceive().WriteHint(Arg.Any<string>());
    }

    [Fact]
    public void Report_Verbose_IncludesExceptionTypeFileNameAndInnerException()
    {
        var interaction = Substitute.For<IInteractionService>();
        var reporter = new UnexpectedErrorReporter(interaction);
        var exception = new FileNotFoundException(string.Empty, "System.Diagnostics.Process", new InvalidOperationException("inner"));

        reporter.Report(exception, verbose: true);

        interaction.Received().WriteHint(Arg.Is<string>(message =>
            message.Contains("FileNotFoundException", StringComparison.Ordinal)
            && message.Contains("System.Diagnostics.Process", StringComparison.Ordinal)
            && message.Contains("inner", StringComparison.Ordinal)));
    }

    [Fact]
    public void Report_NullException_Throws()
    {
        var reporter = new UnexpectedErrorReporter(Substitute.For<IInteractionService>());

        Action action = () => reporter.Report(null!, verbose: true);

        action.Should().Throw<ArgumentNullException>().WithParameterName("exception");
    }

    [Fact]
    public void Constructor_NullInteraction_Throws()
    {
        Action action = () => _ = new UnexpectedErrorReporter(null!);

        action.Should().Throw<ArgumentNullException>().WithParameterName("interaction");
    }
}
