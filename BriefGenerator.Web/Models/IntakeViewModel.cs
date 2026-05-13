namespace BriefGenerator.Web.Models
{
    public class IntakeViewModel
    {
        public string? Title { get; set; }
        public string? Notes { get; set; }
        public List<IFormFile>? Files { get; set; }
    }
}