// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

namespace Azure.Functions.Cli.Templates.Engine;

/// <summary>
/// The func-owned post-action ids the scaffolder dispatches. Templates trigger
/// behaviour only through this allowlist; any other id falls through to its
/// manual instructions so template content can never execute code.
/// </summary>
internal static class FuncPostActionIds
{
    /// <summary>
    /// Func append action (Python v2 append flow): merge the staged snippet
    /// into the target app or blueprint file.
    /// </summary>
    internal static readonly Guid Append = new("E715449B-264D-4669-BC62-DFC06539D969");

    /// <summary>
    /// Add a package or project reference to the scaffolded project file.
    /// </summary>
    internal static readonly Guid AddReference = new("B17581D1-C5C9-4489-8F0A-004BE667B814");

    /// <summary>
    /// Surface the action's manual instructions to the user.
    /// </summary>
    internal static readonly Guid ManualInstructions = new("AC1156F7-BB77-4DB8-B28F-24EEBCCA1E5C");
}
