using System.Diagnostics;
using Azure.Functions.Cli.Diagnostics;
using Microsoft.Azure.WebJobs.Host;
using Xunit;

namespace Azure.Functions.Cli.Tests
{
    public class ConsoleTraceWriteTests
    {
        [Fact]
        public void ConsoleTraceWriteNullMessage()
        {
            var traceWrite = new ConsoleTraceWriter(TraceLevel.Verbose);
            traceWrite.Trace(new TraceEvent(TraceLevel.Info, null, null, null));
        }
    }
}
