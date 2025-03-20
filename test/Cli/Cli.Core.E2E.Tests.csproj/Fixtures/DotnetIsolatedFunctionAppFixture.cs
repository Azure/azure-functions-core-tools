using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Cli.Core.E2E.Tests.Fixtures
{
    public class DotnetIsolatedFunctionAppFixture: BaseFunctionAppFixture
    {
        public DotnetIsolatedFunctionAppFixture() : base("dotnet-isolated")
        {
            UninstallDotnetTemplate("Microsoft.AzureFunctions.ProjectTemplate.CSharp.3.x");
        }
    }
}
