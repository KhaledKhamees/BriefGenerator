using BriefGenerator.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace BriefGenerator.Api.Data
{
    public class AppDbContext : DbContext
    {
        public AppDbContext(DbContextOptions<AppDbContext> dbContextOptions): base(dbContextOptions) { }
        public DbSet<User> Users { get; set; }
        public DbSet<Brief> Briefs { get; set; }
        public DbSet<ClientResponse> ClientResponses { get; set; }
        public DbSet<IntakeSession> IntakeSessions { get; set; }
        public DbSet<ShareLink> ShareLinks { get; set; }
        public DbSet<UploadedFile> UploadedFiles { get; set; }
    }
}
