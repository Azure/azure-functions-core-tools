using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace Azure.Functions.Cli.Actions.HostActions.WebHost.Security
{
    static class AuthMiddlewareExtensions
    {
        public static IApplicationBuilder UseAppServiceMiddleware(this IApplicationBuilder builder, ILoggerProvider loggerProvider)
        {
            return builder.UseMiddleware<AuthMiddleware>(builder, loggerProvider);
        }
    }

    class AuthMiddleware : Microsoft.Azure.AppService.MiddlewareShim.Startup
    {
        // The middleware delegate to call after this one finishes processing
        private readonly RequestDelegate _next;

        public AuthMiddleware(RequestDelegate next, IApplicationBuilder app, ILoggerProvider loggerProvider)
        {
            _next = next;
            this.Configure(app: app, loggerFactory: null, loggerProvider: loggerProvider);
        }

        public Task Invoke(HttpContext httpContext)
        {
            // OnRequest handles calling Invoke on _next and sending the response
            return this.OnRequest(httpContext, _next);
        }
    }
}
