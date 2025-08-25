-- More Links Module Database Script for DEV Database
-- Run this script on your development database

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

-- Add some initial test data only if tables are empty
IF NOT EXISTS (SELECT * FROM [dbo].[LinkCategories])
BEGIN
    INSERT INTO [dbo].[LinkCategories] ([CategoryName], [SortOrder], [IsAdminOnly]) VALUES 
    ('Helpful Resources', 1, 0),
    ('Government Links', 2, 0),
    ('Community Services', 3, 0);

    PRINT 'Initial categories added'

    INSERT INTO [dbo].[CategoryLinks] ([CategoryID], [LinkName], [LinkUrl], [SortOrder]) VALUES 
    (1, 'Google', 'https://www.google.com', 1),
    (1, 'Weather.com', 'https://www.weather.com', 2),
    (2, 'IRS', 'https://www.irs.gov', 1),
    (2, 'Social Security', 'https://www.ssa.gov', 2),
    (3, 'Local Library', 'https://example.com/library', 1);

    PRINT 'Initial test links added'
END
ELSE
BEGIN
    PRINT 'LinkCategories already contains data - skipping test data insertion'
END

PRINT 'More Links tables setup complete for DEV database'