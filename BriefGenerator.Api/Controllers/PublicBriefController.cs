using BriefGenerator.Api.Data;
using BriefGenerator.Api.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace BriefGenerator.Api.Controllers
{
    [ApiController]
    [Route("api/public/brief")]
    public class PublicBriefController : ControllerBase
    {
        private readonly AppDbContext _context;

        public PublicBriefController(AppDbContext context)
        {
            _context = context;
        }

        // Internally used by the employee to generate the client-facing link
        [HttpPost("generate-link/{briefId}")]
        public async Task<IActionResult> GenerateLink(Guid briefId)
        {
            var brief = await _context.Briefs.FindAsync(briefId);
            if (brief == null) return NotFound("Brief not found.");

            var existing = await _context.ShareLinks
                .Where(s => s.BriefId == briefId && s.ExpiresAt > DateTime.UtcNow)
                .OrderByDescending(s => s.ExpiresAt)
                .FirstOrDefaultAsync();

            if (existing != null)
            {
                return Ok(new { url = $"/api/public/brief/{existing.Token}", token = existing.Token, expiresAt = existing.ExpiresAt });
            }

            var shareLink = new ShareLink
            {
                Id = Guid.NewGuid(),
                BriefId = briefId,
                Token = Guid.NewGuid().ToString("N"), // Cryptographically random token
                ExpiresAt = DateTime.UtcNow.AddDays(7)
            };

            _context.ShareLinks.Add(shareLink);
            await _context.SaveChangesAsync();

            return Ok(new { url = $"/api/public/brief/{shareLink.Token}", token = shareLink.Token, expiresAt = shareLink.ExpiresAt });
        }

        [HttpGet("{token}")]
        public async Task<IActionResult> GetPublicBrief(string token)
        {
            var shareLink = await _context.ShareLinks.Include(s => s.Brief).FirstOrDefaultAsync(s => s.Token == token);
            if (shareLink == null || shareLink.ExpiresAt < DateTime.UtcNow)
                return NotFound(new { error = "Invalid or expired link" });

            return Ok(shareLink.Brief);
        }

        [HttpPost("{token}/answers")]
        public async Task<IActionResult> SubmitAnswers(string token, [FromBody] SubmitAnswersRequest request)
        {
            var shareLink = await _context.ShareLinks
                .Include(s => s.Brief)
                .FirstOrDefaultAsync(s => s.Token == token);
            if (shareLink == null || shareLink.ExpiresAt < DateTime.UtcNow)
                return NotFound(new { error = "Invalid or expired link" });

            var responses = request.Responses.Select(resp => new ClientResponse
            {
                Id = Guid.NewGuid(),
                BriefId = shareLink.BriefId,
                FieldName = resp.Field,
                ResponseText = resp.Value
            }).ToList();

            _context.ClientResponses.AddRange(responses);

            // Mark brief/intake as having client feedback (employee should review/merge).
            if (shareLink.Brief != null)
            {
                shareLink.Brief.Status = "client_responded";
                var intake = await _context.IntakeSessions.FindAsync(shareLink.Brief.IntakeSessionId);
                if (intake != null)
                {
                    intake.Status = "client_responded";
                }
            }

            await _context.SaveChangesAsync();

            return Ok(new { message = "Answers submitted successfully." });
        }

        [HttpPost("{token}/confirm")]
        public async Task<IActionResult> ConfirmBrief(string token)
        {
            var shareLink = await _context.ShareLinks.Include(s => s.Brief).FirstOrDefaultAsync(s => s.Token == token);
            if (shareLink == null || shareLink.ExpiresAt < DateTime.UtcNow)
                return NotFound(new { error = "Invalid or expired link" });

            shareLink.Brief.Status = "confirmed";
            
            var intake = await _context.IntakeSessions.FindAsync(shareLink.Brief.IntakeSessionId);
            if (intake != null)
            {
                intake.Status = "confirmed";
            }

            await _context.SaveChangesAsync();

            return Ok(new { message = "Brief confirmed." });
        }
    }

    public class SubmitAnswersRequest
    {
        public List<ClientAnswer> Responses { get; set; } = new();
    }

    public class ClientAnswer
    {
        public string Field { get; set; } = string.Empty;
        public string Value { get; set; } = string.Empty;
    }
}