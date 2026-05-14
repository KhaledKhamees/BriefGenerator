namespace BriefGenerator.Web.Models;

public class EditBriefViewModel
{
    public Guid Id { get; set; }

    public string? ProjectName { get; set; }
    public string? ClientName { get; set; }
    public string? BusinessGoal { get; set; }
    public string? Timeline { get; set; }
    public string? Budget { get; set; }

    /// <summary>One item per line.</summary>
    public string? TargetUsers { get; set; }

    /// <summary>One item per line.</summary>
    public string? Features { get; set; }

    /// <summary>One item per line.</summary>
    public string? Platforms { get; set; }

    /// <summary>One item per line.</summary>
    public string? DesignRequirements { get; set; }

    /// <summary>One item per line.</summary>
    public string? TechnicalConstraints { get; set; }

    public string? MarkdownOutput { get; set; }

    /// <summary>Preserved so we can carry forward fields we don't expose in the form (e.g. missing_information).</summary>
    public string? OriginalStructuredJson { get; set; }
}
