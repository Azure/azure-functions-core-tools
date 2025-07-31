// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

namespace Azure.Functions.Cli.Common
{
    [Flags]
    public enum BuildOption
    {
        None, // will act as "func azure functionapp publish <appname> --no-build"
        Local, // will act as "func azure functionapp publish <appname>", use WEBSITE_RUN_FROM_PACKAGE on Linux Consumption, use zipdeploy to others
        Remote, // will act as "func azure functionapp publish <appname> --server-side-build"
        Container, // will act as "func azure functionapp publish <appname> --build-native-deps"
        Default, // will trigger remote build if requirements.txt has content
        Deferred // will create a zip file when running `func pack` that is remote build ready, but does not deploy it
    }
}
