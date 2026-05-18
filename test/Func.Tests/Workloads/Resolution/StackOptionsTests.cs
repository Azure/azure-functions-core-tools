// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Workloads.Resolution;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;

namespace Azure.Functions.Cli.Tests.Workloads.Resolution;

public sealed class StackOptionsTests
{
    [Fact]
    public void IConfigureOptions_Pipeline_LastRegisteredWins()
    {
        // Simulates the production wiring order: LocalSettings first, then
        // FuncProject. The project config override must win.
        var services = new ServiceCollection();
        services.AddOptions<StackOptions>();
        services.AddSingleton<IConfigureOptions<StackOptions>>(new SetStack("python"));
        services.AddSingleton<IConfigureOptions<StackOptions>>(new SetStack("dotnet"));

        using ServiceProvider sp = services.BuildServiceProvider();
        StackOptions options = sp.GetRequiredService<IOptions<StackOptions>>().Value;

        Assert.Equal("dotnet", options.Stack);
    }

    [Fact]
    public void IConfigureOptions_SkipsWhenUnset()
    {
        var services = new ServiceCollection();
        services.AddOptions<StackOptions>();
        services.AddSingleton<IConfigureOptions<StackOptions>>(new SetStack("node"));
        services.AddSingleton<IConfigureOptions<StackOptions>>(new SetStack(value: null));

        using ServiceProvider sp = services.BuildServiceProvider();
        StackOptions options = sp.GetRequiredService<IOptions<StackOptions>>().Value;

        Assert.Equal("node", options.Stack);
    }

    private sealed class SetStack(string? value) : IConfigureOptions<StackOptions>
    {
        public void Configure(StackOptions options)
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                options.Stack = value;
            }
        }
    }
}
