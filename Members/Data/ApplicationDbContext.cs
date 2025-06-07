using Members.Models;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
namespace Members.Data
{
    public class ApplicationDbContext(DbContextOptions<ApplicationDbContext> options) : IdentityDbContext(options)
    {
        public DbSet<UserProfile> UserProfile { get; set; }
        public DbSet<PDFCategory> PDFCategories { get; set; }
        public DbSet<CategoryFile> CategoryFiles { get; set; }
        public DbSet<Members.Models.File> Files { get; set; }
        public DbSet<Invoice> Invoices { get; set; }
        public DbSet<Payment> Payments { get; set; }
        public DbSet<UserCredit> UserCredits { get; set; }
        // ... other DbSets
        protected override void OnModelCreating(ModelBuilder builder)
        {
            base.OnModelCreating(builder);
        }
    }
}
