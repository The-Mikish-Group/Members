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
        public DbSet<BillableAsset> BillableAssets { get; set; }
        // ... other DbSets
        protected override void OnModelCreating(ModelBuilder builder)
        {
            base.OnModelCreating(builder);

            builder.Entity<BillableAsset>()
                .HasIndex(ba => ba.PlotID)
                .IsUnique();
            
            builder.Entity<BillableAsset>()
                .HasOne(ba => ba.User)
                .WithMany() // Assuming IdentityUser doesn't have a direct navigation collection for BillableAssets
                .HasForeignKey(ba => ba.UserID)
                .IsRequired(false) // Makes the relationship optional (UserID can be null)
                .OnDelete(DeleteBehavior.SetNull); // If a User is deleted, set BillableAsset.UserID to null
        }
    }
}
