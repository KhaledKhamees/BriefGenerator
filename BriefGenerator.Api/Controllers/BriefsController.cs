using BriefGenerator.Api.Data;
using BriefGenerator.Api.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

namespace BriefGenerator.Api.Controllers
{
    [AllowAnonymous]
    [ApiController]
    [Route("api/briefs")]
    public class BriefsController : ControllerBase
    {
        private readonly AppDbContext _context;
        private static readonly Guid DevUserId = Guid.Parse("00000000-0000-0000-0000-000000000001");

        public BriefsController(AppDbContext context)
        {
            _context = context;
        }

        private async Task<Guid> GetUserIdAsync()
        {
            var str = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (Guid.TryParse(str, out var id)) return id;

            // Fallback: dev user
            await EnsureDevUserAsync();
            return DevUserId;
        }

        // GET /api/briefs
        [HttpGet]
        public async Task<IActionResult> GetEmployeeBriefs()
        {
            var userId = await GetUserIdAsync();

            var briefs = await _context.Briefs
                .Include(b => b.intakeSession)
                .Where(b => b.intakeSession.UserId == userId)
                .OrderByDescending(b => b.intakeSession.CreatedAt)
                .ToListAsync();

            return Ok(briefs);
        }

        // GET /api/briefs/{id}
        [HttpGet("{id}")]
        public async Task<IActionResult> GetBrief(Guid id)
        {
            var brief = await _context.Briefs
                .Include(b => b.intakeSession)
                .FirstOrDefaultAsync(b => b.Id == id);

            if (brief == null) return NotFound();
            return Ok(brief);
        }

        // GET /api/briefs/{id}/share-link
        [HttpGet("{id}/share-link")]
        public async Task<IActionResult> GetOrCreateShareLink(Guid id)
        {
            var brief = await _context.Briefs.FindAsync(id);
            if (brief == null) return NotFound();

            var existing = await _context.ShareLinks
                .Where(s => s.BriefId == id && s.ExpiresAt > DateTime.UtcNow)
                .OrderByDescending(s => s.ExpiresAt)
                .FirstOrDefaultAsync();

            if (existing != null)
                return Ok(new { url = $"/api/public/brief/{existing.Token}", token = existing.Token, expiresAt = existing.ExpiresAt });

            var shareLink = new ShareLink
            {
                Id = Guid.NewGuid(),
                BriefId = id,
                Token = Guid.NewGuid().ToString("N"),
                ExpiresAt = DateTime.UtcNow.AddDays(7)
            };

            _context.ShareLinks.Add(shareLink);
            await _context.SaveChangesAsync();

            return Ok(new { url = $"/api/public/brief/{shareLink.Token}", token = shareLink.Token, expiresAt = shareLink.ExpiresAt });
        }

        // GET /api/briefs/{id}/responses
        [HttpGet("{id}/responses")]
        public async Task<IActionResult> GetClientResponses(Guid id)
        {
            var exists = await _context.Briefs.AnyAsync(b => b.Id == id);
            if (!exists) return NotFound();

            var responses = await _context.ClientResponses
                .Where(r => r.BriefId == id)
                .OrderByDescending(r => r.Id)
                .ToListAsync();

            return Ok(responses);
        }

        // PATCH /api/briefs/{id}
        [HttpPatch("{id}")]
        public async Task<IActionResult> UpdateBrief(Guid id, [FromBody] UpdateBriefRequest request)
        {
            var brief = await _context.Briefs.FindAsync(id);
            if (brief == null) return NotFound();

            if (request.StructuredJson != null) brief.StructuredJson = request.StructuredJson;
            if (request.MarkdownOutput != null) brief.MarkdownOutput = request.MarkdownOutput;

            await _context.SaveChangesAsync();
            return Ok(brief);
        }

        private async Task EnsureDevUserAsync()
        {
            if (!await _context.Users.AnyAsync(u => u.Id == DevUserId))
            {
                _context.Users.Add(new User
                {
                    Id = DevUserId,
                    Name = "Dev User",
                    Email = "dev@briefgen.local",
                    PasswordHash = "dev"
                });
                await _context.SaveChangesAsync();
            }
        }
    }

    public class UpdateBriefRequest
    {
        public string? StructuredJson { get; set; }
        public string? MarkdownOutput { get; set; }
    }
}
