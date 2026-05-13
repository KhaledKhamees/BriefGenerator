using BriefGenerator.Api.Data;
using BriefGenerator.Api.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

namespace BriefGenerator.Api.Controllers
{
    //[Authorize]
    [AllowAnonymous] // DEV HACK: Bypass Auth
    [ApiController]
    [Route("api/briefs")]
    public class BriefsController : ControllerBase
    {
        private readonly AppDbContext _context;

        public BriefsController(AppDbContext context)
        {
            _context = context;
        }

        [HttpGet]
        public async Task<IActionResult> GetEmployeeBriefs()
        {
            var userIdStr = User.FindFirstValue(ClaimTypes.NameIdentifier);
            
            // DEV HACK: Fallback user ID
            if (!Guid.TryParse(userIdStr, out var userId))
            {
                userId = Guid.Parse("00000000-0000-0000-0000-000000000001");
                await EnsureDummyUserExistsAsync(userId);
            }

            var intakeIds = await _context.IntakeSessions
                .Where(i => i.UserId == userId)
                .Select(i => i.Id)
                .ToListAsync();

            var briefs = await _context.Briefs
                .Include(b => b.intakeSession)
                .Where(b => intakeIds.Contains(b.IntakeSessionId))
                .OrderByDescending(b => b.intakeSession.CreatedAt)
                .ToListAsync();

            return Ok(briefs);
        }

        [HttpGet("{id}")]
        public async Task<IActionResult> GetBrief(Guid id)
        {
            var brief = await _context.Briefs.FindAsync(id);
            if (brief == null) return NotFound();
            return Ok(brief);
        }

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
            {
                return Ok(new { url = $"/api/public/brief/{existing.Token}", token = existing.Token, expiresAt = existing.ExpiresAt });
            }

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

        [HttpPatch("{id}")]
        public async Task<IActionResult> UpdateBrief(Guid id, [FromBody] UpdateBriefRequest request)
        {
            var brief = await _context.Briefs.FindAsync(id);
            if (brief == null) return NotFound();

            if (request.StructuredJson != null)
                brief.StructuredJson = request.StructuredJson;
            
            if (request.MarkdownOutput != null)
                brief.MarkdownOutput = request.MarkdownOutput;

            await _context.SaveChangesAsync();
            return Ok(brief);
        }

        private async Task EnsureDummyUserExistsAsync(Guid userId)
        {
            if (!await _context.Users.AnyAsync(u => u.Id == userId))
            {
                _context.Users.Add(new User
                {
                    Id = userId,
                    Name = "Dev Dummy",
                    Email = "dummy@dev.local",
                    PasswordHash = "dummy"
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