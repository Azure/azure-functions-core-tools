// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Common;
using System.Runtime.InteropServices;

namespace Azure.Functions.Cli.Tests.Common;

internal static class TemplateSourceTestPlatform
{
    internal static bool IsSupported => PhysicalFileSystem.IsTemplateSourcePlatformSupported(
        OperatingSystem.IsWindows(),
        RuntimeInformation.ProcessArchitecture,
        RuntimeInformation.OSArchitecture);
}

[AttributeUsage(AttributeTargets.Method)]
internal sealed class WindowsX64TemplateSourceFactAttribute : FactAttribute
{
    public WindowsX64TemplateSourceFactAttribute()
    {
        if (!TemplateSourceTestPlatform.IsSupported)
        {
            Skip = "Guarded template-source reads are qualified only on Windows x64.";
        }
    }
}

[AttributeUsage(AttributeTargets.Method)]
internal sealed class WindowsX64TemplateSourceTheoryAttribute : TheoryAttribute
{
    public WindowsX64TemplateSourceTheoryAttribute()
    {
        if (!TemplateSourceTestPlatform.IsSupported)
        {
            Skip = "Guarded template-source reads are qualified only on Windows x64.";
        }
    }
}

[AttributeUsage(AttributeTargets.Method)]
internal sealed class UnsupportedTemplateSourceFactAttribute : FactAttribute
{
    public UnsupportedTemplateSourceFactAttribute()
    {
        if (TemplateSourceTestPlatform.IsSupported)
        {
            Skip = "Requires a platform outside the Windows x64 template-source boundary.";
        }
    }
}
