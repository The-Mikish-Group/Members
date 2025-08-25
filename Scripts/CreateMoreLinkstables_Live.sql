-- More Links Module Database Script for LIVE/PRODUCTION Database
-- Run this script on your production database
-- WARNING: This script will create tables and add initial data if tables don't exist

-- Create LinkCategories table
IF NOT EXISTS (SELECT * FROM sys.objects WHERE object_id = OBJECT_ID(N'[dbo].[LinkCategories]') AND type in (N'U'))
BEGIN
    CREATE TABLE [dbo].[LinkCategories] (
        [CategoryID] int IDENTITY(1,1) NOT NULL,
        [CategoryName] nvarchar(255) NOT NULL,
        [SortOrder] int NOT NULL,
        [IsAdminOnly] bit NOT NULL DEFAULT 0,
        CONSTRAINT [PK_LinkCategories] PRIMARY KEY CLUSTERED ([CategoryID] ASC)
    )
    PRINT 'LinkCategories table created successfully'
END
ELSE
BEGIN
    PRINT 'LinkCategories table already exists'
END

-- Create CategoryLinks table  
IF NOT EXISTS (SELECT * FROM sys.objects WHERE object_id = OBJECT_ID(N'[dbo].[CategoryLinks]') AND type in (N'U'))
BEGIN
    CREATE TABLE [dbo].[CategoryLinks] (
        [LinkID] int IDENTITY(1,1) NOT NULL,
        [CategoryID] int NOT NULL,
        [LinkName] nvarchar(255) NOT NULL,
        [LinkUrl] nvarchar(500) NOT NULL,
        [SortOrder] int NOT NULL,
        CONSTRAINT [PK_CategoryLinks] PRIMARY KEY CLUSTERED ([LinkID] ASC),
        CONSTRAINT [FK_CategoryLinks_LinkCategories] FOREIGN KEY ([CategoryID]) 
            REFERENCES [dbo].[LinkCategories] ([CategoryID]) ON DELETE CASCADE
    )
    PRINT 'CategoryLinks table created successfully'
END
ELSE
BEGIN
    PRINT 'CategoryLinks table already exists'
END

-- For production, only add minimal starter data if completely empty
IF NOT EXISTS (SELECT * FROM [dbo].[LinkCategories])
BEGIN
    -- Add just one starter category for production
    INSERT INTO [dbo].[LinkCategories] ([CategoryName], [SortOrder], [IsAdminOnly]) VALUES 
    ('Useful Links', 1, 0);

    PRINT 'Starter category added to production database'
    
    -- No test links for production - let admins add their own
    PRINT 'Production database ready - no test links added'
END
ELSE
BEGIN
    PRINT 'LinkCategories already contains data - no changes made'
END

PRINT 'More Links tables setup complete for PRODUCTION database'