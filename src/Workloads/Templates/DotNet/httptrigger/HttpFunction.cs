using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;

namespace FunctionNamespace;

public sealed class HttpFunction
{
    [Function(nameof(HttpFunction))]
    public IActionResult Run([HttpTrigger(AuthorizationLevel.Function, "get", "post")] HttpRequest request)
    {
        return new OkObjectResult("Hello, world!");
    }
}
