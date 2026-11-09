// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Text;
using System.Text.RegularExpressions;
using Azure.Functions.Cli.Console.Theme;
using Azure.Functions.Cli.Hosting.Dashboard;
using Azure.Functions.Cli.Hosting.Dashboard.Rendering;
using NSubstitute;
using Spectre.Console;
using Spectre.Console.Rendering;

namespace Azure.Functions.Cli.Tests.Hosting.Dashboard.Rendering;

public class CompactDashboardChromeTests
{
    [Theory]
    [InlineData(1)]
    [InlineData(3)]
    [InlineData(6)]
    [InlineData(12)]
    [InlineData(24)]
    [InlineData(40)]
    [InlineData(59)]
    [InlineData(60)]
    [InlineData(61)]
    [InlineData(80)]
    [InlineData(120)]
    [InlineData(240)]
    public void Footer_AllModesFitOneRowWithoutSplittingShortcuts(int width)
    {
        CompactFooterBuilder builder = new(new DefaultTheme(), Shortcuts());
        string[] completeShortcuts =
        [
            "q", "q quit", "Ctrl+C", "Ctrl+C stop", "Ctrl+C force exit", "? help",
            "Ctrl+C copy", "Esc clear", "Esc close", "Enter filter",
            "↑/↓ select", "type query", "↑/↓ navigate", "f next", "/ search",
            "?/Esc close", "m", "m return", "m return to TUI", "↑/↓ scroll", "t functions", "PgUp/PgDn page",
            "a all", "End live",
        ];

        foreach (CompactFooterMode mode in Enum.GetValues<CompactFooterMode>())
        {
            string[] lines = Lines(builder.Build(width, mode, following: false, filtered: true), 1000);
            lines.Should().ContainSingle();
            LogText.Measure(lines[0]).Should().Be(width, $"mode {mode} must own exactly one {width}-cell row");
            string[] shortcuts = [.. Regex.Split(lines[0].Trim(), " {2,}").Where(value => value.Length > 0)];
            shortcuts.Should().OnlyContain(value => completeShortcuts.Contains(value));

            if (mode is CompactFooterMode.Search or CompactFooterMode.Stopping or CompactFooterMode.MouseReleased)
            {
                if (width >= 6)
                {
                    lines[0].Should().Contain("Ctrl+C");
                }
                lines[0].Should().NotContain("q quit");
                if (mode != CompactFooterMode.MouseReleased)
                {
                    lines[0].Should().NotContain("? help");
                }
            }
            else
            {
                lines[0].TrimEnd().Should().EndWith(width >= 6 ? "q quit" : "q");
            }
        }
    }

    [Theory]
    [InlineData(40)]
    [InlineData(60)]
    [InlineData(80)]
    [InlineData(120)]
    public void Footer_SelectionKeepsCopyClearHelpAndQuitVisible(int width)
    {
        CompactFooterBuilder builder = new(new DefaultTheme(), Shortcuts());

        string row = Lines(builder.Build(width, CompactFooterMode.Selection), 1000).Single();

        row.Should().Contain("Ctrl+C copy").And.Contain("Esc clear").And.Contain("? help").And.EndWith("q quit");
        row.Should().NotContain("Ctrl+C stop");
    }

    [Fact]
    public void Footer_ContextsAdvertiseOnlyApplicableActions()
    {
        CompactFooterBuilder builder = new(new DefaultTheme(), Shortcuts());

        string search = Lines(builder.Build(80, CompactFooterMode.Search), 1000).Single();
        search.Should().Contain("Esc close").And.Contain("Enter filter").And.Contain("↑/↓ select").And.EndWith("Ctrl+C stop");
        search.Should().NotContain("? help").And.NotContain("q quit").And.NotContain("Ctrl+C copy");

        string expired = Lines(builder.Build(40, CompactFooterMode.ExpiredSelection), 1000).Single();
        expired.Should().Contain("Esc clear").And.EndWith("q quit");
        expired.Should().NotContain("Ctrl+C");

        string released = Lines(builder.Build(40, CompactFooterMode.MouseReleased), 1000).Single();
        released.Should().Contain("m return to TUI").And.EndWith("Ctrl+C stop");

        string stopping = Lines(builder.Build(24, CompactFooterMode.Stopping), 1000).Single();
        stopping.Trim().Should().Be("Ctrl+C force exit");
    }

    [Fact]
    public void Footer_PausedFilteredViewPrioritizesRecoveryActions()
    {
        CompactFooterBuilder builder = new(new DefaultTheme(), Shortcuts());

        string row = Lines(builder.Build(40, CompactFooterMode.Logs, following: false, filtered: true), 1000).Single();

        row.Should().Contain("End live").And.Contain("a all").And.Contain("? help").And.EndWith("q quit");
    }

    [Fact]
    public void Footer_TerminalSelectionIsNotAdvertisedOutsideItsActiveMode()
    {
        CompactFooterBuilder builder = new(new DefaultTheme(), Shortcuts());
        foreach (CompactFooterMode mode in Enum.GetValues<CompactFooterMode>().Where(mode => mode != CompactFooterMode.MouseReleased))
        {
            string row = Lines(builder.Build(240, mode), 1000).Single();

            row.Should().NotMatchRegex(@"(^|\s)m(\s|$)").And.NotContain("native copy").And.NotContain("terminal selection");
        }
    }

