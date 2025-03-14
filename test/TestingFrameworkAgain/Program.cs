using Microsoft.Extensions.Logging;
using Moq;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SampleTestingUpdate
{
    internal class Program
    {
        internal static void Main(string[] args)
        {
            var loggerFactory = new Mock<ILoggerFactory>();
            var logger = new Mock<ILogger>();

            string funcPath = "C:\\Users\\aibhandari\\azure-functions-core-tools\\test\\Azure.Functions.Cli.Tests\\bin\\Debug\\net8.0\\func.exe";
            string workingDirectory = "C:\\Users\\aibhandari\\IsolatedFunctionAppSample";

            // Set environent variables
            Environment.SetEnvironmentVariable("FUNC_PATH", funcPath);
            Environment.SetEnvironmentVariable("WORKING_DIRECTORY", workingDirectory);

            var funcStartCommand = new FuncStartCommand(logger.Object)
                    .WithWorkingDirectory(workingDirectory);

            string[] names = { "--verbose" };
            funcStartCommand.Execute(names);

        }
    }
}
