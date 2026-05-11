using BriefGenerator.Api.Data;
using BriefGenerator.Api.Models;
using BriefGenerator.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

namespace BriefGenerator.Api.Controllers
{
    // [Authorize] <-- Commented out for dev hack
    [AllowAnonymous]
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

        [HttpPost("create")]
        public async Task<IActionResult> Create([FromForm] CreateIntakeRequest request)
        {
            var userIdStr = User.FindFirstValue(ClaimTypes.NameIdentifier);
            
            // DEV HACK: Fallback user ID
            if (!Guid.TryParse(userIdStr, out var userId))
            {
                userId = Guid.Parse("00000000-0000-0000-0000-000000000001");
                await EnsureDummyUserExistsAsync(userId);
            }

            var intakeSession = new IntakeSession
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                Title = request.Title ?? "New Intake",
                Status = "uploaded",
                CreatedAt = DateTime.UtcNow
            };

            _context.IntakeSessions.Add(intakeSession);

            var uploadPath = Path.Combine(Directory.GetCurrentDirectory(), "uploads");
            if (!Directory.Exists(uploadPath))
                Directory.CreateDirectory(uploadPath);

            var uploadedFiles = new List<UploadedFile>();

            // Handle Files
            if (request.Files != null)
            {
                foreach (var file in request.Files)
                {
                    if (file.Length > 0)
                    {
                        var fileName = $"{Guid.NewGuid()}_{file.FileName}";
                        var filePath = Path.Combine(uploadPath, fileName);

                        using (var stream = new FileStream(filePath, FileMode.Create))
                        {
                            await file.CopyToAsync(stream);
                        }

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

            // Treat plain text notes as a file
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

            // Trigger AI Pipeline in the background
            Task.Run(async () =>
            {
                using var scope = _scopeFactory.CreateScope();
                var aiService = scope.ServiceProvider.GetRequiredService<IAiProcessingService>();
                try
                {
                    await aiService.ProcessIntakeAsync(intakeSession.Id);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"AI Processing Error: {ex.Message}");
                }
            });

            return Ok(new { intakeId = intakeSession.Id, status = "processing" });
        }

        [HttpGet("{id}")]
        public async Task<IActionResult> GetIntake(Guid id)
        {
            var intake = await _context.IntakeSessions.FindAsync(id);
            if (intake == null) return NotFound();

            var brief = _context.Briefs.FirstOrDefault(b => b.IntakeSessionId == id);
            return Ok(new { id = intake.Id, status = intake.Status, brief });
        }

        // DEV HACK: Helper to auto-create the dummy user
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

    public class CreateIntakeRequest
    {
        public string? Title { get; set; }
        public string? Notes { get; set; }
        public List<IFormFile>? Files { get; set; }
    }
}