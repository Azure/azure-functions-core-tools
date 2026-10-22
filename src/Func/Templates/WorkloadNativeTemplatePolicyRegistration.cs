// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Templates.Native;
using Azure.Functions.Cli.Workloads;

namespace Azure.Functions.Cli.Templates;

internal sealed record WorkloadNativeTemplatePolicyRegistration(RuntimeWorkloadInfo Owner, NativeTemplatePolicy Policy)
{
    public RuntimeWorkloadInfo Owner { get; } = Owner ?? throw new ArgumentNullException(nameof(Owner));

    public NativeTemplatePolicy Policy { get; } = Policy ?? throw new ArgumentNullException(nameof(Policy));
}
