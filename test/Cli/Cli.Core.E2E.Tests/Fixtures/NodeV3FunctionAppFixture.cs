using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Cli.Core.E2E.Tests.Fixtures
{
    public class NodeV3FunctionAppFixture: BaseFunctionAppFixture
    {
        public NodeV3FunctionAppFixture() : base("node", version: "v3")
        {

        }
    }
}
