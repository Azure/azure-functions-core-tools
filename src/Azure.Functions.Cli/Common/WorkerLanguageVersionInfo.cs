using System;
using System.Threading.Tasks;
using Azure.Functions.Cli.Helpers;

namespace Azure.Functions.Cli.Common
{
    public class WorkerLanguageVersionInfo
    {
        public readonly WorkerRuntime Runtime;

        public readonly string Version;

        public readonly string ExecutablePath;

        public int? Major
        {
            get
            {
                if (Version?.Split('.').Length >= 1 && int.TryParse(Version.Split('.')[0], out int major))
                {
                    return major;
                }
                return null;
            }
        }

        public int? Minor
        {
            get
            {
                if (Version?.Split('.').Length >= 2 && int.TryParse(Version.Split('.')[1], out int minor))
                {
                    return minor;
                }
                return null;
            }
        }

        public string Patch
        {
            get
            {
                if (Version?.Split('.').Length >= 3)
                {
                    return Version.Split('.')[2];
                }
                return null;
            }
        }

        public Task<string> DockerInitFileContent
        {
            get
            {
                if (Major == 3)
                {
                    switch (Minor)
                    {
                        case 6:
                            return StaticResources.DockerfilePython36;
                        case 7:
                            return StaticResources.DockerfilePython37;
                        case 8:
                            return StaticResources.DockerfilePython38;
                    }
                }
                return StaticResources.DockerfilePython36;
            }
        }

        public string BuildNativeDepsEnvironmentImage
        {
            get
            {
                if (Major == 3)
                {
                    switch (Minor)
                    {
                        case 6:
                            return Constants.DockerImages.LinuxPython36ImageAmd64;
                        case 7:
                            return Constants.DockerImages.LinuxPython37ImageAmd64;
                        case 8:
                            return Constants.DockerImages.LinuxPython38ImageAmd64;
                    }
                }
                return Constants.DockerImages.LinuxPython36ImageAmd64;
            }
        }

        /// <summary>
        /// Construct the basic information of a worker runtime
        /// </summary>
        /// <param name="runtime">The runtime of this worker (e.g. <see cref="WorkerRuntime"/>.Python)</param>
        /// <param name="version">A string of worker runtime version (e.g. 3.6.8)</param>
        /// <param name="executable">The path to executable (e.g. python, or C:\Program Files\nodejs\node.exe)</param>
        public WorkerLanguageVersionInfo(WorkerRuntime runtime, string version, string executable)
        {
            if (runtime == WorkerRuntime.None)
            {
                throw new ArgumentNullException("Worker runtime should not be None");
            }

            Runtime = runtime;
            Version = version?.Trim();
            ExecutablePath = executable;
        }
    }
}
