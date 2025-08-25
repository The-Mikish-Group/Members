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
        public DbSet<CreditApplication> CreditApplications { get; set; }
        public DbSet<ColorVar> ColorVars { get; set; }
        
        // More Links System DbSets
        public DbSet<LinkCategory> LinkCategories { get; set; }
        public DbSet<CategoryLink> CategoryLinks { get; set; }

        // Task System DbSets
        public DbSet<AdminTask> AdminTasks { get; set; }
        public DbSet<AdminTaskInstance> AdminTaskInstances { get; set; }
        public DbSet<TaskStatusMessage> TaskStatusMessages { get; set; }

        protected override void OnModelCreating(ModelBuilder builder)
        {
            base.OnModelCreating(builder);

            builder.Entity<BillableAsset>()
                .HasIndex(ba => ba.PlotID)
                .IsUnique();

            builder.Entity<BillableAsset>()
                .HasOne(ba => ba.User)
                .WithMany()
                .HasForeignKey(ba => ba.UserID)
                .IsRequired(false)
                .OnDelete(DeleteBehavior.SetNull);

            // Configure CreditApplication relationships
            builder.Entity<CreditApplication>(entity =>
            {
                entity.HasOne(ca => ca.UserCredit)
                    .WithMany()
                    .HasForeignKey(ca => ca.UserCreditID)
                    .OnDelete(DeleteBehavior.Restrict);

                entity.HasOne(ca => ca.Invoice)
                    .WithMany()
                    .HasForeignKey(ca => ca.InvoiceID)
                    .OnDelete(DeleteBehavior.Restrict);
            });

            // Configure Task System relationships
            builder.Entity<AdminTaskInstance>(entity =>
            {
                entity.HasOne(ati => ati.AdminTask)
                    .WithMany(at => at.TaskInstances)
                    .HasForeignKey(ati => ati.TaskID)
                    .OnDelete(DeleteBehavior.Cascade);

                entity.HasOne(ati => ati.AssignedToUser)
                    .WithMany()
                    .HasForeignKey(ati => ati.AssignedToUserId)
                    .OnDelete(DeleteBehavior.SetNull);

                entity.HasOne(ati => ati.CompletedByUser)
                    .WithMany()
                    .HasForeignKey(ati => ati.CompletedByUserId)
                    .OnDelete(DeleteBehavior.SetNull);

                // Ensure unique constraint for Task + Year + Month
                entity.HasIndex(ati => new { ati.TaskID, ati.Year, ati.Month })
                    .IsUnique();
            });

            builder.Entity<TaskStatusMessage>(entity =>
            {
                entity.HasOne(tsm => tsm.User)
                    .WithMany()
                    .HasForeignKey(tsm => tsm.UserId)
                    .OnDelete(DeleteBehavior.Cascade);
            });
        }
    }
}