// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Microsoft.AspNetCore.Authorization;
using Microsoft.Azure.WebJobs.Script.Description;
using Microsoft.Azure.WebJobs.Script.WebHost.Security.Authorization;

namespace Azure.Functions.Cli.Workloads.Host.Security;

internal sealed class FunctionsHostAuthorizationHandler
    : AuthorizationHandler<FunctionAuthorizationRequirement, FunctionDescriptor>
{
    protected override Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        FunctionAuthorizationRequirement requirement,
        FunctionDescriptor resource)
    {
        context.Succeed(requirement);
        return Task.CompletedTask;
    }
}
