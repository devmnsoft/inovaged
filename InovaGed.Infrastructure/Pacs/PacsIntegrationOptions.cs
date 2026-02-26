namespace InovaGed.Infrastructure.Pacs
{
    public sealed class PacsIntegrationOptions
    {
        public bool Enabled { get; set; } = true;
        public string InboundRoot { get; set; } = "";
        public string[] AllowedExtensions { get; set; } = Array.Empty<string>();
        public int MaxFilesPerFolder { get; set; } = 5000;
    }
}
