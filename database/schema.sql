IF DB_ID(N'LoanApiDb') IS NULL
BEGIN
    EXEC(N'CREATE DATABASE [LoanApiDb]');
END;
GO

USE [LoanApiDb];
GO

IF OBJECT_ID(N'dbo.Users', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.Users
    (
        Id int IDENTITY(1, 1) NOT NULL CONSTRAINT PK_Users PRIMARY KEY,
        FirstName nvarchar(100) NOT NULL,
        LastName nvarchar(100) NOT NULL,
        Username nvarchar(50) NOT NULL CONSTRAINT UQ_Users_Username UNIQUE,
        Email nvarchar(254) NOT NULL CONSTRAINT UQ_Users_Email UNIQUE,
        Age tinyint NOT NULL,
        MonthlyIncome decimal(18, 2) NOT NULL,
        IsBlocked bit NOT NULL CONSTRAINT DF_Users_IsBlocked DEFAULT (0),
        BlockedUntil datetime2(7) NULL,
        PasswordHash varchar(255) NOT NULL,
        CreatedAt datetime2(7) NOT NULL CONSTRAINT DF_Users_CreatedAt DEFAULT (sysutcdatetime()),
        CONSTRAINT CK_Users_Age CHECK (Age BETWEEN 18 AND 100),
        CONSTRAINT CK_Users_MonthlyIncome CHECK (MonthlyIncome >= 0),
        CONSTRAINT CK_Users_BlockState CHECK (IsBlocked = 1 OR BlockedUntil IS NULL)
    );
END;
GO

IF OBJECT_ID(N'dbo.Accountants', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.Accountants
    (
        Id int IDENTITY(1, 1) NOT NULL CONSTRAINT PK_Accountants PRIMARY KEY,
        FirstName nvarchar(100) NOT NULL,
        LastName nvarchar(100) NOT NULL,
        Username nvarchar(50) NOT NULL CONSTRAINT UQ_Accountants_Username UNIQUE,
        Email nvarchar(254) NOT NULL CONSTRAINT UQ_Accountants_Email UNIQUE,
        PasswordHash varchar(255) NOT NULL,
        IsActive bit NOT NULL CONSTRAINT DF_Accountants_IsActive DEFAULT (1),
        CreatedAt datetime2(7) NOT NULL CONSTRAINT DF_Accountants_CreatedAt DEFAULT (sysutcdatetime())
    );
END;
GO

IF OBJECT_ID(N'dbo.Loans', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.Loans
    (
        Id int IDENTITY(1, 1) NOT NULL CONSTRAINT PK_Loans PRIMARY KEY,
        UserId int NOT NULL,
        LoanType varchar(30) NOT NULL,
        Amount decimal(18, 2) NOT NULL,
        Currency char(3) NOT NULL,
        PeriodMonths smallint NOT NULL,
        Status varchar(20) NOT NULL CONSTRAINT DF_Loans_Status DEFAULT ('Pending'),
        CreatedAt datetime2(7) NOT NULL CONSTRAINT DF_Loans_CreatedAt DEFAULT (sysutcdatetime()),
        UpdatedAt datetime2(7) NULL,
        IsDeleted bit NOT NULL CONSTRAINT DF_Loans_IsDeleted DEFAULT (0),
        DeletedAt datetime2(7) NULL,
        CONSTRAINT FK_Loans_Users FOREIGN KEY (UserId) REFERENCES dbo.Users(Id),
        CONSTRAINT CK_Loans_LoanType CHECK (LoanType IN ('FastLoan', 'AutoLoan', 'Installment')),
        CONSTRAINT CK_Loans_Amount CHECK (Amount > 0),
        CONSTRAINT CK_Loans_Currency CHECK
        (
            LEN(Currency) = 3
            AND Currency COLLATE Latin1_General_100_BIN2 NOT LIKE '%[^A-Z]%'
        ),
        CONSTRAINT CK_Loans_PeriodMonths CHECK (PeriodMonths BETWEEN 1 AND 600),
        CONSTRAINT CK_Loans_Status CHECK (Status IN ('Pending', 'Approved', 'Rejected')),
        CONSTRAINT CK_Loans_DeletedState CHECK
        (
            (IsDeleted = 0 AND DeletedAt IS NULL)
            OR (IsDeleted = 1 AND DeletedAt IS NOT NULL)
        )
    );

    CREATE INDEX IX_Loans_UserId ON dbo.Loans(UserId);
    CREATE INDEX IX_Loans_UserId_IsDeleted ON dbo.Loans(UserId, IsDeleted);
END;
GO

IF OBJECT_ID(N'dbo.LoanHistory', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.LoanHistory
    (
        Id bigint IDENTITY(1, 1) NOT NULL CONSTRAINT PK_LoanHistory PRIMARY KEY,
        LoanId int NOT NULL,
        ChangedByUserId int NULL,
        ChangedByAccountantId int NULL,
        Action varchar(30) NOT NULL,
        FieldName nvarchar(100) NULL,
        OldValue nvarchar(1000) NULL,
        NewValue nvarchar(1000) NULL,
        ChangedAt datetime2(7) NOT NULL CONSTRAINT DF_LoanHistory_ChangedAt DEFAULT (sysutcdatetime()),
        CONSTRAINT FK_LoanHistory_Loans FOREIGN KEY (LoanId) REFERENCES dbo.Loans(Id),
        CONSTRAINT FK_LoanHistory_Users FOREIGN KEY (ChangedByUserId) REFERENCES dbo.Users(Id),
        CONSTRAINT FK_LoanHistory_Accountants FOREIGN KEY (ChangedByAccountantId) REFERENCES dbo.Accountants(Id),
        CONSTRAINT CK_LoanHistory_Action CHECK (Action IN ('Created', 'Updated', 'StatusChanged', 'Deleted')),
        CONSTRAINT CK_LoanHistory_ChangedBy CHECK
        (
            (ChangedByUserId IS NOT NULL AND ChangedByAccountantId IS NULL)
            OR (ChangedByUserId IS NULL AND ChangedByAccountantId IS NOT NULL)
        )
    );

    CREATE INDEX IX_LoanHistory_LoanId ON dbo.LoanHistory(LoanId);
    CREATE INDEX IX_LoanHistory_ChangedByUserId ON dbo.LoanHistory(ChangedByUserId);
    CREATE INDEX IX_LoanHistory_ChangedByAccountantId ON dbo.LoanHistory(ChangedByAccountantId);
END;
GO

IF OBJECT_ID(N'dbo.UserBlockHistory', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.UserBlockHistory
    (
        Id bigint IDENTITY(1, 1) NOT NULL CONSTRAINT PK_UserBlockHistory PRIMARY KEY,
        UserId int NOT NULL,
        AccountantId int NOT NULL,
        BlockedFrom datetime2(7) NOT NULL,
        BlockedUntil datetime2(7) NOT NULL,
        Reason nvarchar(500) NULL,
        CreatedAt datetime2(7) NOT NULL CONSTRAINT DF_UserBlockHistory_CreatedAt DEFAULT (sysutcdatetime()),
        CONSTRAINT FK_UserBlockHistory_Users FOREIGN KEY (UserId) REFERENCES dbo.Users(Id),
        CONSTRAINT FK_UserBlockHistory_Accountants FOREIGN KEY (AccountantId) REFERENCES dbo.Accountants(Id),
        CONSTRAINT CK_UserBlockHistory_Dates CHECK (BlockedUntil > BlockedFrom)
    );

    CREATE INDEX IX_UserBlockHistory_UserId ON dbo.UserBlockHistory(UserId);
    CREATE INDEX IX_UserBlockHistory_AccountantId ON dbo.UserBlockHistory(AccountantId);
END;
GO
