// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

namespace Azure.Functions.Cli.Helpers;

public class DockerImageInfo
{
    public string ImageName { get; set; }

    public bool CanPull { get; set; }
}
