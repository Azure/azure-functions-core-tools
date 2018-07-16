using Xunit.Abstractions;

namespace Azure.Functions.Cli.Tests.E2E
{
    public abstract class BaseE2ETest
    {
        protected readonly ITestOutputHelper _output;

        public BaseE2ETest(ITestOutputHelper output)
        {
            _output = output;
        }
    }
}