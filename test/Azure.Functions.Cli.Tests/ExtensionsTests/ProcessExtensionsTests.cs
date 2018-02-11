using System.Diagnostics;
using System.Threading.Tasks;
using FluentAssertions;
using Azure.Functions.Cli.Extensions;
using Xunit;

namespace Azure.Functions.Cli.Tests.ExtensionsTests
{
    public class ProcessExtensionsTests
    {
        [Fact]
        public async Task WaitForExitTest()
        {
            Process process = Process.Start("cmd");
            var calledContinueWith = false;

            process.WaitForExitAsync().ContinueWith(_ => {
                calledContinueWith = true;
            }).Ignore();

            process.Kill();
            for (var i = 0; !calledContinueWith && i < 5; i ++)
            {
                await Task.Delay(200);
            }
            calledContinueWith.Should().BeTrue(because: "the process should have exited and called the continuation");
        }
    }
}
