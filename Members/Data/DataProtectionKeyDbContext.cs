using Microsoft.AspNetCore.DataProtection.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace Members.Data
{
    public class DataProtectionKeyDbContext(DbContextOptions<DataProtectionKeyDbContext> options) : DbContext(options), IDataProtectionKeyContext
    {

        // This property maps to the table that stores data protection keys.
        public DbSet<DataProtectionKey> DataProtectionKeys { get; set; }
    }
}
