// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

namespace Azure.Functions.Cli.Templates.Engine;

internal sealed class TemplateStoreRecoveryRequiredException()
    : InvalidOperationException("The installed template store requires recovery. Run an explicit workload install or update to recover it, then retry. No template registry or project files were changed.");
