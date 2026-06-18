// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Microsoft.Build.Framework;

namespace Azure.Functions.Cli.Workloads.Sdk.Tasks;

public sealed class ResolveWorkloadCopyLocal : Microsoft.Build.Utilities.Task
{
    [Required]
    public ITaskItem[] Items { get; set; } = [];

    [Required]
    public ITaskItem[] UnifiedItems { get; set; } = [];

    [Output]
    public ITaskItem[] WorkloadCopyLocal { get; set; } = [];

    public override bool Execute()
    {
        GetUnifiedItems(out HashSet<string> runtimeProjects, out HashSet<string> runtimePackages);

        List<ITaskItem> filteredCopyLocalFiles = [];
        foreach (ITaskItem item in Items)
        {
            if (ShouldIncludeItem(item, runtimeProjects, runtimePackages))
            {
                string path = item.GetMetadata("DestinationSubPath") is string p
                    ? p
                    : Path.GetFileName(item.ItemSpec);
                item.SetMetadata("TargetPath", path);
                filteredCopyLocalFiles.Add(item);
            }
        }

        WorkloadCopyLocal = [.. filteredCopyLocalFiles];
        return true;
    }

    private bool ShouldIncludeItem(
        ITaskItem item, HashSet<string> runtimeProjects, HashSet<string> runtimePackages)
    {
        string packageId = item.GetMetadata("NuGetPackageId");
        if (!string.IsNullOrEmpty(packageId) && runtimePackages.Contains(packageId))
        {
            // Comes from a runtime package, exclude.
            Log.LogMessage(MessageImportance.Low, $"Excluding item: {item.ItemSpec} (from package: {packageId})");
            return false;
        }

        string project = item.GetMetadata("MSBuildSourceProjectFile");
        if (!string.IsNullOrEmpty(project) && runtimeProjects.Contains(project))
        {
            // Comes from a runtime project, exclude.
            Log.LogMessage(MessageImportance.Low, $"Excluding item: {item.ItemSpec} (from project: {project})");
            return false;
        }

        return true;
    }

    private static bool IsKind(ITaskItem item, string value)
    {
        string actual = item.GetMetadata("Kind");
        return !string.IsNullOrEmpty(actual) && string.Equals(actual, value, StringComparison.OrdinalIgnoreCase);
    }

    private void GetUnifiedItems(out HashSet<string> unifiedProjects, out HashSet<string> unifiedPackages)
    {
        unifiedProjects = new(
            UnifiedItems.Where(i => IsKind(i, "Project")).Select(i => Path.GetFullPath(i.ItemSpec)), StringComparer.OrdinalIgnoreCase);
        unifiedPackages = new(
            UnifiedItems.Where(i => IsKind(i, "Package")).Select(i => i.ItemSpec), StringComparer.OrdinalIgnoreCase);
    }
}
