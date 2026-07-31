// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

namespace Azure.Functions.Cli.Templates.Engine;

/// <summary>
/// Provides the provider-owned scratch directory that append-flow templates
/// instantiate into, so their staged snippet never lands in the user's
/// project. Behind an interface so the scaffolder stays substitutable in tests.
/// </summary>
internal interface IFuncTemplateStagingArea
{
    /// <summary>
    /// Creates a fresh, unique staging directory and returns its absolute path.
    /// </summary>
    public string Create();

    /// <summary>
    /// Best-effort removal of a staging directory once the scaffold completes.
    /// </summary>
    public void Cleanup(string path);
}
