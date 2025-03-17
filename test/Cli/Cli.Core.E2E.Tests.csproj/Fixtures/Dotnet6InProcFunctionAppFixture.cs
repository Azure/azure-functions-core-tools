using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Cli.Core.E2E.Tests.Fixtures
{
    public class Dotnet6InProcFunctionAppFixture : BaseFunctionAppFixture
    {
        public Dotnet6InProcFunctionAppFixture() : base("dotnet", targetFramework: "net6.0")
        {

        }
    }
}
