using BriefGenerator.Api.Services;
using Microsoft.AspNetCore.Mvc;

namespace BriefGenerator.Api.Controllers
{
    [ApiController]
    [Route("internal")]
    public class InternalProcessController : ControllerBase
    {
        private readonly IServiceScopeFactory _scopeFactory;

        public InternalProcessController(IServiceScopeFactory scopeFactory)
        {
            _scopeFactory = scopeFactory;
        }

        [HttpPost("process-intake")]
        public IActionResult ProcessIntake([FromBody] ProcessIntakeRequest request)
        {
            if (request.IntakeId == Guid.Empty)
            {
                return BadRequest("Invalid intake ID.");
            }

            // Fire and forget using a new scope so the DbContext isn't disposed early
            Task.Run(async () =>
            {
                using var scope = _scopeFactory.CreateScope();
                var aiService = scope.ServiceProvider.GetRequiredService<IAiProcessingService>();
                
                try
                {
                    await aiService.ProcessIntakeAsync(request.IntakeId);
                }
                catch (Exception ex)
                {
                    // In a real app, log this error
                    Console.WriteLine($"Background processing failed: {ex.Message}");
                }
            });

            return Ok(new { message = "Processing started in the background." });
        }
    }

    public class ProcessIntakeRequest
    {
        public Guid IntakeId { get; set; }
    }
}