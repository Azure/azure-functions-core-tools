// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

namespace Func.E2ETests.Fixtures
{
    public class Dotnet8InProcFunctionAppFixture : BaseFunctionAppFixture
    {
        public Dotnet8InProcFunctionAppFixture() : base("dotnet", targetFramework: "net8.0")
        {
            UninstallDotnetTemplate("Microsoft.AzureFunctions.ProjectTemplate.CSharp.Isolated.3.x");
        }
    }
}
