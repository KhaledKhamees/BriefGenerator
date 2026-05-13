using System.Text.Json.Serialization;

namespace BriefGenerator.Web.Models
{
    public class BriefDto
    {
        public Guid Id { get; set; }
        public Guid IntakeSessionId { get; set; }
        public string? StructuredJson { get; set; }
        public string? MarkdownOutput { get; set; }
        public string? MissingFieldsJson { get; set; }
        public string? Status { get; set; }
        public IntakeSessionDto? IntakeSession { get; set; }
    }

    public class IntakeSessionDto
    {
        public Guid Id { get; set; }
        public string? Title { get; set; }
        public string? Status { get; set; }
        public DateTime CreatedAt { get; set; }
    }

    public class IntakeStatusDto
    {
        public Guid Id { get; set; }
        public string? Status { get; set; }
        public BriefDto? Brief { get; set; }
        public string? ShareToken { get; set; }
        public string? ShareUrl { get; set; }
    }

    public class ShareLinkDto
    {
        public string? Url { get; set; }
        public string? Token { get; set; }
        public DateTime ExpiresAt { get; set; }
    }

    public class ClientResponseDto
    {
        public Guid Id { get; set; }
        public Guid BriefId { get; set; }
        public string? FieldName { get; set; }
        public string? ResponseText { get; set; }
    }

    /// <summary>Parsed representation of the Gemini-generated structured brief JSON.</summary>
    public class StructuredBriefData
    {
        [JsonPropertyName("project_name")]
        public string? ProjectName { get; set; }

        [JsonPropertyName("client_name")]
        public string? ClientName { get; set; }

        [JsonPropertyName("business_goal")]
        public string? BusinessGoal { get; set; }

        [JsonPropertyName("target_users")]
        public List<string>? TargetUsers { get; set; }

        [JsonPropertyName("features")]
        public List<string>? Features { get; set; }

        [JsonPropertyName("platforms")]
        public List<string>? Platforms { get; set; }

        [JsonPropertyName("design_requirements")]
        public List<string>? DesignRequirements { get; set; }

        [JsonPropertyName("technical_constraints")]
        public List<string>? TechnicalConstraints { get; set; }

        [JsonPropertyName("timeline")]
        public string? Timeline { get; set; }

        [JsonPropertyName("budget")]
        public string? Budget { get; set; }

        /// <summary>
        /// Gemini sometimes returns this as List&lt;string&gt;, sometimes as
        /// List&lt;object&gt; with nested fields. We accept JsonElement so we
        /// can handle both shapes without throwing.
        /// </summary>
        [JsonPropertyName("missing_information")]
        public List<System.Text.Json.JsonElement>? MissingInformationRaw { get; set; }

        /// <summary>Flattened list of missing-field strings regardless of shape.</summary>
        [JsonIgnore]
        public List<string> MissingInformationFlat
        {
            get
            {
                var result = new List<string>();
                if (MissingInformationRaw == null) return result;

                foreach (var el in MissingInformationRaw)
                {
                    if (el.ValueKind == System.Text.Json.JsonValueKind.String)
                    {
                        var s = el.GetString();
                        if (!string.IsNullOrWhiteSpace(s)) result.Add(s);
                    }
                    else if (el.ValueKind == System.Text.Json.JsonValueKind.Object)
                    {
                        // Shape: {"clarifying_question": "...", "reason": "..."}
                        if (el.TryGetProperty("clarifying_question", out var q) &&
                            q.ValueKind == System.Text.Json.JsonValueKind.String)
                        {
                            var s = q.GetString();
                            if (!string.IsNullOrWhiteSpace(s)) result.Add(s);
                        }
                        else if (el.TryGetProperty("question", out var q2) &&
                                 q2.ValueKind == System.Text.Json.JsonValueKind.String)
                        {
                            var s = q2.GetString();
                            if (!string.IsNullOrWhiteSpace(s)) result.Add(s);
                        }
                    }
                }
                return result;
            }
        }
    }
}