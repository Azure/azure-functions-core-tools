// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

namespace Azure.Functions.Cli.Projects.FileWriting;

internal class WindowsProjectFileSystem
{
    public virtual WindowsProjectWriteSession OpenSession(string destination, CancellationToken cancellationToken) =>
        new(destination, cancellationToken);
}
