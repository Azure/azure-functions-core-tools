using System;
using System.Net.Http;
using System.Threading.Tasks;

namespace Azure.Functions.Cli.Interfaces
{
    internal interface IFunctionsLocalServer
    {
        Task<HttpClient> ConnectAsync(TimeSpan timeout, bool noInteractive);
    }
}