// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Helpers;

namespace Azure.Functions.Cli.E2ETests.Fixtures
{
    public class NodeV4FunctionAppFixture : BaseFunctionAppFixture
    {
        public NodeV4FunctionAppFixture()
            : base(WorkerRuntime.Node, version: "v4")
        {
        }
    }
}
