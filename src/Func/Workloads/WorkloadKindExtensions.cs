// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

namespace Azure.Functions.Cli.Workloads;

/// <summary>
/// Extension members that translate <see cref="WorkloadKind"/> to and from the
/// wire values used by <c>workload.json</c>'s <c>kind</c> field.
/// </summary>
internal static class WorkloadKindExtensions
{
    private const string WorkloadValue = "workload";
    private const string ContentValue = "content";
    private const string MetaValue = "meta";
    private const string RidPointerValue = "rid-pointer";

    extension(WorkloadKind kind)
    {
        /// <summary>
        /// Returns the manifest wire value for this kind.
        /// </summary>
        /// <exception cref="ArgumentOutOfRangeException">
        /// Thrown when <paramref name="kind"/> is not a defined
        /// <see cref="WorkloadKind"/>.
        /// </exception>
        public string ToWireValue()
            => kind switch
            {
                WorkloadKind.Workload => WorkloadValue,
                WorkloadKind.Content => ContentValue,
                WorkloadKind.Meta => MetaValue,
                WorkloadKind.RidPointer => RidPointerValue,
                _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unrecognized workload kind."),
            };
    }

    extension(WorkloadKind)
    {
        /// <summary>
        /// Converts a manifest wire value to its <see cref="WorkloadKind"/>,
        /// returning <see langword="false"/> when the value is unrecognized.
        /// </summary>
        /// <remarks>
        /// Named <c>TryParseWireValue</c> rather than <c>TryParse</c> because the
        /// inherited <see cref="Enum.TryParse{TEnum}(string, out TEnum)"/> would win
        /// overload resolution and silently match enum member names instead of wire
        /// values.
        /// </remarks>
        public static bool TryParseWireValue(string? value, out WorkloadKind kind)
        {
            switch (value)
            {
                case WorkloadValue:
                    kind = WorkloadKind.Workload;
                    return true;
                case ContentValue:
                    kind = WorkloadKind.Content;
                    return true;
                case MetaValue:
                    kind = WorkloadKind.Meta;
                    return true;
                case RidPointerValue:
                    kind = WorkloadKind.RidPointer;
                    return true;
                default:
                    kind = default;
                    return false;
            }
        }
    }
}
