// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

namespace Azure.Functions.Cli.Telemetry
{
    internal interface IDockerContainerDetector
    {
        internal DockerContainer IsDockerContainer();
    }
}
