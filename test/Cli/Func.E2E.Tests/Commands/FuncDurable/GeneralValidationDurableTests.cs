// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.E2E.Tests.Fixtures;
using Azure.Functions.Cli.TestFramework.Assertions;
using Azure.Functions.Cli.TestFramework.Commands;
using Xunit;
using Xunit.Abstractions;

namespace Azure.Functions.Cli.E2E.Tests.Commands.FuncDurable
{
    public class GeneralValidationDurableTests : IClassFixture<DurableFunctionAppFixture>
    {
        private readonly DurableFunctionAppFixture _fixture;

        public GeneralValidationDurableTests(DurableFunctionAppFixture fixture, ITestOutputHelper log)
        {
            _fixture = fixture;
            _fixture.Log = log;
        }

        [Theory]
        [InlineData("Netherite")]
        [InlineData("MSSQL")]
        public void Durable_WithAlternateBackends_DisplaysNotSupportedError(string providerType)
        {
            var hostJsonContent = $@"{{""extensions"":{{""durableTask"":{{""storageProvider"":{{""type"":""{providerType}""}}}}}},""version"": ""2.0""}}";
            File.WriteAllText(Path.Combine(_fixture.WorkingDirectory, "host.json"), hostJsonContent);

            var funcDurableCommand = new FuncDurableCommand(_fixture.FuncPath, nameof(Durable_WithAlternateBackends_DisplaysNotSupportedError), _fixture.Log);
            var result = funcDurableCommand
                .WithWorkingDirectory(_fixture.WorkingDirectory)
                .Execute(["get-instances"]);

            result.Should().ExitWith(1);
            result.Should().HaveStdErrContaining($"The {providerType} storage provider for Durable Functions is not yet supported by this command. However, it may be supported by an SDK API or an HTTP API. To learn about alternate ways issue commands for Durable Functions, see https://aka.ms/durable-functions-instance-management.");
        }
    }
}
