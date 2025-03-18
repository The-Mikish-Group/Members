using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Members.Data;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.AspNetCore.Identity.UI.Services;
using Members.Services;
using Microsoft.Extensions.Logging;

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

// Register IEmailSender and EmailService correctly
builder.Services.AddTransient<IEmailSender, EmailService>();
builder.Services.AddTransient<EmailService>();

builder.Services.AddControllersWithViews();
builder.Services.AddRazorPages();

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

using (var scope = app.Services.CreateScope())
{
    var UserManager = scope.ServiceProvider.GetRequiredService<UserManager<IdentityUser>>();

    string ADMIN_EMAIL = Environment.GetEnvironmentVariable("ADMIN_EMAIL")!;
    string ADMIN_PASSWORD = Environment.GetEnvironmentVariable("ADMIN_PASSWORD")!;

    if (string.IsNullOrEmpty(ADMIN_EMAIL) || string.IsNullOrEmpty(ADMIN_PASSWORD))
    {
        throw new InvalidOperationException("ADMIN_EMAIL or ADMIN_PASSWORD environment variables are not set.");
    }

    if (await UserManager.FindByEmailAsync(ADMIN_EMAIL) == null)
    {
        var user = new IdentityUser
        {
            UserName = ADMIN_EMAIL,
            Email = ADMIN_EMAIL,
            EmailConfirmed = true
        };

        await UserManager.CreateAsync(user, ADMIN_PASSWORD);

        await UserManager.AddToRoleAsync(user, "Admin");
    }
}

Members.Helpers.ImageHelper.Initialize(app.Environment);

app.Run();