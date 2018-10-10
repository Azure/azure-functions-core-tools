using System.Threading.Tasks;
namespace Azure.Functions.Cli.Interfaces
{
    internal interface IAuthManager
    {
        Task CreateAADApplication(string accessToken, string AADName, string appName = null);
    }
}