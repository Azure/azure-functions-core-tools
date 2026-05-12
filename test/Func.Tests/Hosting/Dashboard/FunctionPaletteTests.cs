// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Hosting.Dashboard;
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

        // Hash + 8-slot palette must reach at least three distinct entries for
        // any reasonable input set; otherwise contrast suffers in the header.
        Assert.True(colors.Count >= 3, $"Expected at least 3 distinct colors, got {colors.Count}: {string.Join(",", colors)}");
    }
}
