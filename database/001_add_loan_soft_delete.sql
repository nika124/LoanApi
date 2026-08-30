USE [LoanApiDb];
GO

IF COL_LENGTH(N'dbo.Loans', N'IsDeleted') IS NULL
BEGIN
    ALTER TABLE dbo.Loans
        ADD IsDeleted bit NOT NULL
            CONSTRAINT DF_Loans_IsDeleted DEFAULT (0) WITH VALUES;
END;
GO

IF COL_LENGTH(N'dbo.Loans', N'DeletedAt') IS NULL
BEGIN
    ALTER TABLE dbo.Loans ADD DeletedAt datetime2(7) NULL;
END;
GO

IF NOT EXISTS
(
    SELECT 1
    FROM sys.indexes
    WHERE object_id = OBJECT_ID(N'dbo.Loans')
      AND name = N'IX_Loans_UserId_IsDeleted'
)
BEGIN
    CREATE INDEX IX_Loans_UserId_IsDeleted ON dbo.Loans(UserId, IsDeleted);
END;
GO

IF EXISTS
(
    SELECT 1
    FROM sys.check_constraints
    WHERE parent_object_id = OBJECT_ID(N'dbo.LoanHistory')
      AND name = N'CK_LoanHistory_Action'
      AND definition NOT LIKE N'%Deleted%'
)
BEGIN
    ALTER TABLE dbo.LoanHistory DROP CONSTRAINT CK_LoanHistory_Action;
    ALTER TABLE dbo.LoanHistory WITH CHECK ADD CONSTRAINT CK_LoanHistory_Action
        CHECK (Action IN ('Created', 'Updated', 'StatusChanged', 'Deleted'));
    ALTER TABLE dbo.LoanHistory CHECK CONSTRAINT CK_LoanHistory_Action;
END;
GO
