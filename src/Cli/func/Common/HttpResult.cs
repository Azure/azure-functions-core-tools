// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

namespace Azure.Functions.Cli.Common
{
    public class HttpResult<TSuccessResult, TErrorResult>(TSuccessResult successResult, TErrorResult errorResult = default)
    {
        public TSuccessResult SuccessResult { get; private set; } = successResult;

        public TErrorResult ErrorResult { get; private set; } = errorResult;

        public bool IsSuccessful => EqualityComparer<TErrorResult>.Default.Equals(ErrorResult, default);
    }
}
