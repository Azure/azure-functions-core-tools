// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Workload.DotNet;
using Azure.Functions.Cli.Workloads;
using Microsoft.Extensions.DependencyInjection;

namespace Workload.DotNet.Tests;

public class DotNetWorkloadTests
{
    // Some sample tests just to fill out the test project a bit
    [Fact]
    public void DisplayName_ReturnsDotNet()
    {
        // Arrange
        var workload = new DotNetWorkload();

        // Act & Assert
        Assert.Equal(".NET", workload.DisplayName);
    }

    [Fact]
    public void Description_ReturnsCorrectValue()
    {
        // Arrange
        var workload = new DotNetWorkload();

        // Act & Assert
        Assert.Equal("func init / func new support for C# and F#.", workload.Description);
    }

    private sealed class TestBuilder(IServiceCollection services) : FunctionsCliBuilder
    {
        public override IServiceCollection Services { get; } = services;

        protected override void OnRegisterCommand(Func<IServiceProvider, FuncCommand> factory)
        {
            // Test implementation - no-op for basic service registration tests
        }
    }
}


