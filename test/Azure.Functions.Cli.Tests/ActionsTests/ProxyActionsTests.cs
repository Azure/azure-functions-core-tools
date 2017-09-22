using System.IO;
using System.Linq;
using Azure.Functions.Cli.Common;
using FluentAssertions;
using Xunit;
using Xunit.Abstractions;
using NSubstitute;
using System.Text;
using Colors.Net;

namespace Azure.Functions.Cli.Tests.ActionsTests
{
    public class ProxyActionsTests : ActionTestsBase
    {
        public ProxyActionsTests(ITestOutputHelper output) : base(output)
        {
        }

        [Theory]
        [InlineData("proxy create --name MyProxy --route MyRoute")]
        [InlineData("proxy create -n MyProxy2 -r MyRoute2")]
        [InlineData("proxy new -n MyProxy3 -r MyRoute3")]
        [InlineData("proxy create --name MyProxy4 --route MyRoute4 --methods GET --backend-url http://httpbin.org/ip  ")]
        [InlineData("proxy create --name MyProxy5 --route MyRoute5 --methods GET POST --backend-url http://httpbin.org/ip ")]
        [InlineData("proxy create --name MyProxy6 --template SampleProxy")]
        [InlineData("proxy create --name MyProxy7 --route '{*path}' --methods GET POST --backend-url \"http://%REMOTE_HOST%/{path}\" ")]
        [InlineData("proxy create --name MyProxy8 --route '/MyRoute8' --methods GET --backend-url 'http://httpbin.org/ip'  ")]
        public void AddProxyActionTest(string args)
        {
            // Setup
            Program.Main(new[] { "init" });

            // Test
            string[] arguments = args.Split(' ').ToArray();
            var proxyName = arguments[3];

            Program.Main(arguments);

            var content = File.ReadAllText(Path.Combine(WorkingDirectory, Constants.ProxiesFileName));

            // Assert
            content.Should().Contain(proxyName);
            content.Should().NotContain("'");

            // cleanup
            CleanUp();
        }

        [Fact]
        public void DeleteProxyActionTest()
        {
            // Setup
            Program.Main(new[] { "init" });
            Program.Main("proxy create --name MyProxy --route MyRoute".Split(' ').ToArray());
            Program.Main("proxy create --name MyProxy2 --route MyRoute2".Split(' ').ToArray());

            // Test
            Program.Main("proxy delete --name MyProxy2".Split(' ').ToArray());

            var content = File.ReadAllText(Path.Combine(WorkingDirectory, Constants.ProxiesFileName));

            // Assert
            content.Should().Contain("MyProxy");
            content.Should().NotContain("MyProxy2");

            // cleanup
            CleanUp();
        }

        [Fact]
        public void ShowProxyActionTest()
        {
            var console = Substitute.For<IConsoleWriter>();
            var stringBuilder = new StringBuilder();
            console.WriteLine(Arg.Do<object>(o => stringBuilder.AppendLine(o?.ToString()))).Returns(console);
            console.Write(Arg.Do<object>(o => stringBuilder.Append(o.ToString()))).Returns(console);
            ColoredConsole.Out = console;

            // Setup
            Program.Main(new[] { "init" });
            Program.Main("proxy create --name MyProxy --route MyRoute".Split(' ').ToArray());

            // Test
            stringBuilder.Clear();
            Program.Main("proxy show --name MyProxy".Split(' ').ToArray());

            // Assert
            var output = stringBuilder.ToString();
            output.Should().Contain("MyRoute");

            // cleanup
            CleanUp();
        }
    }
}