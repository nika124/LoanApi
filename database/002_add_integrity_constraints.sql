USE [LoanApiDb];
GO

IF NOT EXISTS
(
    SELECT 1 FROM sys.check_constraints
    WHERE parent_object_id = OBJECT_ID(N'dbo.Users') AND name = N'CK_Users_Age'
)
BEGIN
    ALTER TABLE dbo.Users WITH CHECK ADD CONSTRAINT CK_Users_Age
        CHECK (Age BETWEEN 18 AND 100);
    ALTER TABLE dbo.Users CHECK CONSTRAINT CK_Users_Age;
END;
GO

IF NOT EXISTS
(
    SELECT 1 FROM sys.check_constraints
    WHERE parent_object_id = OBJECT_ID(N'dbo.Users') AND name = N'CK_Users_BlockState'
)
BEGIN
    ALTER TABLE dbo.Users WITH CHECK ADD CONSTRAINT CK_Users_BlockState
        CHECK (IsBlocked = 1 OR BlockedUntil IS NULL);
    ALTER TABLE dbo.Users CHECK CONSTRAINT CK_Users_BlockState;
END;
GO

IF EXISTS
(
    SELECT 1 FROM sys.check_constraints
    WHERE parent_object_id = OBJECT_ID(N'dbo.Loans') AND name = N'CK_Loans_PeriodMonths'
)
BEGIN
    ALTER TABLE dbo.Loans DROP CONSTRAINT CK_Loans_PeriodMonths;
END;
GO

ALTER TABLE dbo.Loans WITH CHECK ADD CONSTRAINT CK_Loans_PeriodMonths
    CHECK (PeriodMonths BETWEEN 1 AND 600);
ALTER TABLE dbo.Loans CHECK CONSTRAINT CK_Loans_PeriodMonths;
GO

IF NOT EXISTS
(
    SELECT 1 FROM sys.check_constraints
    WHERE parent_object_id = OBJECT_ID(N'dbo.Loans') AND name = N'CK_Loans_Currency'
)
BEGIN
    ALTER TABLE dbo.Loans WITH CHECK ADD CONSTRAINT CK_Loans_Currency
        CHECK
        (
            LEN(Currency) = 3
            AND Currency COLLATE Latin1_General_100_BIN2 NOT LIKE '%[^A-Z]%'
        );
    ALTER TABLE dbo.Loans CHECK CONSTRAINT CK_Loans_Currency;
END;
GO

IF NOT EXISTS
(
    SELECT 1 FROM sys.check_constraints
    WHERE parent_object_id = OBJECT_ID(N'dbo.Loans') AND name = N'CK_Loans_DeletedState'
)
BEGIN
    ALTER TABLE dbo.Loans WITH CHECK ADD CONSTRAINT CK_Loans_DeletedState
        CHECK
        (
            (IsDeleted = 0 AND DeletedAt IS NULL)
            OR (IsDeleted = 1 AND DeletedAt IS NOT NULL)
        );
    ALTER TABLE dbo.Loans CHECK CONSTRAINT CK_Loans_DeletedState;
END;
GO
