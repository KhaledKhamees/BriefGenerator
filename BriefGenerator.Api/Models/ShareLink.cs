namespace BriefGenerator.Api.Models
{
    public class ShareLink
    {
        public Guid Id { get; set; }
        public Guid BriefId { get; set; }
        public string Token { get; set; }
        public DateTime ExpiresAt { get; set; }
    }
}
