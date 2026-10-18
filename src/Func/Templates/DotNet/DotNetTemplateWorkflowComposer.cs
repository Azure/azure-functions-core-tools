// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Projects;
using Azure.Functions.Cli.Templates.DotNet.Shared;

namespace Azure.Functions.Cli.Templates.DotNet;

/// <summary>
/// Explicitly composes CLI-owned .NET template helpers without installing a default workflow or SDK capability.
/// </summary>
internal sealed class DotNetTemplateWorkflowComposer(IFunctionsProjectResolver projects, IFileSystem files,
    IDotnetCliRunner dotnetCli, FSharpProjectFiles paths, NativeTemplateStackCompatibility? compatibility = null)
{
    private readonly IFunctionsProjectResolver _projects = projects ?? throw new ArgumentNullException(nameof(projects));
    private readonly IFileSystem _files = files ?? throw new ArgumentNullException(nameof(files));
    private readonly IDotnetCliRunner _dotnetCli = dotnetCli ?? throw new ArgumentNullException(nameof(dotnetCli));
    private readonly FSharpProjectFiles _paths = paths ?? throw new ArgumentNullException(nameof(paths));

    public DotNetTemplateWorkflowProvider Create()
    {
        DotNetTemplateProjectPreparation preparation = new(_dotnetCli);
        return new(new(compatibility), new(_projects, _files, preparation, _dotnetCli, _paths), _files, _paths,
            preparation, new(_dotnetCli), new(_dotnetCli, _paths));
    }
}
