// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

namespace Func.E2ETests.Fixtures
{
    public class NodeV3FunctionAppFixture : BaseFunctionAppFixture
    {
        public NodeV3FunctionAppFixture() : base("node", version: "v3")
        {
        }
    }
}
