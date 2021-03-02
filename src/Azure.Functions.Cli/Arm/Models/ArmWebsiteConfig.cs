namespace Azure.Functions.Cli.Arm.Models
{
    internal class ArmWebsiteConfig
    {
        public string scmType { get; set; }
        public string linuxFxVersion { get; set; }
        public string netFrameworkVersion { get; set; }
    }
}
