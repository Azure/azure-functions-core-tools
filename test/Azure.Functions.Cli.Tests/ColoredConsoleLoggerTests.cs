using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Diagnostics;
using Microsoft.Extensions.Configuration;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using Xunit;

namespace Azure.Functions.Cli.Tests
{
    public class ColoredConsoleLoggerTests
    {
        [Theory(Skip = "https://github.com/Azure/azure-functions-core-tools/issues/2174")]
        [InlineData("somelog", false)]
        [InlineData("Worker process started and initialized.", true)]
        [InlineData("Worker PROCESS started and initialized.", true)]
        [InlineData("Worker process started.", false)]
        [InlineData("Host lock lease acquired by instance ID", true)]
        [InlineData("Host lock lease acquired by instance id", true)]
        [InlineData("Host lock lease", false)]
        public async Task DoesMessageStartsWithWhiteListedPrefix_Tests(string formattedMessage, bool expected)
        {
            string defaultJson = "{\"version\": \"2.0\",\"Logging\": {\"LogLevel\": {\"Host.Startup\": \"Debug\"}}}";
            await FileSystemHelpers.WriteToFile("host.json", new MemoryStream(Encoding.ASCII.GetBytes(defaultJson)));
            var testConfiguration = new ConfigurationBuilder().AddJsonFile("host.json").Build();
            ColoredConsoleLogger coloredConsoleLogger = new ColoredConsoleLogger("test", new LoggingFilterHelper(testConfiguration, true));
            Assert.Equal(expected, coloredConsoleLogger.DoesMessageStartsWithAllowedLogsPrefix(formattedMessage));
        }

        [Theory(Skip = "https://github.com/Azure/azure-functions-core-tools/issues/2174")]
        [InlineData("somelog", false)]
        [InlineData("Worker process started and initialized.", true)]
        [InlineData("Worker PROCESS started and initialized.", true)]
        [InlineData("Worker process started.", false)]
        [InlineData("Host lock lease acquired by instance ID", true)]
        [InlineData("Host lock lease acquired by instance id", true)]
        [InlineData("Host lock lease", false)]
        public async Task IsEnabled_Tests(string formattedMessage, bool expected)
        {
            string defaultJson = "{\"version\": \"2.0\",\"Logging\": {\"LogLevel\": {\"Host.Startup\": \"Debug\"}}}";
            await FileSystemHelpers.WriteToFile("host.json", new MemoryStream(Encoding.ASCII.GetBytes(defaultJson)));
            var testConfiguration = new ConfigurationBuilder().AddJsonFile("host.json").Build();
            ColoredConsoleLogger coloredConsoleLogger = new ColoredConsoleLogger("test", new LoggingFilterHelper(testConfiguration, true));
            Assert.Equal(expected, coloredConsoleLogger.DoesMessageStartsWithAllowedLogsPrefix(formattedMessage));
        }
    }
}
