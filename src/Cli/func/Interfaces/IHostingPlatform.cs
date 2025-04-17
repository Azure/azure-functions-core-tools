// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

namespace Azure.Functions.Cli.Interfaces
{
    public interface IHostingPlatform
    {
        internal Task DeployContainerizedFunction(string functionName, string image, string nameSpace, int min, int max, double cpu, int memory, string port, string pullSecret);
    }
}
