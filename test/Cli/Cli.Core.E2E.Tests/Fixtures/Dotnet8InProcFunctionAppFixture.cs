using FluentAssertions;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TestFramework.Commands;
using TestFramework.Helpers;
using Xunit.Abstractions;

namespace Cli.Core.E2E.Tests.Fixtures
{
    public class Dotnet8InProcFunctionAppFixture: BaseFunctionAppFixture
    {
        public Dotnet8InProcFunctionAppFixture() : base("dotnet", targetFramework: "net8.0")
        {
            UninstallDotnetTemplate("Microsoft.AzureFunctions.ProjectTemplate.CSharp.Isolated.3.x");
        }
    }
}
