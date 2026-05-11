namespace BriefGenerator.Api.Models
{
    public class UploadedFile
    {
        public Guid Id { get; set; }
        public Guid IntakeSessionId { get; set; }
        public string FileName { get; set; }
        public string FileType { get; set; }
        public string StoragePath { get; set; } = string.Empty;
        public string ExtractedText { get; set; } = string.Empty ;
        public IntakeSession IntakeSession { get; set; }
    }
}
