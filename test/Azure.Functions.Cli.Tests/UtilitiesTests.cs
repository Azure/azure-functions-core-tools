using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Text;
using Xunit;

namespace Azure.Functions.Cli.Tests
{
    public class UtilitiesTests
    {
        [Theory]
        [InlineData("{\"version\": \"2.0\",\"Logging\": {\"LogLevel\": {\"Default\": \"None\"}}}", LogLevel.None)]
        [InlineData("{\"version\": \"2.0\",\"logging\": {\"logLevel\": {\"Default\": \"NONE\"}}}", LogLevel.None)]
        [InlineData("{\"version\": \"2.0\",\"logging\": {\"logLevel\": {\"Default\": \"Debug\"}}}", LogLevel.Debug)]
        [InlineData("{\"version\": \"2.0\"}", LogLevel.Information)]
        public void GetHostJsonDefaultLogLevel_Test(string hostJsonContent, LogLevel expectedLogLevel)
        {
            LogLevel actualLogLevel = Utilities.GetHostJsonDefaultLogLevel(hostJsonContent);
            Assert.Equal(actualLogLevel, expectedLogLevel);
        }
    }
}
