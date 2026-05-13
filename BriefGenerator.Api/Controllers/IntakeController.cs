using BriefGenerator.Api.Data;
using BriefGenerator.Api.Models;
using BriefGenerator.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

namespace BriefGenerator.Api.Controllers
{
    [Authorize]
    [ApiController]
    [Route("api/intake")]
    public class IntakeController : ControllerBase
    {
        private readonly AppDbContext _context;
        private readonly IServiceScopeFactory _scopeFactory;

        public IntakeController(AppDbContext context, IServiceScopeFactory scopeFactory)
        {
            _context = context;
            _scopeFactory = scopeFactory;
        }

        private Guid? CurrentUserId()
        {
            var str = User.FindFirstValue(ClaimTypes.NameIdentifier);
            return Guid.TryParse(str, out var id) ? id : null;
        }

        // ── POST /api/intake/create ───────────────────────────────────────
        [HttpPost("create")]
        public async Task<IActionResult> Create([FromForm] CreateIntakeRequest request)
        {
            var userId = CurrentUserId();
            if (userId == null) return Unauthorized();

            var intakeSession = new IntakeSession
            {
                Id = Guid.NewGuid(),
                UserId = userId.Value,
                Title = request.Title ?? "New Intake",
                Status = "uploaded",
                CreatedAt = DateTime.UtcNow
            };

            _context.IntakeSessions.Add(intakeSession);

            var uploadPath = Path.Combine(Directory.GetCurrentDirectory(), "uploads");
            if (!Directory.Exists(uploadPath))
                Directory.CreateDirectory(uploadPath);

            var uploadedFiles = new List<UploadedFile>();

            if (request.Files != null)
            {
                foreach (var file in request.Files)
                {
                    if (file.Length > 0)
                    {
                        var fileName = $"{Guid.NewGuid()}_{file.FileName}";
                        var filePath = Path.Combine(uploadPath, fileName);

                        using var stream = new FileStream(filePath, FileMode.Create);
                        await file.CopyToAsync(stream);

                        uploadedFiles.Add(new UploadedFile
                        {
                            Id = Guid.NewGuid(),
                            IntakeSessionId = intakeSession.Id,
                            FileName = file.FileName,
                            FileType = file.ContentType,
                            StoragePath = filePath
                        });
                    }
                }
            }

            if (!string.IsNullOrWhiteSpace(request.Notes))
            {
                var notesFileName = $"{Guid.NewGuid()}_notes.txt";
                var notesFilePath = Path.Combine(uploadPath, notesFileName);
                await System.IO.File.WriteAllTextAsync(notesFilePath, request.Notes);

                uploadedFiles.Add(new UploadedFile
                {
                    Id = Guid.NewGuid(),
                    IntakeSessionId = intakeSession.Id,
                    FileName = "notes.txt",
                    FileType = "text/plain",
                    StoragePath = notesFilePath
                });
            }

            _context.UploadedFiles.AddRange(uploadedFiles);
            await _context.SaveChangesAsync();

            // Fire AI pipeline in background
            Task.Run(async () =>
            {
                using var scope = _scopeFactory.CreateScope();
                var aiService = scope.ServiceProvider.GetRequiredService<IAiProcessingService>();
                try { await aiService.ProcessIntakeAsync(intakeSession.Id); }
                catch (Exception ex) { Console.WriteLine($"AI Processing Error: {ex.Message}"); }
            });

            return Ok(new { intakeId = intakeSession.Id, status = "processing" });
        }

        // ── GET /api/intake/{id} ──────────────────────────────────────────
        [HttpGet("{id}")]
        public async Task<IActionResult> GetIntake(Guid id)
        {
            var userId = CurrentUserId();
            if (userId == null) return Unauthorized();

            var intake = await _context.IntakeSessions.FindAsync(id);
            if (intake == null) return NotFound();
            if (intake.UserId != userId) return Forbid();

            var brief = await _context.Briefs.FirstOrDefaultAsync(b => b.IntakeSessionId == id);
            if (brief == null)
                return Ok(new { id = intake.Id, status = intake.Status, brief = (Brief?)null, shareToken = (string?)null, shareUrl = (string?)null });

            var shareLink = await _context.ShareLinks
                .Where(s => s.BriefId == brief.Id && s.ExpiresAt > DateTime.UtcNow)
                .OrderByDescending(s => s.ExpiresAt)
                .FirstOrDefaultAsync();

            var shareToken = shareLink?.Token;
            var shareUrl = shareToken is null ? null : $"/api/public/brief/{shareToken}";

            return Ok(new { id = intake.Id, status = intake.Status, brief, shareToken, shareUrl });
        }
    }

    public class CreateIntakeRequest
    {
        [FromForm(Name = "Title")]
        public string? Title { get; set; }

        [FromForm(Name = "Notes")]
        public string? Notes { get; set; }

        [FromForm(Name = "Files")]
        public List<IFormFile>? Files { get; set; }
    }
}
