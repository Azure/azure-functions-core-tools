using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Xunit.Abstractions;

namespace Cli.Core.E2E.Tests.Fixtures
{
    public class NodeV4FunctionAppFixture : BaseFunctionAppFixture
    {
        public NodeV4FunctionAppFixture() : base("node")
        {

        }
    }
}
