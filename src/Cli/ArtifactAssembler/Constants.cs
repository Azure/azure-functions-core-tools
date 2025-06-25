// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

namespace Azure.Functions.Cli.ArtifactAssembler
{
    internal static class Constants
    {
        internal const string StagingDirName = "staging";
        internal const string ReleaseDirName = "release";
        internal const string InProc8DirectoryName = "in-proc8";
        internal const string InProc6DirectoryName = "in-proc6";
        internal const string CoreToolsHostDirectoryName = "host";
        internal const string VisualStudioOutputArtifactDirectoryName = "func-visualstudio";
        internal const string InProcOutputArtifactNameSuffix = "_inproc";
        internal const string CoreToolsProductVersionPattern = @"(\d+\.\d+\.\d+(?:-[0-9A-Za-z\-\.]+)?(?:\+[0-9A-Za-z\-\.]+)?)$";
        internal const string ArtifactNameRegexPattern = @"^(.*?)(\d+\.\d+\.\d+)$";
        internal const string OutOfProcDirectoryName = "default";
        internal const string CliOutputArtifactDirectoryName = "func-cli";
    }
}
