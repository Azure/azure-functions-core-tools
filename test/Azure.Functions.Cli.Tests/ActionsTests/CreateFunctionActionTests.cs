using System.Threading.Tasks;
using Xunit;
using Xunit.Abstractions;

namespace Azure.Functions.Cli.Tests.ActionsTests
{
    public class CreateFunctionActionTests : ActionTestsBase
    {
        public CreateFunctionActionTests(ITestOutputHelper output) : base(output) { }

        [Theory]
        [InlineData("c#", "httpTrigger", "newFunc")]
        public static async Task CreateFunctionActionTest(string language, string templateName, string FunctionName)
        {
        }
    }
}
