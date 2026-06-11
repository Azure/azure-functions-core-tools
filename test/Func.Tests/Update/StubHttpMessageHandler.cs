// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

namespace Azure.Functions.Cli.Tests.Update;

/// <summary>
/// <see cref="HttpMessageHandler"/> that lets each test inspect the outgoing
/// request and program the response in one place. Captures the last seen
/// request so tests can assert on headers and URL after invocation.
/// </summary>
internal sealed class StubHttpMessageHandler(Func<HttpRequestMessage, CancellationToken, HttpResponseMessage> responder)
    : HttpMessageHandler
{
    private readonly Func<HttpRequestMessage, CancellationToken, HttpResponseMessage> _responder = responder ?? throw new ArgumentNullException(nameof(responder));

    public HttpRequestMessage? LastRequest { get; private set; }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        LastRequest = request;
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(_responder(request, cancellationToken));
    }
}
