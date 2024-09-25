﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

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
    }
}
