// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Reflection;
using Azure.Functions.Cli.Hosting.Dashboard;
using Spectre.Console;
using Xunit;

namespace Azure.Functions.Cli.Tests.Hosting.Dashboard;

public class FunctionPaletteTests
{
    [Fact]
    public void GetColorFor_IsStableAcrossCalls()
    {
        var palette = new FunctionPalette();

        var first = palette.GetColorFor("HttpTrigger1");
        var second = palette.GetColorFor("HttpTrigger1");

        Assert.Equal(first, second);
    }

    [Fact]
    public void GetColorFor_AssignsConsistentColor_ForSameName()
    {
        var a = new FunctionPalette();
        var b = new FunctionPalette();

        Assert.Equal(a.GetColorFor("Login"), b.GetColorFor("Login"));
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
        Assert.True(colors.Count >= 8, $"Expected at least 8 distinct colors, got {colors.Count}: {string.Join(",", colors)}");
    }

    [Fact]
    public void PaletteColors_AreValidSpectreColors()
    {
        string[] colors = GetPaletteColors();

        Assert.True(colors.Length >= 32, $"Expected at least 32 palette entries, got {colors.Length}.");
        Assert.Equal(colors.Length, colors.Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.DoesNotContain(colors, static color => color.Contains("red", StringComparison.OrdinalIgnoreCase));

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
            Assert.Equal("sample", writer.ToString());
        }
    }

    private static string[] GetPaletteColors()
    {
        FieldInfo field = typeof(FunctionPalette).GetField("_palette", BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new InvalidOperationException("FunctionPalette palette field was not found.");

        return (string[])field.GetValue(null)!;
    }
}
