using Azure.Functions.Cli.Tests.E2E.Helpers;
using System.Threading.Tasks;
using Xunit;
using Xunit.Abstractions;

namespace Azure.Functions.Cli.Tests.E2E
{
    public class CreateFunctionTests : BaseE2ETest
    {
        public CreateFunctionTests(ITestOutputHelper output) : base(output) { }

        [Fact]
        public async Task create_template_function_success_message()
        {
            await CliTester.Run(new RunConfiguration
            {
                Commands = new[]
                {
                    "init . --worker-runtime dotnet --no-source-control",
                    "new --template HttpTrigger --name testfunc"
                },
                OutputContains = new[]
                {
                    "The function \"testfunc\" was created successfully from the \"HttpTrigger\" template."
                }
            }, _output);
        }
    }
}
