namespace BriefGenerator.Web.Models
{
    public class PublicBriefViewModel
    {
        public string Token { get; set; } = string.Empty;
        public BriefDto? Brief { get; set; }
        public StructuredBriefData? Structured { get; set; }
        /// <summary>Display strings for the missing fields (questions or field names).</summary>
        public List<string> MissingFields { get; set; } = new();
        /// <summary>Canonical field names in the same order as MissingFields, e.g. ["budget","timeline"].</summary>
        public List<string> MissingFieldNames { get; set; } = new();
    }

    public class SubmitAnswersViewModel
    {
        public string Token { get; set; } = string.Empty;
        public List<FieldAnswer> Answers { get; set; } = new();
    }

    public class FieldAnswer
    {
        public string Field { get; set; } = string.Empty;
        public string Value { get; set; } = string.Empty;
    }
}
