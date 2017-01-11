using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
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
        public async Task CreateFunctionActionTest(string language, string templateName, string FunctionName)
        {
        }
    }
}
