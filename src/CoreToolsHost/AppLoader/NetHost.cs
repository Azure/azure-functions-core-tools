// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Runtime.InteropServices;

namespace CoreToolsHost
{
    internal class NetHost
    {
        [DllImport("nethost", EntryPoint = "GetHostFXRPath", CharSet = CharSet.Auto)]
        private static extern unsafe int GetHostFXRPath(
        [Out] char[] buffer,
        [In] ref int buffer_size,
        HostFxrParameters* parameters);

        internal static unsafe string GetHostFxrPath(HostFxrParameters* parameters)
        {
            char[] buffer = new char[200];
            int bufferSize = buffer.Length;

            int rc = GetHostFXRPath(buffer, ref bufferSize, parameters);

            if (rc != 0)
            {
                throw new InvalidOperationException("Failed to get the hostfxr path.");
            }

            return new string(buffer, 0, bufferSize - 1);
        }

        public unsafe struct HostFxrParameters
        {
            public nint Size;

            // Optional.Path to the application assembly,
            // If specified, hostfxr is located from this directory if present (For self-contained deployments)
            public char* AssemblyPath;
            public char* DotnetRoot;
        }
    }
}
