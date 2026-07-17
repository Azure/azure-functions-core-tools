// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Reflection;
using Azure.Functions.Cli.Hosting.Dashboard;
using Spectre.Console;

namespace Azure.Functions.Cli.Tests.Hosting.Dashboard;

public class FunctionPaletteTests
{
    [Fact]
    public void GetColorFor_IsStableAcrossCalls()
    {
        var palette = new FunctionPalette();

        var first = palette.GetColorFor("HttpTrigger1");
        var second = palette.GetColorFor("HttpTrigger1");

        second.Should().Be(first);
    }

    [Fact]
    public void GetColorFor_AssignsConsistentColor_ForSameName()
    {
        var a = new FunctionPalette();
        var b = new FunctionPalette();

        b.GetColorFor("Login").Should().Be(a.GetColorFor("Login"));
    }

    [Fact]
    public void GetColorFor_ExercisesMultipleSlots()
    {
        var palette = new FunctionPalette();

        var colors = new[]
        {
            "Login", "Logout", "Refresh", "VerifyToken", "GetUser", "CreateUser",
            "UpdateUser", "DeleteUser", "ListUsers", "GetMe", "HealthcheckTimer",
            "ListOrders", "CancelOrder", "ProcessOrderQueue", "CreateOrder",
        }.Select(palette.GetColorFor).Distinct().ToList();

        // The expanded palette should provide useful variety for common apps.
        (colors.Count >= 8).Should().BeTrue($"Expected at least 8 distinct colors, got {colors.Count}: {string.Join(",", colors)}");
    }

    [Fact]
    public void PaletteColors_AreValidSpectreColors()
    {
        string[] colors = GetPaletteColors();

        (colors.Length >= 32).Should().BeTrue($"Expected at least 32 palette entries, got {colors.Length}.");
        colors.Distinct(StringComparer.OrdinalIgnoreCase).Count().Should().Be(colors.Length);
        colors.Should().NotContain(static color => color.Contains("red", StringComparison.OrdinalIgnoreCase));

        var writer = new StringWriter();
        IAnsiConsole console = AnsiConsole.Create(new AnsiConsoleSettings
        {
            Ansi = AnsiSupport.No,
            ColorSystem = ColorSystemSupport.NoColors,
            Interactive = InteractionSupport.No,
            Out = new AnsiConsoleOutput(writer),
        });

        foreach (string color in colors)
        {
            writer.GetStringBuilder().Clear();
            console.Write(new Markup($"[{color}]sample[/]"));
            writer.ToString().Should().Be("sample");
        }
    }

    private static string[] GetPaletteColors()
    {
        FieldInfo field = typeof(FunctionPalette).GetField("_palette", BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new InvalidOperationException("FunctionPalette palette field was not found.");

        return (string[])field.GetValue(null)!;
    }
}
