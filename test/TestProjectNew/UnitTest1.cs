using Microsoft.Extensions.Logging;
using Moq;
using SampleTestingUpdate;
using System.Diagnostics;
using TestingFrameworkAgain;

namespace TestProjectNew
{
    public class UnitTest1
    {
        [Fact]
        public async void Test1()
        {
            var loggerFactory = new Mock<ILoggerFactory>();
            var logger = new Mock<ILogger>();

            string funcPath = "C:\\Users\\aibhandari\\azure-functions-core-tools\\test\\Azure.Functions.Cli.Tests\\bin\\Debug\\net8.0\\func.exe";
            string workingDirectory = "C:\\Users\\aibhandari\\IsolatedFunctionAppSample";

            // Set environent variables
            Environment.SetEnvironmentVariable("FUNC_PATH", funcPath);

            var funcStartCommand = new FuncStartCommand(logger.Object)
                .WithWorkingDirectory(workingDirectory);

            funcStartCommand.ProcessStartedHandler = async process =>
            {
                await ProcessHelper.WaitForFunctionHostToStart(process, 7071);

                using (var client = new HttpClient())
                {
                    var response = await client.GetAsync("http://localhost:7071/api/Function1");
                    var content = await response.Content.ReadAsStringAsync();

                    process.Kill();
                    // Do more async operations...

                }
            };

            string[] names = { "--verbose" };
            var result = funcStartCommand.Execute(names);
            result.Should().Pass();

        }
    }
}