// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Runtime.InteropServices;

namespace CoreToolsHost
{
    internal static partial class HostFxr
    {
        [LibraryImport("hostfxr", EntryPoint = "hostfxr_initialize_for_dotnet_command_line",
#if OS_LINUX
            StringMarshalling = StringMarshalling.Utf8)]
#else
            StringMarshalling = StringMarshalling.Utf16)]
#endif
        public static unsafe partial int Initialize(
            int argc,
            string[] argv,
            IntPtr parameters,
            out IntPtr hostContextHandle);

        [LibraryImport("hostfxr", EntryPoint = "hostfxr_run_app")]
        public static partial int Run(IntPtr hostContextHandle);

        [LibraryImport("hostfxr", EntryPoint = "hostfxr_set_runtime_property_value",
#if OS_LINUX
            StringMarshalling = StringMarshalling.Utf8)]
#else
            StringMarshalling = StringMarshalling.Utf16)]
#endif
        public static partial int SetAppContextData(IntPtr hostContextHandle, string name, string value);

        [LibraryImport("hostfxr", EntryPoint = "hostfxr_close")]
        public static partial int Close(IntPtr hostContextHandle);

        public unsafe struct HostFXRInitializeParameters
        {
            public nint Size;
            public char* HostPath;
            public char* DotnetRoot;
        }
    }
}
