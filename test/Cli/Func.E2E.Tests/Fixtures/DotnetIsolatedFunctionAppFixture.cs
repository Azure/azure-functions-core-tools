// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Helpers;

namespace Azure.Functions.Cli.E2E.Tests.Fixtures
{
    public class DotnetIsolatedFunctionAppFixture : BaseFunctionAppFixture
    {
        public DotnetIsolatedFunctionAppFixture()
            : base(WorkerRuntime.DotnetIsolated)
        {
        }
    }
}
