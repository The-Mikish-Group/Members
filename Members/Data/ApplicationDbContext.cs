using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

using Members.Models;
namespace Members.Data;

public class ApplicationDbContext(DbContextOptions<ApplicationDbContext> options) : IdentityDbContext(options)
{
    public DbSet<UserProfile> UserProfile { get; set; }
    //public DbSet<Plots> Plots { get; set; }
    //public DbSet<HOADues> HOADues { get; set; }
}
public class Plots
{
    public int Id { get; set; }
    public required string Name { get; set; }
    // Add other properties as needed
}
