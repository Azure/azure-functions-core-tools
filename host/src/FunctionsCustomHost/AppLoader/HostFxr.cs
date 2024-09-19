// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Runtime.InteropServices;

namespace FunctionsNetHost
{
    static partial class HostFxr
    {
        public unsafe struct hostfxr_initialize_parameters
        {
            public nint size;
            public char* host_path;
            public char* dotnet_root;
        };

        [LibraryImport("hostfxr", EntryPoint = "hostfxr_initialize_for_dotnet_command_line",
#if OS_LINUX
            StringMarshalling = StringMarshalling.Utf8
#else
            StringMarshalling = StringMarshalling.Utf16
#endif
        )]
        public unsafe static partial int Initialize(
            int argc,
            string[] argv,
            IntPtr parameters,
            out IntPtr host_context_handle
        );

        [LibraryImport("hostfxr", EntryPoint = "hostfxr_run_app")]
        public static partial int Run(IntPtr host_context_handle);

        [LibraryImport("hostfxr", EntryPoint = "hostfxr_set_runtime_property_value",
#if OS_LINUX
            StringMarshalling = StringMarshalling.Utf8
#else
            StringMarshalling = StringMarshalling.Utf16
#endif
        )]
        public static partial int SetAppContextData(IntPtr host_context_handle, string name, string value);

        [LibraryImport("hostfxr", EntryPoint = "hostfxr_close")]
        public static partial int Close(IntPtr host_context_handle);
    }
}
