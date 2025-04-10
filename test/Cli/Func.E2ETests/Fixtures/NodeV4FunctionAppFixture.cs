// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

namespace Func.E2ETests.Fixtures
{
    public class NodeV4FunctionAppFixture : BaseFunctionAppFixture
    {
        public NodeV4FunctionAppFixture() : base("node")
        {
            Environment.SetEnvironmentVariable("FUNCTIONS_WORKER_RUNTIME", "node");
        }
    }
}
