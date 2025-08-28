// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Helpers;

namespace Azure.Functions.Cli.E2ETests.Fixtures
{
    public class DotnetIsolatedFunctionAppFixture : BaseFunctionAppFixture
    {
        public DotnetIsolatedFunctionAppFixture()
            : base(WorkerRuntime.DotnetIsolated)
        {
            var hiveRoot = Path.Combine(Path.GetTempPath(), "func-e2e-hives");
            Environment.SetEnvironmentVariable(DotnetHelpers.CustomHiveFlag, "1");
            Environment.SetEnvironmentVariable(DotnetHelpers.CustomHiveRoot, hiveRoot);
            Directory.CreateDirectory(hiveRoot);
        }
    }
}
