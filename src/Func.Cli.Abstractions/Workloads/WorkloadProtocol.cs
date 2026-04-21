// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

namespace Azure.Functions.Cli.Workloads;

/// <summary>
/// Protocol constants for the out-of-process workload contract. The host (func)
/// communicates with each workload over stdio using JSON-RPC 2.0 messages
/// framed with LSP-style Content-Length headers.
///
/// This contract is the only coupling between the host and a workload — workloads
/// can be implemented in any language as long as they speak the protocol.
/// </summary>
public static class WorkloadProtocol
{
    /// <summary>Current protocol version. Bumped on breaking changes.</summary>
    public const string Version = "1.0";

    /// <summary>Header field name used for frame length.</summary>
    public const string ContentLengthHeader = "Content-Length";

    /// <summary>JSON-RPC version string.</summary>
    public const string JsonRpcVersion = "2.0";

    /// <summary>
    /// Method names exchanged over the wire. Keep stable — these are public API.
    /// </summary>
    public static class Methods
    {
        // Lifecycle
        public const string Initialize = "initialize";
        public const string Shutdown = "shutdown";

        // Project lifecycle
        public const string ProjectDetect = "project.detect";
        public const string ProjectInit = "project.init";

        // Templates
        public const string TemplatesList = "templates.list";
        public const string TemplatesCreate = "templates.create";

        // Packaging
        public const string PackRun = "pack.run";
    }

    /// <summary>
    /// Standard JSON-RPC error codes plus workload-specific extensions.
    /// </summary>
    public static class ErrorCodes
    {
        // Standard JSON-RPC 2.0
        public const int ParseError = -32700;
        public const int InvalidRequest = -32600;
        public const int MethodNotFound = -32601;
        public const int InvalidParams = -32602;
        public const int InternalError = -32603;

        // Workload-specific (range -32000..-32099 reserved for impl)
        public const int CapabilityNotSupported = -32001;
        public const int ProtocolVersionMismatch = -32002;
        public const int UserError = -32010;
    }

    /// <summary>
    /// Capability identifiers a workload may advertise during initialize.
    /// The host uses these to route requests only to workloads that support them.
    /// </summary>
    public static class Capabilities
    {
        public const string ProjectInit = "project.init";
        public const string ProjectDetect = "project.detect";
        public const string Templates = "templates";
        public const string Pack = "pack";
    }
}
