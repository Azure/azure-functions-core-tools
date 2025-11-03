// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

namespace Azure.Functions.Cli.Common
{
    internal static class DotnetConstants
    {
        public const string WindowsExecutableName = "func.exe";
        public const string LinuxExecutableName = "func";
        public const string InProc8DirectoryName = "in-proc8";
        public const string InProc6DirectoryName = "in-proc6";
        public const string InProc8HostRuntime = "inproc8";
        public const string InProc6HostRuntime = "inproc6";

        public const string InProcFunctionsSdk = "Microsoft.NET.Sdk.Functions";
        public const string InProcFunctionsMinSdkVersion = "4.5.0";
        public const string InProcFunctionsDocsLink = "https://aka.ms/functions-core-tools-in-proc-sdk-requirement";
        public const string DotnetIsolatedMigrationDocLink = "https://aka.ms/af-dotnet-isolated-migration";

        public static readonly string[] ValidRuntimeValues = [InProc8HostRuntime, InProc6HostRuntime, "default"];
    }
}
