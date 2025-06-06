﻿// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Security;
using Microsoft.DotNet.PlatformAbstractions;
using Microsoft.Win32;

namespace Azure.Functions.Cli.Telemetry
{
    internal enum DockerContainer
    {
        True,
        False,
        Unknown
    }

    internal class DockerContainerDetectorForTelemetry : IDockerContainerDetector
    {
        public DockerContainer IsDockerContainer()
        {
            switch (RuntimeEnvironment.OperatingSystemPlatform)
            {
                case Platform.Windows:
                    try
                    {
#pragma warning disable CA1416 // Validate platform compatibility - This is a windows only code path.

                        using var subkey = Registry.LocalMachine.OpenSubKey("System\\CurrentControlSet\\Control");

                        return subkey?.GetValue("ContainerType") != null
                            ? DockerContainer.True
                            : DockerContainer.False;

#pragma warning restore CA1416 // Validate platform compatibility
                    }
                    catch (SecurityException)
                    {
                        return DockerContainer.Unknown;
                    }

                case Platform.Linux:
                    try
                    {
                        return ReadProcToDetectDockerInLinux()
                            ? DockerContainer.True
                            : DockerContainer.False;
                    }
                    catch (Exception ex) when (ex is IOException || ex.InnerException is IOException)
                    {
                        // In some environments (restricted docker container, shared hosting etc.),
                        // procfs is not accessible and we get UnauthorizedAccessException while the
                        // inner exception is set to IOException. In this case, it is unknown.
                        return DockerContainer.Unknown;
                    }

                case Platform.Unknown:
                    return DockerContainer.Unknown;
                case Platform.Darwin:
                default:
                    return DockerContainer.False;
            }
        }

        private static bool ReadProcToDetectDockerInLinux()
        {
            return File
                .ReadAllText("/proc/1/cgroup")
                .Contains("/docker/");
        }
    }
}
