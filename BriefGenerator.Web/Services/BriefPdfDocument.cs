using BriefGenerator.Web.Models;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace BriefGenerator.Web.Services;

/// <summary>
/// QuestPDF document that renders a confirmed brief as a clean, well-organised PDF.
/// </summary>
public class BriefPdfDocument : IDocument
{
    private readonly BriefDto _brief;
    private readonly StructuredBriefData _data;
    private readonly List<ClientResponseDto> _clientResponses;

    // ── Palette ──────────────────────────────────────────────────────────
    private const string AccentHex  = "#4F46E5"; // indigo
    private const string LightHex   = "#F5F5F5";
    private const string MutedHex   = "#6B7280";
    private const string BorderHex  = "#E5E7EB";
    private const string SuccessHex = "#059669";

    public BriefPdfDocument(BriefDto brief, StructuredBriefData data, List<ClientResponseDto> clientResponses)
    {
        _brief = brief;
        _data = data;
        _clientResponses = clientResponses;
    }

    public DocumentMetadata GetMetadata() => new()
    {
        Title = $"Project Brief – {_data.ProjectName ?? "Untitled"}",
        Author = "BriefGen",
        CreationDate = DateTimeOffset.UtcNow
    };

    public DocumentSettings GetSettings() => DocumentSettings.Default;

    public void Compose(IDocumentContainer container)
    {
        container.Page(page =>
        {
            page.Size(PageSizes.A4);
            page.Margin(40);
            page.DefaultTextStyle(t => t.FontFamily("Arial").FontSize(10).FontColor(Colors.Grey.Darken3));

            page.Header().Element(ComposeHeader);
            page.Content().Element(ComposeContent);
            page.Footer().Element(ComposeFooter);
        });
    }

    // ── Header ───────────────────────────────────────────────────────────
    private void ComposeHeader(IContainer c)
    {
        c.Column(col =>
        {
            // Top accent bar
            col.Item()
               .Height(6)
               .Background(AccentHex);

            col.Item().PaddingTop(16).Row(row =>
            {
                row.RelativeItem().Column(inner =>
                {
                    inner.Item()
                         .Text("PROJECT BRIEF")
                         .FontSize(9)
                         .FontColor(AccentHex)
                         .Bold()
                         .LetterSpacing(0.12f);

                    inner.Item()
                         .Text(_data.ProjectName ?? "Untitled Brief")
                         .FontSize(22)
                         .Bold()
                         .FontColor(Colors.Grey.Darken4);

                    if (!string.IsNullOrWhiteSpace(_data.ClientName))
                    {
                        inner.Item()
                             .PaddingTop(2)
                             .Text($"Client: {_data.ClientName}")
                             .FontSize(10)
                             .FontColor(MutedHex);
                    }
                });

                row.ConstantItem(110).Column(meta =>
                {
                    meta.Item()
                        .Background(SuccessHex)
                        .Padding(6)
                        .AlignCenter()
                        .Text("CONFIRMED")
                        .FontSize(9)
                        .Bold()
                        .FontColor(Colors.White);

                    meta.Item()
                        .PaddingTop(6)
                        .AlignRight()
                        .Text($"Generated: {DateTime.UtcNow:MMM d, yyyy}")
                        .FontSize(8)
                        .FontColor(MutedHex);
                });
            });

            col.Item()
               .PaddingTop(12)
               .Height(1)
               .Background(BorderHex);
        });
    }

