using Azure.Functions.Cli.Common;
using Microsoft.Azure.WebJobs.Script;
using Microsoft.Extensions.Logging;
using System.IO;
using Xunit;

namespace Azure.Functions.Cli.Tests
{
    public class UtilitiesTests
    {
        private string _workerDir;
        private ScriptApplicationHostOptions _hostOptions;
        private string _hostJsonFilePath;

        public UtilitiesTests()
        {
            try
            {
                _workerDir = Path.GetTempFileName();
                if (File.Exists(_workerDir))
                {
                    File.Delete(_workerDir);
                }
                else if (Directory.Exists(_workerDir))
                {
                    Directory.Delete(_workerDir, recursive: true);
                }
                Directory.CreateDirectory(_workerDir);
                _hostOptions = new ScriptApplicationHostOptions
                {
                    ScriptPath = _workerDir
                };
            }
            catch
            {
                _hostOptions = new ScriptApplicationHostOptions
                {
                    ScriptPath = Directory.GetCurrentDirectory()
                };
            }
            _hostJsonFilePath = Path.Combine(_hostOptions.ScriptPath, Constants.HostJsonFileName);
        }

        [Theory]
        [InlineData("{\"version\": \"2.0\",\"Logging\": {\"LogLevel\": {\"Default\": \"None\"}}}", LogLevel.None)]
        [InlineData("{\"version\": \"2.0\",\"logging\": {\"logLevel\": {\"Default\": \"NONE\"}}}", LogLevel.None)]
        [InlineData("{\"version\": \"2.0\",\"logging\": {\"logLevel\": {\"Default\": \"Debug\"}}}", LogLevel.Debug)]
        [InlineData("{\"version\": \"2.0\"}", LogLevel.Information)]
        public void GetHostJsonDefaultLogLevel_Test(string hostJsonContent, LogLevel expectedLogLevel)
        {
            try
            {
                FileSystemHelpers.WriteAllTextToFile(_hostJsonFilePath, hostJsonContent);
                var configuration = Utilities.BuildHostJsonConfigutation(_hostOptions);
                LogLevel actualLogLevel = Utilities.GetHostJsonDefaultLogLevel(configuration);
                Assert.Equal(actualLogLevel, expectedLogLevel);
            }
            finally
            {
                DeleteIfExists(_workerDir);
            }
        }

        [Theory]
        [InlineData("{\"version\": \"2.0\",\"Logging\": {\"LogLevel\": {\"Host.General\": \"Debug\"}}}", "Host.General", true)]
        [InlineData("{\"version\": \"2.0\",\"Logging\": {\"LogLevel\": {\"Host.Startup\": \"Debug\"}}}", "Host.General", false)]
        [InlineData("{\"version\": \"2.0\"}", "Function.HttpFunction", false)]
        public void LogLevelExists_Test(string hostJsonContent, string category, bool expected)
        {
            try
            {
                FileSystemHelpers.WriteAllTextToFile(_hostJsonFilePath, hostJsonContent);

                var configuration = Utilities.BuildHostJsonConfigutation(_hostOptions);
                Assert.Equal(expected, Utilities.LogLevelExists(configuration, category));
            }
            finally
            {
                DeleteIfExists(_workerDir);
            }
        }

        [Theory]
        [InlineData("{\"version\": \"2.0\",\"extensionBundle\": { \"id\": \"Microsoft.Azure.Functions.ExtensionBundle\",\"version\": \"[1.*, 2.0.0)\"}}", "extensionBundle", true)]
        [InlineData("{\"version\": \"2.0\",\"extensionBundle\": { \"id\": \"Microsoft.Azure.Functions.ExtensionBundle\",\"version\": \"[1.*, 2.0.0)\"}}", "ExtensionBundle", true)]
        [InlineData("{\"version\": \"2.0\"}", "extensionBundle", false)]
        public void JobHostConfigSectionExists_Test(string hostJsonContent, string section, bool expected)
        {
            try
            {
                FileSystemHelpers.WriteAllTextToFile(_hostJsonFilePath, hostJsonContent);
                var configuration = Utilities.BuildHostJsonConfigutation(_hostOptions);
                Assert.Equal(expected, Utilities.JobHostConfigSectionExists(configuration, section));
            }
            finally
            {
                DeleteIfExists(_workerDir);
            }
        }

        [Theory]
        [InlineData(LogLevel.None, false)]
        [InlineData(LogLevel.Debug, true)]
        [InlineData(LogLevel.Information, true)]
        public void UserLoggingFilter_Test(LogLevel inputLogLevel, bool expected)
        {
            Assert.Equal(expected, Utilities.UserLoggingFilter(inputLogLevel));
        }

        [Theory]
        [InlineData("Function.Function1", LogLevel.None, true)]
        [InlineData("Function.Function1", LogLevel.Warning, true)]
        [InlineData("Function.Function1.User", LogLevel.Information, true)]
        [InlineData("Host.General", LogLevel.Information, false)]
        [InlineData("Host.Startup", LogLevel.Error, true)]
        [InlineData("Host.General", LogLevel.Warning, true)]
        public void DefaultLoggingFilter_Test(string inputCategory, LogLevel inputLogLevel, bool expected)
        {
            Assert.Equal(expected, Utilities.DefaultLoggingFilter(inputCategory, inputLogLevel, LogLevel.Information, LogLevel.Warning));
        }

        private void DeleteIfExists(string filePath)
        {
            try
            {
                if (File.Exists(filePath))
                {
                    File.Delete(filePath);
                }
                else if (Directory.Exists(filePath))
                {
                    Directory.Delete(filePath, recursive: true);
                }
            }
            catch
            {
            }
        }
    }
}
