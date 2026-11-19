// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

namespace Azure.Functions.Cli.Actions.LocalActions
{
    internal sealed record FlexByocTemplateArtifacts(
        string Dockerfile,
        string Entrypoint,
        string DockerIgnore);
}