    // ── Content ──────────────────────────────────────────────────────────
    private void ComposeContent(IContainer c)
    {
        c.PaddingTop(16).Column(col =>
        {
            // Business Goal
            if (!string.IsNullOrWhiteSpace(_data.BusinessGoal))
                AddSection(col, "Business Goal", _data.BusinessGoal);

            // Two-column: Timeline + Budget
            if (!string.IsNullOrWhiteSpace(_data.Timeline) || !string.IsNullOrWhiteSpace(_data.Budget))
            {
                col.Item().PaddingBottom(14).Row(row =>
                {
                    if (!string.IsNullOrWhiteSpace(_data.Timeline))
                    {
                        row.RelativeItem().Column(inner =>
                        {
                            SectionLabel(inner, "Timeline");
                            inner.Item().Text(_data.Timeline).FontSize(10);
                        });
                    }
                    if (!string.IsNullOrWhiteSpace(_data.Budget))
                    {
                        row.ConstantItem(8); // gutter
                        row.RelativeItem().Column(inner =>
                        {
                            SectionLabel(inner, "Budget");
                            inner.Item().Text(_data.Budget).FontSize(10);
                        });
                    }
                });
            }

            // Target Users
            if (_data.TargetUsers?.Any() == true)
                AddTagSection(col, "Target Users", _data.TargetUsers);

            // Platforms
            if (_data.Platforms?.Any() == true)
                AddTagSection(col, "Platforms", _data.Platforms);

            // Features
            if (_data.Features?.Any() == true)
                AddBulletSection(col, "Features", _data.Features);

            // Design Requirements
            if (_data.DesignRequirements?.Any() == true)
                AddBulletSection(col, "Design Requirements", _data.DesignRequirements);

            // Technical Constraints
            if (_data.TechnicalConstraints?.Any() == true)
                AddBulletSection(col, "Technical Constraints", _data.TechnicalConstraints);

            // Client Responses
            if (_clientResponses.Any())
            {
                col.Item().PaddingBottom(6).Column(inner =>
                {
                    SectionLabel(inner, "Client Responses");
                    inner.Item()
                         .Border(1)
                         .BorderColor(SuccessHex)
                         .Table(table =>
                         {
                             table.ColumnsDefinition(cols =>
                             {
                                 cols.RelativeColumn(2);
                                 cols.RelativeColumn(5);
                             });

                             // Header row
                             table.Header(h =>
                             {
                                 h.Cell().Background(SuccessHex).Padding(5)
                                  .Text("Field").Bold().FontColor(Colors.White).FontSize(9);
                                 h.Cell().Background(SuccessHex).Padding(5)
                                  .Text("Response").Bold().FontColor(Colors.White).FontSize(9);
                             });

                             foreach (var r in _clientResponses)
                             {
                                 var label = System.Globalization.CultureInfo.CurrentCulture.TextInfo
                                     .ToTitleCase((r.FieldName ?? "").Replace("_", " "));

                                 table.Cell().BorderBottom(1).BorderColor(BorderHex).Padding(5)
                                      .Text(label).FontSize(9).Bold();
                                 table.Cell().BorderBottom(1).BorderColor(BorderHex).Padding(5)
                                      .Text(r.ResponseText ?? "").FontSize(9);
                             }
                         });
                });
            }
        });
    }

    // ── Footer ───────────────────────────────────────────────────────────
    private void ComposeFooter(IContainer c)
    {
        c.Column(col =>
        {
            col.Item().Height(1).Background(BorderHex);
            col.Item().PaddingTop(6).Row(row =>
            {
                row.RelativeItem()
                   .Text("Generated by BriefGen · AI-Powered Project Briefs")
                   .FontSize(8)
                   .FontColor(MutedHex);

                row.ConstantItem(60)
                   .AlignRight()
                   .Text(t =>
                   {
                       t.Span("Page ").FontSize(8).FontColor(MutedHex);
                       t.CurrentPageNumber().FontSize(8).FontColor(MutedHex);
                       t.Span(" of ").FontSize(8).FontColor(MutedHex);
                       t.TotalPages().FontSize(8).FontColor(MutedHex);
                   });
            });
        });
    }

    // ── Helpers ──────────────────────────────────────────────────────────
    private static void SectionLabel(ColumnDescriptor col, string label)
    {
        col.Item()
           .PaddingBottom(4)
           .BorderBottom(2)
           .BorderColor(AccentHex)
           .Text(label.ToUpperInvariant())
           .FontSize(8)
           .Bold()
           .FontColor(AccentHex)
           .LetterSpacing(0.08f);
    }

    private static void AddSection(ColumnDescriptor col, string label, string text)
    {
        col.Item().PaddingBottom(14).Column(inner =>
        {
            SectionLabel(inner, label);
            inner.Item().Text(text).FontSize(10).LineHeight(1.5f);
        });
    }

    private static void AddBulletSection(ColumnDescriptor col, string label, IEnumerable<string> items)
    {
        col.Item().PaddingBottom(14).Column(inner =>
        {
            SectionLabel(inner, label);
            foreach (var item in items)
            {
                inner.Item().Row(row =>
                {
                    row.ConstantItem(14).Text("•").FontColor(AccentHex).Bold();
                    row.RelativeItem().Text(item).FontSize(10).LineHeight(1.4f);
                });
            }
        });
    }

    private static void AddTagSection(ColumnDescriptor col, string label, IEnumerable<string> tags)
    {
        col.Item().PaddingBottom(14).Column(inner =>
        {
            SectionLabel(inner, label);
            // Use Inlined layout for wrapping tag pills
            inner.Item().PaddingTop(4).Inlined(inlined =>
            {
                inlined.Spacing(4);
                foreach (var tag in tags)
                {
                    inlined.Item()
                           .Background(LightHex)
                           .Border(1)
                           .BorderColor(BorderHex)
                           .PaddingHorizontal(8)
                           .PaddingVertical(3)
                           .Text(tag)
                           .FontSize(9)
                           .FontColor(Colors.Grey.Darken3);
                }
            });
        });
    }
}
