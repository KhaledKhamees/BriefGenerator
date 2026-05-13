using BriefGenerator.Api.Data;
using BriefGenerator.Api.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

namespace BriefGenerator.Api.Controllers
{
    [Authorize]
    [ApiController]
    [Route("api/briefs")]
    public class BriefsController : ControllerBase
    {
        private readonly AppDbContext _context;

        public BriefsController(AppDbContext context)
        {
            _context = context;
        }

        // ── Helpers ───────────────────────────────────────────────────────

        /// <summary>Returns the authenticated user's ID, or null if the claim is missing.</summary>
        private Guid? CurrentUserId()
        {
            var str = User.FindFirstValue(ClaimTypes.NameIdentifier);
            return Guid.TryParse(str, out var id) ? id : null;
        }

        /// <summary>
        /// Returns true if the brief with the given ID belongs to the current user.
        /// Avoids loading the full entity when we only need the ownership check.
        /// </summary>
        private async Task<bool> BriefBelongsToCurrentUserAsync(Guid briefId, Guid userId)
        {
            return await _context.Briefs
                .AnyAsync(b => b.Id == briefId && b.intakeSession.UserId == userId);
        }

        // ── GET /api/briefs ───────────────────────────────────────────────
        [HttpGet]
        public async Task<IActionResult> GetEmployeeBriefs()
        {
            var userId = CurrentUserId();
            if (userId == null) return Unauthorized();

            var briefs = await _context.Briefs
                .Include(b => b.intakeSession)
                .Where(b => b.intakeSession.UserId == userId)
                .OrderByDescending(b => b.intakeSession.CreatedAt)
                .ToListAsync();

            return Ok(briefs);
        }

        // ── GET /api/briefs/{id} ──────────────────────────────────────────
        [HttpGet("{id}")]
        public async Task<IActionResult> GetBrief(Guid id)
        {
            var userId = CurrentUserId();
            if (userId == null) return Unauthorized();

            var brief = await _context.Briefs
                .Include(b => b.intakeSession)
                .FirstOrDefaultAsync(b => b.Id == id);

            if (brief == null) return NotFound();
            if (brief.intakeSession.UserId != userId) return Forbid();

            return Ok(brief);
        }

        // ── GET /api/briefs/{id}/share-link ───────────────────────────────
        [HttpGet("{id}/share-link")]
        public async Task<IActionResult> GetOrCreateShareLink(Guid id)
        {
            var userId = CurrentUserId();
            if (userId == null) return Unauthorized();

            if (!await BriefBelongsToCurrentUserAsync(id, userId.Value))
                return NotFound();   // don't reveal existence to other users

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

        // ── GET /api/briefs/{id}/responses ────────────────────────────────
        [HttpGet("{id}/responses")]
        public async Task<IActionResult> GetClientResponses(Guid id)
        {
            var userId = CurrentUserId();
            if (userId == null) return Unauthorized();

            if (!await BriefBelongsToCurrentUserAsync(id, userId.Value))
                return NotFound();

            var responses = await _context.ClientResponses
                .Where(r => r.BriefId == id)
                .OrderByDescending(r => r.Id)
                .ToListAsync();

            return Ok(responses);
        }

        // ── PATCH /api/briefs/{id} ────────────────────────────────────────
        [HttpPatch("{id}")]
        public async Task<IActionResult> UpdateBrief(Guid id, [FromBody] UpdateBriefRequest request)
        {
            var userId = CurrentUserId();
            if (userId == null) return Unauthorized();

            var brief = await _context.Briefs
                .Include(b => b.intakeSession)
                .FirstOrDefaultAsync(b => b.Id == id);

            if (brief == null) return NotFound();
            if (brief.intakeSession.UserId != userId) return Forbid();

            if (request.StructuredJson != null)
                brief.StructuredJson = request.StructuredJson;

            if (request.MarkdownOutput != null)
                brief.MarkdownOutput = request.MarkdownOutput;

            await _context.SaveChangesAsync();
            return Ok(brief);
        }
    }

    public class UpdateBriefRequest
    {
        public string? StructuredJson { get; set; }
        public string? MarkdownOutput { get; set; }
    }
}
