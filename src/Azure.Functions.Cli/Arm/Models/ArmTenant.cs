using Newtonsoft.Json;

namespace Azure.Functions.Cli.Arm.Models
{
    internal class ArmTenant
    {
        [JsonProperty("id")]
        public string Id { get; set; }

        [JsonProperty("tenantId")]
        public string TenantId { get; set; }
    }
}
