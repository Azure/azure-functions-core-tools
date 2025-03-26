// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

namespace Func.E2ETests.Fixtures
{
    public class DotnetIsolatedFunctionAppFixture: BaseFunctionAppFixture
    {
        public DotnetIsolatedFunctionAppFixture() : base("dotnet-isolated")
        {
            UninstallDotnetTemplate("Microsoft.AzureFunctions.ProjectTemplate.CSharp.3.x");
        }
    }
}
