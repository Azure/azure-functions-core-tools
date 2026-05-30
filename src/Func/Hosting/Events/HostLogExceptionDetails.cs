// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Text;

namespace Azure.Functions.Cli.Hosting.Events;

/// <summary>
/// Structured exception details reported by the host process.
/// </summary>
internal sealed record HostLogExceptionDetails(string Type, string Message, string? Stack = null, HostLogExceptionDetails? InnerException = null)
{
    public static HostLogExceptionDetails FromException(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);

        return new HostLogExceptionDetails(
            exception.GetType().FullName ?? exception.GetType().Name,
            exception.Message,
            exception.StackTrace,
            exception.InnerException is not null ? FromException(exception.InnerException) : null);
    }

    public string FormatSummary()
    {
        var builder = new StringBuilder();
        AppendSummary(builder, this);
        return builder.ToString();
    }

    private static void AppendSummary(StringBuilder builder, HostLogExceptionDetails exception)
    {
        builder.Append(exception.Type);
        if (!string.IsNullOrEmpty(exception.Message))
        {
            builder.Append(": ");
            builder.Append(exception.Message);
        }

        if (exception.InnerException is not null)
        {
            builder.Append(" ---> ");
            AppendSummary(builder, exception.InnerException);
        }
    }
}
