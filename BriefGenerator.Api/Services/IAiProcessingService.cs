using BriefGenerator.Api.Models;

namespace BriefGenerator.Api.Services
{
    public interface IAiProcessingService
    {
        Task ProcessIntakeAsync(Guid intakeId);
        Task<string> ExtractTextFromFileAsync(UploadedFile file);
        Task<string> GenerateStructuredBriefAsync(string extractedText);
        Task<string> DetectMissingInformationAsync(string structuredJson);
    }
}