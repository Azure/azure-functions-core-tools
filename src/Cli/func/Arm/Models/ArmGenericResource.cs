namespace Azure.Functions.Cli.Arm.Models
{
    public class ArmGenericResource
    {
        public string id { get; set; }

        public string name { get; set; }

        public string type { get; set; }

        public string kind { get; set; }

        public string location { get; set; }
    }
}