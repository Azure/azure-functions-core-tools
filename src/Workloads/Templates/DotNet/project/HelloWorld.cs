using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;

namespace FunctionsProject;

public sealed class HelloWorld
{
    [Function(nameof(HelloWorld))]
    public IActionResult Run([HttpTrigger(AuthorizationLevel.Function, "get", "post")] HttpRequest request)
    {
        return new OkObjectResult("Hello, world!");
    }
}
