using System;
using System.Threading.Tasks;
using Azure.Functions.Cli.Tests.E2ETests.Helpers;
using Xunit;
using Xunit.Abstractions;

namespace Azure.Functions.Cli.Tests.E2ETests
{
    [Trait(TestTraits.Category, TestTraits.EndToEnd)]
    [Trait(TestTraits.Group, TestTraits.FuncListTests)]
    public class ListFunctionsTests : BaseE2ETest
    {
        public ListFunctionsTests(ITestOutputHelper output) : base(output) { }


        [SkippableFact]
        public async Task ListFunctionsWorks()
        {
            TestConditions.SkipIfAzureServicePrincipalNotDefined();

            Environment.SetEnvironmentVariable("CLI_DEBUG", "0");
            try
            {
                await CliTester.Run(new[] {
                    new RunConfiguration
                    {
                        Commands = new[]
                        {
                            "azure functionapp list-functions core-tools-list-functions",
                        },
                        OutputContains = new string[]
                        {
                            "api/httpwithkey",
                            "api/httpwithoutkey"
                        },
                        OutputDoesntContain = new string[]
                        {
                            "api/httpwithkey?code=",
                            "api/httpwithoutkey?code="
                        },
                        CommandTimeout = TimeSpan.FromMinutes(2)
                    },
                    new RunConfiguration
                    {
                        Commands = new []
                        {
                            "azure functionapp list-functions core-tools-list-functions --show-keys"
                        },
                        OutputContains = new string []
                        {
                            "api/httpwithkey?code=",
                            "api/httpwithoutkey"
                        },
                        OutputDoesntContain = new string[]
                        {
                            "api/httpwithoutkey?code="
                        },
                        CommandTimeout = TimeSpan.FromMinutes(2)
                    }
                });
            }
            finally
            {
                Environment.SetEnvironmentVariable("CLI_DEBUG", "1");
            }
        }
    }
}