    [Theory]
    [InlineData(24)]
    [InlineData(40)]
    [InlineData(80)]
    [InlineData(120)]
    public void Footer_TerminalSelectionKeepsReturnAndStopVisible(int width)
    {
        CompactFooterBuilder builder = new(new DefaultTheme(), Shortcuts());

        string row = Lines(builder.Build(width, CompactFooterMode.MouseReleased), 1000).Single();

        row.Should().Contain(width >= 28 ? "m return to TUI" : "m return").And.EndWith("Ctrl+C stop");
        row.Should().NotContain("Ctrl+C copy").And.NotContain("m resume");
    }

    [Theory]
    [InlineData(40)]
    [InlineData(60)]
    [InlineData(80)]
    [InlineData(120)]
    public void LogDivider_FitsTheReservedRowAndPreservesUnicode(int width)
    {
        CompactHeaderBuilder builder = new(new DefaultTheme(), new DashboardRunInfo());
        string name = string.Concat(Enumerable.Repeat("\u754c\U0001f600e\u0301[red]", 40));

        string[] lines = Lines(builder.BuildLogDivider(width, ["Paused", "24 new", "Filter " + name]), width);

        lines.Should().ContainSingle();
        lines[0].Should().Contain("Logs").And.Contain("Paused").And.Contain("24 new").And.Contain("─");
        LogText.Measure(lines[0]).Should().BeLessThanOrEqualTo(width);
        lines[0].Should().NotContain("\ufffd").And.NotContain("\n");
        Action encode = () => new UTF8Encoding(false, true).GetBytes(lines[0]);
        encode.Should().NotThrow();
    }

    [Theory]
    [InlineData(24)]
    [InlineData(40)]
    [InlineData(80)]
    [InlineData(120)]
    public void TerminalSelectionDivider_DistinguishesTheRunningHostFromPausedDisplay(int width)
    {
        CompactHeaderBuilder builder = new(new DefaultTheme(), new DashboardRunInfo());

        string[] lines = Lines(builder.BuildLogDivider(width,
            ["host running", "display paused", "use terminal Copy"], "Terminal selection"), width);

        lines.Should().ContainSingle();
        lines[0].Should().Contain("Terminal selection");
        LogText.Measure(lines[0]).Should().BeLessThanOrEqualTo(width);
        if (width >= 40)
        {
            lines[0].Should().Contain("host running");
        }
        if (width >= 80)
        {
            lines[0].Should().Contain("display paused").And.Contain("use terminal Copy");
        }
    }

    [Fact]
    public void Banner_TotalsUseTheExistingBorderWithoutAddingRows()
    {
        CompactHeaderBuilder builder = new(new DefaultTheme(), new DashboardRunInfo());

        string[] lines = Lines(builder.BuildBanner(new DashboardState().Snapshot(), 120), 120);

        lines.Should().HaveCount(3);
        lines[0].Should().Contain("0 functions").And.Contain("0 invocations").And.Contain("0 errors");
    }

    [Fact]
    public void Help_ShowsVersionAndFullCopyFailureDetails()
    {
        const string version = "5.0.0-test-chrome";
        const string status = "Clipboard access was denied by the operating system. The selection is retained.";
        CompactHelpOverlayBuilder builder = new(new DefaultTheme(), Shortcuts(), new DashboardRunInfo(version));

        string output = string.Join("\n", Lines(builder.Build(120, status), 120));

        output.Should().Contain(version).And.Contain(status).And.Contain("Available compact-mode controls");
        output.Should().Contain("Advanced: terminal selection").And.Contain("terminal's Copy command");
    }

    [Fact]
    public void Builders_RejectInvalidArguments()
    {
        ITheme theme = new DefaultTheme();
        CompactDashboardShortcutLabels shortcuts = Shortcuts();
        FluentActions.Invoking(() => new CompactFooterBuilder(null!, shortcuts)).Should().Throw<ArgumentNullException>();
        FluentActions.Invoking(() => new CompactFooterBuilder(theme, null!)).Should().Throw<ArgumentNullException>();
        CompactFooterBuilder footer = new(theme, shortcuts);
        FluentActions.Invoking(() => footer.Build(0, CompactFooterMode.Logs)).Should().Throw<ArgumentOutOfRangeException>();
        FluentActions.Invoking(() => footer.Build(80, (CompactFooterMode)(-1))).Should().Throw<ArgumentOutOfRangeException>();
        CompactHeaderBuilder header = new(theme, new DashboardRunInfo());
        FluentActions.Invoking(() => header.BuildLogDivider(0, [])).Should().Throw<ArgumentOutOfRangeException>();
        FluentActions.Invoking(() => header.BuildLogDivider(80, null!)).Should().Throw<ArgumentNullException>();
        FluentActions.Invoking(() => header.BuildLogDivider(80, [], " ")).Should().Throw<ArgumentException>();
        FluentActions.Invoking(() => header.BuildBanner(null!, 80)).Should().Throw<ArgumentNullException>();
    }

    private static CompactDashboardShortcutLabels Shortcuts()
        => new(Substitute.For<IPlatform>());

    private static string[] Lines(IRenderable renderable, int width)
        => [.. Segment.SplitLines(renderable.Render(LogText.CreateRenderOptions(width, 40), width))
            .Select(line => string.Concat(line.Where(segment => !segment.IsControlCode).Select(segment => segment.Text)))];
}
