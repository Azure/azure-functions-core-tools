// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.CommandLine;
using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Workloads;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Azure.Functions.Cli.Workload.DotNet.Tests;

public class DotNetProjectInitializerTests
{
    [Fact]
    public void Configure_RegistersProjectInitializer()
    {
        // Arrange
        var services = new ServiceCollection();
        var builder = new TestBuilder(services);
        var workload = new DotNetWorkload();

        // Act
        workload.Configure(builder);

        // Assert
        var serviceDescriptor = services.FirstOrDefault(sd => sd.ServiceType == typeof(IProjectInitializer));
        Assert.NotNull(serviceDescriptor);
        Assert.Equal(typeof(DotNetProjectInitializer), serviceDescriptor.ImplementationType);
    }

    [Fact]
    public void Stack_ReturnsDotnet()
    {
        // Arrange
        var initializer = new DotNetProjectInitializer();

        // Act & Assert
        Assert.Equal("dotnet", initializer.Stack);
    }

    [Fact]
    public void SupportedLanguages_ReturnsCSharpAndFSharp()
    {
        // Arrange
        var initializer = new DotNetProjectInitializer();

        // Act
        var languages = initializer.SupportedLanguages;

        // Assert
        Assert.NotNull(languages);
        Assert.Equal(2, languages.Count);
        Assert.Contains("C#", languages);
        Assert.Contains("F#", languages);
    }

    [Fact]
    public async Task InitializeAsync_ThrowsNotImplementedException()
    {
        // Arrange
        var initializer = new DotNetProjectInitializer();
        var workingDir = WorkingDirectory.FromCwd();
        var context = new InitContext(workingDir, "TestProject", "C#", Force: false);
        var command = new RootCommand();
        var parseResult = command.Parse("");

        // Act & Assert
        await Assert.ThrowsAsync<NotImplementedException>(
            () => initializer.InitializeAsync(context, parseResult));
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
