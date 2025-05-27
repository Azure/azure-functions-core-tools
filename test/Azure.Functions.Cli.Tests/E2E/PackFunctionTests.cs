using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Tests.E2E.Helpers;
using System;
using System.IO;
using System.Threading.Tasks;
using Xunit;
using Xunit.Abstractions;

namespace Azure.Functions.Cli.Tests.E2E
{
    public class PackFunctionTests : BaseE2ETest
    {
        public PackFunctionTests(ITestOutputHelper output) : base(output) { }

        /// <summary>
        /// This test has been migrated to the new test framework.
        /// See test/Cli/Func.E2E.Tests/Commands/FuncPack/PythonPackTests.cs
        /// </summary>
        [Fact(Skip = "Migrated to new test framework")]
        public Task pack_python_from_cache()
        {
            return Task.CompletedTask;
        }

    }
}
