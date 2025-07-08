// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Helpers;

namespace Azure.Functions.Cli.E2ETests.Fixtures
{
    public class Dotnet6InProcFunctionAppFixture : BaseFunctionAppFixture
    {
        public Dotnet6InProcFunctionAppFixture()
            : base(WorkerRuntime.Dotnet, targetFramework: "net6.0")
        {
        }
    }
}
