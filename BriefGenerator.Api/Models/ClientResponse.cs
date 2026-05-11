namespace BriefGenerator.Api.Models
{
    public class ClientResponse
    {
        public Guid Id { get; set; }
        public Guid BriefId { get; set; }
        public string FieldName { get; set; }
        public string ResponseText { get; set; }
    }
}
