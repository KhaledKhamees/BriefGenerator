namespace BriefGenerator.Api.Models
{
    public class Brief
    {
        public Guid Id { get; set; }
        public Guid IntakeSessionId { get; set; }
        public string StructuredJson { get; set; }
        public string MarkdownOutput { get; set; }
        public string MissingFieldsJson { get; set; }
        public string Status { get; set; }
        public IntakeSession intakeSession { get; set; }
    }
}
