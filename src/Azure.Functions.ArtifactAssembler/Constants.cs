using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Azure.Functions.ArtifactAssembler
{
    internal static class Constants
    {
        internal const string StagingDirName = "staging";
        internal const string InProc8DirectoryName = "in-proc8";
        internal const string InProc6DirectoryName = "in-proc6";
        internal const string CoreToolsHostDirectoryName = "host";
        internal const string VisualStudioOutputArtifactDirectoryName = "coretools-visualstudio";
        internal const string _InProcOutputArtifactNameSuffix = "_inproc";
        internal const string _coreToolsProductVersionPattern = @"(\d+\.\d+\.\d+)$";
        internal const string _artifactNameRegexPattern = @"^(.*?)(\d+\.\d+\.\d+)$";
        internal const string OutOfProcDirectoryName = "default";
        internal const string CliOutputArtifactDirectoryName = "coretools-cli";
    }
}
