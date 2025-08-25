# Claude Code Configuration

This file contains configuration and instructions for Claude Code.

## Commands

- lint: npm run lint
- typecheck: npm run typecheck
- test: npm test
- build: npm run build
- dev: npm run dev

## Project Structure

This is the Oaks-Village ASP.NET Core MVC project with the following structure:

### Framework Details
- ASP.NET Core MVC with Razor Pages
- Entity Framework Core for data access
- ASP.NET Core Identity for authentication
- Bootstrap 5 for UI styling
- Uses primary constructor pattern: `public class(dependencies) : base`

### Key Directories
- `Members/` - Main application folder
- `Controllers/` - MVC controllers
- `Models/` - Entity models and view models
- `Views/` - Razor views organized by controller
- `Data/` - DbContext and migrations
- `Areas/` - Identity, Admin, Information, Member areas
- `wwwroot/` - Static files (CSS, JS, images)

### Database Context
- Uses `ApplicationDbContext` inheriting from `IdentityDbContext`
- Connection managed through dependency injection
- Models include: UserProfile, PDFCategory, Invoice, Payment, BillableAsset, etc.

### Authentication & Authorization
- Role-based authorization with "Admin" and "Manager" roles
- Identity pages in `Areas/Identity/Pages/Account/`
- Admin functionality in `Areas/Admin/Pages/`

### Existing Navigation Structure
- Main navigation in `Views/Shared/_PartialHeader.cshtml`
- Partial views for banner, header, footer
- More Links page now at `/Links/MoreLinks` (dynamic database-driven)
- Old static `/Info/MoreLinks` has been replaced and archived

### CSS & Styling
- Custom button classes: btn-billing, btn-rename, btn-delete
- Dynamic color system with CSS variables
- Fish-Smart card styling with custom colors
- Bootstrap Icons (bi-*) used throughout
- More Links system CSS moved to site.css (includes responsive @media queries)
- Note: @media queries cannot be used in Razor view <style> blocks

## More Links System Implementation Notes

### Additional Steps Required Beyond Standard Deployment Package

#### 1. Razor View CSS Constraints
**Issue:** @media queries cannot be used in Razor view `<style>` blocks
**Solution:** All CSS with @media queries must be moved to site.css
- Extracted all CSS from `Views/Links/MoreLinks.cshtml` 
- Added dedicated `/* #region More Links System CSS */` section to site.css
- Includes responsive styling for mobile devices

#### 2. Database Auto-Creation Causes Resource Locks
**Issue:** EnsureDatabaseTablesExist() method caused application crashes
**Problem:** SQL execution within controller actions can lock database resources
**Solution:** 
- Removed automatic table creation from LinksController entirely
- Added proper try-catch error handling instead
- Database tables must be created manually using provided scripts
- Added helpful error messages when tables don't exist

#### 3. Navigation Integration
**Issue:** Replacing existing static Info/MoreLinks required multiple reference updates
**Steps Taken:**
- Updated header navigation: `_PartialHeader.cshtml` 
- Updated footer navigation: `_PartialFooter.cshtml`
- Commented out old `InfoController.MoreLinks()` action
- Archived old `Views/Info/MoreLinks.cshtml` as `.OBSOLETE`
- Verified all references point to new `Links/MoreLinks`

#### 4. Mobile Responsiveness Enhancements
**Issue:** Admin buttons needed better mobile layout
**Solution:** Added custom CSS class `admin-buttons-container`
- Buttons stack vertically on mobile (≤576px)
- Maintains center alignment at all screen sizes
- Proper spacing between buttons using flexbox gap

#### 5. Environment-Specific Considerations
**Your Project Used:**
- ASP.NET Core MVC with primary constructor pattern
- Entity Framework Core with ApplicationDbContext
- Bootstrap 5.x with Bootstrap Icons (bi-*)
- Role-based authorization (Admin/Manager)
- Dynamic color system with CSS variables
- Fish-Smart card styling components

### Key Deployment Tips for Similar Projects

1. **Always run database scripts manually** - Never rely on auto-creation
2. **Move all @media queries to CSS files** - Razor views cannot handle them
3. **Update ALL navigation references** - Check header, footer, and any hardcoded links
4. **Test error handling** - Ensure graceful degradation when tables don't exist
5. **Verify CSS inheritance** - Ensure custom card styling matches existing theme
6. **Check button CSS classes** - Confirm btn-billing, btn-rename, btn-delete exist
7. **Test mobile responsiveness** - Verify button layout works on small screens

### Files Modified Beyond Standard Package
- `site.css` - Added More Links system CSS section
- `_PartialHeader.cshtml` - Updated navigation menu
- `_PartialFooter.cshtml` - Updated footer links
- `InfoController.cs` - Commented out old MoreLinks action
- `CLAUDE.md` - Added implementation documentation