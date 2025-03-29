using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Members.Data;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.AspNetCore.Identity.UI.Services;
using Members.Services;
using Microsoft.Extensions.Logging;
using Members.Models; // Add this to access UserProfile

var builder = WebApplication.CreateBuilder(args);

// Retrieve connection string from environment variables
string DB_SERVER = Environment.GetEnvironmentVariable("DB_SERVER")!;
string DB_USER = Environment.GetEnvironmentVariable("DB_USER")!;
string DB_PASSWORD = Environment.GetEnvironmentVariable("DB_PASSWORD")!;
string DB_NAME = Environment.GetEnvironmentVariable("DB_NAME")!;

if (string.IsNullOrEmpty(DB_SERVER) || string.IsNullOrEmpty(DB_USER) || string.IsNullOrEmpty(DB_PASSWORD) || string.IsNullOrEmpty(DB_NAME))
{
    // Handle the error: Log, throw an exception, or provide a default value
    throw new InvalidOperationException("Database environment variables (DB_SERVER, DB_USER, DB_PASSWORD, or DB_NAME) are not set.");
}

string connectionString = $"Data Source={DB_SERVER};Initial Catalog={DB_NAME};User Id={DB_USER};Password={DB_PASSWORD}";

// Configure DbContext with retry-on-failure and connection string from env vars
builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseSqlServer(
        connectionString,
        sqlServerOptions => sqlServerOptions.EnableRetryOnFailure()
    )
);

builder.Services.AddDatabaseDeveloperPageExceptionFilter();

builder.Services.AddDefaultIdentity<IdentityUser>(options => options.SignIn.RequireConfirmedAccount = true)
    .AddRoles<IdentityRole>()
    .AddEntityFrameworkStores<ApplicationDbContext>();

// Register IEmailSender and EmailService
builder.Services.AddTransient<IEmailSender, EmailService>();
builder.Services.AddTransient<EmailService>();
builder.Services.AddScoped<UserService>();
builder.Services.AddControllersWithViews();
builder.Services.AddRazorPages();
// *** END: Your existing builder configuration (replace this comment block) ***

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseMigrationsEndPoint();
}
else
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseRouting();
app.UseAuthorization();
app.MapStaticAssets();
app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Info}/{action=Index}/{id?}")
    .WithStaticAssets();
app.MapRazorPages()
    .WithStaticAssets();


// Create the Roles if they have been deleted.
using (var scope = app.Services.CreateScope())
{
    var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();

    var roles = new[] { "Admin", "Manager", "Member" };
    foreach (var role in roles)
    {
        if (!await roleManager.RoleExistsAsync(role))
        {
            await roleManager.CreateAsync(new IdentityRole(role));
        }
    }
}

// Create the Administrator Account if it has been deleted.
using (var scope = app.Services.CreateScope())
{
    var UserManager = scope.ServiceProvider.GetRequiredService<UserManager<IdentityUser>>();
    var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>(); // Get the ApplicationDbContext

    string ADMIN_EMAIL = Environment.GetEnvironmentVariable("ADMIN_EMAIL")!;
    string ADMIN_PASSWORD = Environment.GetEnvironmentVariable("ADMIN_PASSWORD")!;

    if (string.IsNullOrEmpty(ADMIN_EMAIL) || string.IsNullOrEmpty(ADMIN_PASSWORD))
    {
        throw new InvalidOperationException("ADMIN_EMAIL or ADMIN_PASSWORD environment variables are not set.");
    }

    var adminUser = await UserManager.FindByEmailAsync(ADMIN_EMAIL);

    if (adminUser == null)
    {
        adminUser = new IdentityUser
        {
            UserName = ADMIN_EMAIL,
            Email = ADMIN_EMAIL,
            EmailConfirmed = true,
            PhoneNumber = "(217) 371-8041" // Set the Cell Phone number in AspNetUsers
        };

        var createResult = await UserManager.CreateAsync(adminUser, ADMIN_PASSWORD);
        if (createResult.Succeeded)
        {
            await UserManager.AddToRoleAsync(adminUser, "Admin");
        }
        else
        {
            var logger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();
            foreach (var error in createResult.Errors)
            {
                logger.LogError("Error creating admin user: {Description}", error.Description);
            }
            throw new Exception("Failed to create admin user.");
        }
    }
    else
    {
        // Update PhoneNumber if it's not already set
        if (string.IsNullOrEmpty(adminUser.PhoneNumber))
        {
            adminUser.PhoneNumber = "(217) 371-8041";
            await UserManager.UpdateAsync(adminUser);
        }
    }

    // Update UserProfile
    var adminProfile = await dbContext.UserProfile.FirstOrDefaultAsync(up => up.UserId == adminUser.Id);

    if (adminProfile == null)
    {
        adminProfile = new UserProfile
        {
            UserId = adminUser.Id,
            FirstName = "*",
            LastName = "Administrator",
            AddressLine1 = "1042 N Brainerd",
            City = "Avon Park",
            State = "FL",
            ZipCode = "33825",
            HomePhoneNumber = "(217) 371-8041",
            User = adminUser 
        };
        dbContext.UserProfile.Add(adminProfile);
    }
    else
    {
        adminProfile.FirstName = "*";
        adminProfile.LastName = "Administrator";
        adminProfile.AddressLine1 = "1042 N Brainerd";
        adminProfile.City = "Avon Park";
        adminProfile.State = "FL";
        adminProfile.ZipCode = "33825";
        // You can choose to update HomePhoneNumber here if needed
    }

    await dbContext.SaveChangesAsync();
}

Members.Helpers.ImageHelper.Initialize(app.Environment);

app.Run();