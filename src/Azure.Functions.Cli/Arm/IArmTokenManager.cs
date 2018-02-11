using System.Collections.Generic;
using System.Threading.Tasks;

namespace Azure.Functions.Cli.Arm
{
    internal interface IArmTokenManager
    {
        Task Login();

        void Logout();

        Task<string> GetToken(string tenantId);

        Task<IEnumerable<string>> GetTenants();
    }
}
