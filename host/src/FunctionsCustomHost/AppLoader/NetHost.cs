// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Runtime.InteropServices;

namespace FunctionsNetHost
{
    internal class NetHost
    {
        public unsafe struct get_hostfxr_parameters
        {
            public nint size;

            // Optional.Path to the application assembly,
            // If specified, hostfxr is located from this directory if present (For self-contained deployments)
            public char* assembly_path;
            public char* dotnet_root;
        }

        [DllImport("nethost", CharSet = CharSet.Auto)]
        private unsafe static extern int get_hostfxr_path(
        [Out] char[] buffer,
        [In] ref int buffer_size,
        get_hostfxr_parameters* parameters);

        internal unsafe static string GetHostFxrPath(get_hostfxr_parameters* parameters)
        {
            char[] buffer = new char[200];
            int bufferSize = buffer.Length;

            int rc = get_hostfxr_path(buffer, ref bufferSize, parameters);

            if (rc != 0)
            {
                throw new InvalidOperationException("Failed to get the hostfxr path.");
            }

            return new string(buffer, 0, bufferSize - 1);
        }
    }
}
