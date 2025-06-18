using Members.Data;
using Members.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
namespace Members.Areas.Identity.Pages
{
    // Using the primary constructor for dependency injection
    public class UsersModel(UserManager<IdentityUser> userManager, ApplicationDbContext dbContext) : PageModel
    {
        private readonly UserManager<IdentityUser> _userManager = userManager;
        private readonly ApplicationDbContext _dbContext = dbContext; // Inject ApplicationDbContext
        public class UserModel
        {
            public required string Id { get; set; }
            public required string UserName { get; set; }
            public string? FullName { get; set; }
            public required string Email { get; set; }
            public bool EmailConfirmed { get; set; }
            public string? PhoneNumber { get; set; }
            public bool PhoneNumberConfirmed { get; set; }
            public IList<string>? Roles { get; set; }
            public DateTime? LastLogin { get; set; }
            // --- New UserProfile Fields ---
            public string? FirstName { get; set; }
            public string? MiddleName { get; set; }
            public string? LastName { get; set; }
            public string? HomePhoneNumber { get; set; }
            public string? AddressLine1 { get; set; }
            public string? AddressLine2 { get; set; }
            public string? City { get; set; }
            public string? State { get; set; }
            public string? ZipCode { get; set; }
            public string? Plot { get; set; }
            public DateTime? Birthday { get; set; }
            public DateTime? Anniversary { get; set; }
            public bool IsBillingContact { get; set; }
            public decimal CurrentBalance { get; set; }
            // --- End New UserProfile Fields ---
        }
        // Property to hold the users for the current page
        public required List<UserModel> Users { get; set; } = []; // Initialize with an empty list
        // Pagination Properties
        [BindProperty(SupportsGet = true)]
        public int PageNumber { get; set; } = 1; // Default to the first page
        [BindProperty(SupportsGet = true)]
        public int PageSize { get; set; } = 20; // Default page size
        public int TotalUsers { get; set; } // Total number of users after filtering
        public int TotalPages { get; set; } // Total number of pages
        // Sorting Properties
        [BindProperty(SupportsGet = true)]
        public required string? SortColumn { get; set; } = null;
        [BindProperty(SupportsGet = true)]
        public required string? SortOrder { get; set; } = null;
        // Search Property
        [BindProperty(SupportsGet = true)]
        public required string? SearchTerm { get; set; } = null;
        // --- New Toggle Property ---
        [BindProperty(SupportsGet = true)]
        public bool ShowExtraFields { get; set; } = false; // Default to false
        // --- End New Toggle Property ---
        // --- Your Original OnGetAsync (with ShowExtraFields added) ---
        // This handles the initial full page load.
        public async Task<IActionResult> OnGetAsync(string? searchTerm, int pageNumber = 1, int pageSize = 20, string? sortColumn = null, string? sortOrder = null, bool showExtraFields = false)
        {
            SearchTerm = searchTerm;
            PageNumber = pageNumber;
            PageSize = pageSize;
            SortColumn = sortColumn;
            SortOrder = sortOrder;
            ShowExtraFields = showExtraFields;
            await LoadUsersDataAsync(); // Use the helper to load data for the initial page
            return Page(); // Return the full page
        }
        // --- End Original OnGetAsync ---
        // --- New Handler for AJAX Requests ---
        // This handler will be targeted by the JavaScript fetch call using ?handler=PartialTable
        public async Task<PartialViewResult> OnGetPartialTableAsync(string? searchTerm, int pageNumber = 1, int pageSize = 20, string? sortColumn = null, string? sortOrder = null, bool showExtraFields = false)
        {
            SearchTerm = searchTerm;
            PageNumber = pageNumber;
            PageSize = pageSize;
            SortColumn = sortColumn;
            SortOrder = sortOrder;
            ShowExtraFields = showExtraFields;
            // Use the data loading helper
            await LoadUsersDataAsync();
            // Return a partial view. The path should be relative to the directory of Users.cshtml.
            // If _UsersTablePartial.cshtml is in the same directory, "./_UsersTablePartial" is correct.
            // If it's in a shared location (e.g., /Pages/Shared/), adjust the path accordingly.
            return Partial("_UsersTablePartial", this);
        }
        // --- End New Handler ---
        // --- Helper method to load users based on current properties (reused logic) ---
        private async Task LoadUsersDataAsync() // Renamed from LoadUsersAsync to differentiate
        {
            // Start with the base query for Identity Users
            IQueryable<IdentityUser> usersQuery = _userManager.Users.AsQueryable();
            // --- Apply Filtering based on SearchTerm ---
            if (!string.IsNullOrEmpty(SearchTerm))
            {
                string searchTerm = SearchTerm.Trim().ToLower(); // Convert to lowercase for case-insensitive comparison
                // Start building the filter condition
                var filterCondition = PredicateBuilder.False<IdentityUser>();
                if (searchTerm.Equals("bad", StringComparison.OrdinalIgnoreCase))
                {
                    // Filter for users with no roles and unconfirmed email
                    filterCondition = filterCondition.Or(u => !_dbContext.UserRoles.Any(ur => ur.UserId == u.Id) && !u.EmailConfirmed);
                }
                else if (searchTerm.Equals("no role", StringComparison.OrdinalIgnoreCase))
                {
                    // Filter for users with no roles
                    filterCondition = filterCondition.Or(u => !_dbContext.UserRoles.Any(ur => ur.UserId == u.Id));
                }
                else if (searchTerm.Equals("not confirmed", StringComparison.OrdinalIgnoreCase))
                {
                    // Filter for users with unconfirmed email
                    filterCondition = filterCondition.Or(u => !u.EmailConfirmed);
                }
                else if (searchTerm.Equals("billable", StringComparison.OrdinalIgnoreCase))                              
                {
                    filterCondition = filterCondition.Or(u => _dbContext.UserProfile.Any(up => up.UserId == u.Id && (up.IsBillingContact)));
                    //filterCondition = filterCondition.Or(u => _dbContext.UserProfile.Any(ur => ur.IsBillingContact));                    
                }
                else
                {
                    // Standard search across multiple fields
                    filterCondition = filterCondition.Or(u => u.Email != null && u.Email.ToLower().Contains(searchTerm)); // Case-insensitive
                    filterCondition = filterCondition.Or(u => u.PhoneNumber != null && u.PhoneNumber.ToLower().Contains(searchTerm)); // Case-insensitive
                    filterCondition = filterCondition.Or(u => _dbContext.UserProfile.Any(up => up.UserId == u.Id &&
                        (                            
                            (up.FirstName != null && up.FirstName.ToLower().Contains(searchTerm)) ||
                            (up.MiddleName != null && up.MiddleName.ToLower().Contains(searchTerm)) ||
                            (up.LastName != null && up.LastName.ToLower().Contains(searchTerm)) ||
                            (up.HomePhoneNumber != null && up.HomePhoneNumber.ToLower().Contains(searchTerm)) ||
                            // --- Add new fields to search filter if ShowExtraFields is true ---
                            (ShowExtraFields && up.AddressLine1 != null && up.AddressLine1.ToLower().Contains(searchTerm)) ||
                            (ShowExtraFields && up.AddressLine2 != null && up.AddressLine2.ToLower().Contains(searchTerm)) ||
                            (ShowExtraFields && up.City != null && up.City.ToLower().Contains(searchTerm)) ||
                            (ShowExtraFields && up.State != null && up.State.ToLower().Contains(searchTerm)) ||
                            (ShowExtraFields && up.ZipCode != null && up.ZipCode.ToLower().Contains(searchTerm)) ||
                            (ShowExtraFields && up.Plot != null && up.Plot.ToLower().Contains(searchTerm))
                        // --- End new fields to search filter ---
                        )
                    ));
                    // Search in Roles
                    filterCondition = filterCondition.Or(u => _dbContext.UserRoles.Any(ur => ur.UserId == u.Id &&
                                                                _dbContext.Roles.Any(r => r.Id == ur.RoleId && r.Name != null && r.Name.ToLower().Contains(searchTerm))));
                }
                // Apply the combined filter condition to the users query
                usersQuery = usersQuery.Where(filterCondition);
            }
            // --- End Apply Filtering ---
            // Get the total count *after* applying filters but *before* joining for selection
            TotalUsers = await usersQuery.CountAsync();
            // Calculate Total Pages
            TotalPages = (int)Math.Ceiling(TotalUsers / (double)PageSize);
            // Ensure PageNumber is within valid range
            if (PageNumber < 1)
            {
                PageNumber = 1;
            }
            else if (PageNumber > TotalPages && TotalPages > 0)
            {
                PageNumber = TotalPages;
            }
            else if (TotalPages == 0) // Handle case with no users matching filter
            {
                PageNumber = 1; // Or 0, depending on desired behavior for empty results
            }
            // --- Join with UserProfile and Select Data ---
            // Perform a left join to include users without a UserProfile
            var joinedQuery = usersQuery.GroupJoin(
                _dbContext.UserProfile,
                user => user.Id,
                userProfile => userProfile.UserId,
                (user, userProfiles) => new { User = user, UserProfile = userProfiles.FirstOrDefault() }
            ).AsQueryable();
            // --- Apply Sorting to the Joined Data ---
            IOrderedQueryable<dynamic> orderedQuery;
            // Default sort if no column is specified, or if a new column is requested but ShowExtraFields is false
            if (string.IsNullOrEmpty(SortColumn) || (!ShowExtraFields && IsExtraFieldSortColumn(SortColumn)))
            {
                // Default sort by Full Name (LastName then FirstName) using UserProfile, handle null UserProfile
                orderedQuery = joinedQuery.OrderBy(x => x.UserProfile != null ? x.UserProfile.LastName : null)
                                          .ThenBy(x => x.UserProfile != null ? x.UserProfile.FirstName : null);
            }
            else
            {
                // Apply specified sort column
                orderedQuery = SortColumn.ToLower() switch
                {
                    "fullname" => SortOrder?.ToLower() == "asc" ? joinedQuery.OrderBy(x => x.UserProfile != null ? x.UserProfile.LastName : null).ThenBy(x => x.UserProfile != null ? x.UserProfile.FirstName : null) : joinedQuery.OrderByDescending(x => x.UserProfile != null ? x.UserProfile.LastName : null).ThenByDescending(x => x.UserProfile != null ? x.UserProfile.FirstName : null),
                    "email" => SortOrder?.ToLower() == "asc" ? joinedQuery.OrderBy(x => x.User.Email) : joinedQuery.OrderByDescending(x => x.User.Email),
                    "emailconfirmed" => SortOrder?.ToLower() == "asc" ? joinedQuery.OrderBy(x => x.User.EmailConfirmed) : joinedQuery.OrderByDescending(x => x.User.EmailConfirmed),
                    "phonenumber" => SortOrder?.ToLower() == "asc" ? joinedQuery.OrderBy(x => x.User.PhoneNumber) : joinedQuery.OrderByDescending(x => x.User.PhoneNumber), // Sort by IdentityUser's PhoneNumber
                    "homephonenumber" => SortOrder?.ToLower() == "asc" ? joinedQuery.OrderBy(x => x.UserProfile != null ? x.UserProfile.HomePhoneNumber : null) : joinedQuery.OrderByDescending(x => x.UserProfile != null ? x.UserProfile.HomePhoneNumber : null), // Sort by UserProfile's HomePhoneNumber
                    "phonenumberconfirmed" => SortOrder?.ToLower() == "asc" ? joinedQuery.OrderBy(x => x.User.PhoneNumberConfirmed) : joinedQuery.OrderByDescending(x => x.User.PhoneNumberConfirmed),
                    "roles" => SortOrder?.ToLower() == "asc" ? joinedQuery.OrderBy(x => _dbContext.UserRoles.Where(ur => ur.UserId == x.User.Id).Join(_dbContext.Roles, ur => ur.RoleId, r => r.Id, (ur, r) => r.Name).FirstOrDefault()) : joinedQuery.OrderByDescending(x => _dbContext.UserRoles.Where(ur => ur.UserId == x.User.Id).Join(_dbContext.Roles, ur => ur.RoleId, r => r.Id, (ur, r) => r.Name).FirstOrDefault()),
                    "lastlogin" => SortOrder?.ToLower() == "desc" ? joinedQuery.OrderBy(x => x.UserProfile != null ? x.UserProfile.LastLogin : null) : joinedQuery.OrderByDescending(x => x.UserProfile != null ? x.UserProfile.LastLogin : null), // Sort by UserProfile's LastLogin
                    // --- Add sorting for new fields ---
                    "firstname" => SortOrder?.ToLower() == "asc" ? joinedQuery.OrderBy(x => x.UserProfile != null ? x.UserProfile.FirstName : null) : joinedQuery.OrderByDescending(x => x.UserProfile != null ? x.UserProfile.FirstName : null),
                    "middlename" => SortOrder?.ToLower() == "asc" ? joinedQuery.OrderBy(x => x.UserProfile != null ? x.UserProfile.MiddleName : null) : joinedQuery.OrderByDescending(x => x.UserProfile != null ? x.UserProfile.MiddleName : null),
                    "lastname" => SortOrder?.ToLower() == "asc" ? joinedQuery.OrderBy(x => x.UserProfile != null ? x.UserProfile.LastName : null) : joinedQuery.OrderByDescending(x => x.UserProfile != null ? x.UserProfile.LastName : null),
                    "addressline1" => SortOrder?.ToLower() == "asc" ? joinedQuery.OrderBy(x => x.UserProfile != null ? x.UserProfile.AddressLine1 : null) : joinedQuery.OrderByDescending(x => x.UserProfile != null ? x.UserProfile.AddressLine1 : null),
                    "addressline2" => SortOrder?.ToLower() == "asc" ? joinedQuery.OrderBy(x => x.UserProfile != null ? x.UserProfile.AddressLine2 : null) : joinedQuery.OrderByDescending(x => x.UserProfile != null ? x.UserProfile.AddressLine2 : null),
                    "city" => SortOrder?.ToLower() == "asc" ? joinedQuery.OrderBy(x => x.UserProfile != null ? x.UserProfile.City : null) : joinedQuery.OrderByDescending(x => x.UserProfile != null ? x.UserProfile.City : null),
                    "state" => SortOrder?.ToLower() == "asc" ? joinedQuery.OrderBy(x => x.UserProfile != null ? x.UserProfile.State : null) : joinedQuery.OrderByDescending(x => x.UserProfile != null ? x.UserProfile.State : null),
                    "zipcode" => SortOrder?.ToLower() == "asc" ? joinedQuery.OrderBy(x => x.UserProfile != null ? x.UserProfile.ZipCode : null) : joinedQuery.OrderByDescending(x => x.UserProfile != null ? x.UserProfile.ZipCode : null),
                    "plot" => SortOrder?.ToLower() == "asc" ? joinedQuery.OrderBy(x => x.UserProfile != null ? x.UserProfile.Plot : null) : joinedQuery.OrderByDescending(x => x.UserProfile != null ? x.UserProfile.Plot : null),
                    "birthday" => SortOrder?.ToLower() == "asc" ? joinedQuery.OrderBy(x => x.UserProfile != null ? x.UserProfile.Birthday : null) : joinedQuery.OrderByDescending(x => x.UserProfile != null ? x.UserProfile.Birthday : null),
                    "anniversary" => SortOrder?.ToLower() == "asc" ? joinedQuery.OrderBy(x => x.UserProfile != null ? x.UserProfile.Anniversary : null) : joinedQuery.OrderByDescending(x => x.UserProfile != null ? x.UserProfile.Anniversary : null),
                    "isbillingcontact" => SortOrder?.ToLower() == "asc" ? joinedQuery.OrderBy(x => x.UserProfile != null && x.UserProfile.IsBillingContact) : joinedQuery.OrderByDescending(x => x.UserProfile != null && x.UserProfile.IsBillingContact),
                    // --- End sorting for new fields ---
                    _ => joinedQuery.OrderBy(x => x.UserProfile != null ? x.UserProfile.LastName : null).ThenBy(x => x.UserProfile != null ? x.UserProfile.FirstName : null), // Default sort by Full Name if SortColumn is invalid or not provided
                };
            }
            // --- End Apply Sorting ---
            // Apply Pagination
            var paginatedJoinedUsers = await orderedQuery
                .Skip((PageNumber - 1) * PageSize)
                .Take(PageSize)
                .ToListAsync();
            // --- Map Joined Data to UserModel and Fetch Roles ---
            Users = []; // Clear the list before adding the current page's users
            foreach (var item in paginatedJoinedUsers)
            {
                var user = item.User;
                var userProfile = item.UserProfile;
                var roles = await _userManager.GetRolesAsync(user);
                string? fullName = null;
                if (userProfile != null)
                {
                    fullName = $"{userProfile.FirstName} {(string.IsNullOrEmpty(userProfile.MiddleName) ? "" : userProfile.MiddleName + " ")}{userProfile.LastName}".Trim();
                }
                // Fix for CS1963: An expression tree may not contain a dynamic operation  
                // The issue arises because LINQ-to-Entities does not support dynamic types in expression trees.  
                // To resolve this, we need to materialize the query into memory using `.ToList()` or `.AsEnumerable()` before performing operations involving dynamic types.  
                // Updated code for calculating `totalCharges` and `totalPayments`  
                decimal totalCharges = _dbContext.Invoices
                    .AsEnumerable() // Materialize the query into memory  
                    .Where(i => i.UserID == user.Id && i.Status != InvoiceStatus.Cancelled)
                    .Sum(i => i.AmountDue);
                decimal totalPayments = _dbContext.Payments
                    .AsEnumerable() // Materialize the query into memory  
                    .Where(p => p.UserID == user.Id)
                    .Sum(p => p.Amount);
                decimal currentBalance = totalCharges - totalPayments;
                Users.Add(new UserModel
                {
                    Id = user.Id,
                    UserName = user.UserName ?? string.Empty,
                    Email = user.Email ?? string.Empty,
                    EmailConfirmed = user.EmailConfirmed,
                    PhoneNumber = user.PhoneNumber,
                    PhoneNumberConfirmed = user.PhoneNumberConfirmed,
                    FullName = fullName ?? "No Info",
                    Roles = roles,
                    HomePhoneNumber = userProfile?.HomePhoneNumber,
                    LastLogin = userProfile?.LastLogin,
                    // --- Map Additional UserProfile Fields ---
                    FirstName = userProfile?.FirstName,
                    MiddleName = userProfile?.MiddleName,
                    LastName = userProfile?.LastName,
                    AddressLine1 = userProfile?.AddressLine1,
                    AddressLine2 = userProfile?.AddressLine2,
                    City = userProfile?.City,
                    State = userProfile?.State,
                    ZipCode = userProfile?.ZipCode,
                    Plot = userProfile?.Plot,
                    Birthday = userProfile?.Birthday,
                    Anniversary = userProfile?.Anniversary,
                    IsBillingContact = userProfile?.IsBillingContact ?? false,
                    CurrentBalance = currentBalance
                    // --- End Map Additional UserProfile Fields ---
                });
            }
        }
        // Helper method to check if a sort column is one of the new extra fields
        private static bool IsExtraFieldSortColumn(string? sortColumn)
        {
            if (string.IsNullOrEmpty(sortColumn)) return false;
            var extraFields = new List<string>
            {
                "firstname", "middlename", "lastname", "homephonenumber", "addressline1", "addressline2",
                "city", "state", "zipcode", "plot", "birthday", "anniversary"
            };
            return extraFields.Contains(sortColumn.ToLower());
        }
    }
    // This helper class is needed for combining multiple LINQ Where clauses with OR
    // For complex OR conditions across relationships, PredicateBuilder is helpful.
    public static class PredicateBuilder
    {
        public static System.Linq.Expressions.Expression<Func<T, bool>> True<T>() { return f => true; }
        public static System.Linq.Expressions.Expression<Func<T, bool>> False<T>() { return f => false; }
        public static System.Linq.Expressions.Expression<Func<T, bool>> Or<T>(this System.Linq.Expressions.Expression<Func<T, bool>> expr1,
                                                                             System.Linq.Expressions.Expression<Func<T, bool>> expr2)
        {
            var invokedExpr = System.Linq.Expressions.Expression.Invoke(expr2, expr1.Parameters.Cast<System.Linq.Expressions.Expression>());
            return System.Linq.Expressions.Expression.Lambda<Func<T, bool>>(System.Linq.Expressions.Expression.OrElse(expr1.Body, invokedExpr), expr1.Parameters);
        }
        public static System.Linq.Expressions.Expression<Func<T, bool>> And<T>(this System.Linq.Expressions.Expression<Func<T, bool>> expr1, System.Linq.Expressions.Expression<Func<T, bool>> expr2)
        {
            var invokedExpr = System.Linq.Expressions.Expression.Invoke(expr2, expr1.Parameters.Cast<System.Linq.Expressions.Expression>());
            return System.Linq.Expressions.Expression.Lambda<Func<T, bool>>(System.Linq.Expressions.Expression.AndAlso(expr1.Body, invokedExpr), expr1.Parameters);
        }
    }
}
