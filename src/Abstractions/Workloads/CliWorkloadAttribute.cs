// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

namespace Azure.Functions.Cli.Workloads;

/// <summary>
/// Marks the assembly as a Functions CLI workload and identifies the
/// <see cref="IWorkload"/> implementation the CLI should activate. Authors
/// add a single <c>[assembly: CliWorkload&lt;MyWorkload&gt;]</c>
/// declaration anywhere in their package; the install pipeline scans the
/// package's lib assemblies for this attribute and persists the result into
/// the global manifest so subsequent CLI invocations don't have to crack
/// open the assembly again.
/// </summary>
/// <typeparam name="T">
/// The <see cref="IWorkload"/> implementation to activate. Constrained at
/// compile time so authors can't accidentally point the CLI at a non-workload
/// type, surfacing the requirement at the call site rather than at install
/// time. <typeparamref name="T"/> must be defined in the same assembly that
/// declares this attribute; the install scanner rejects cross-assembly
/// declarations.
/// </typeparam>
/// <remarks>
/// <para>
/// Targets only the assembly (not types or methods) because workload
/// activation is a per-package concern, not a per-type concern. A single
/// package can ship at most one workload entry point; the install scanner
/// rejects packages that declare more than one occurrence of this attribute.
/// </para>
/// <para>
/// <see cref="AttributeUsageAttribute.Inherited"/> is <c>false</c>: the
/// scanner walks each assembly explicitly and inheritance is meaningless on
/// an assembly target.
/// </para>
/// </remarks>
[AttributeUsage(AttributeTargets.Assembly, AllowMultiple = false, Inherited = false)]
public sealed class CliWorkloadAttribute<T> : Attribute
    where T : IWorkload;
