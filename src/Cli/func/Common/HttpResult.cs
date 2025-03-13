using System.Collections.Generic;

namespace Azure.Functions.Cli.Common
{
    public class HttpResult<TSuccessResult, TErrorResult>
    {
        public TSuccessResult SuccessResult { get; private set; }
        public TErrorResult ErrorResult { get; private set; }

        public bool IsSuccessful => EqualityComparer<TErrorResult>.Default.Equals(ErrorResult, default(TErrorResult));

        public HttpResult(TSuccessResult successResult, TErrorResult errorResult = default(TErrorResult))
        {
            SuccessResult = successResult;
            ErrorResult = errorResult;
        }
    }
}
