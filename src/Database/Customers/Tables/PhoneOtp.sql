-- =-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=
-- TABLE: Customers.PhoneOtp
-- Purpose: One SMS login code. Only a SHA-256 hash of the code is stored. A code expires after 5 minutes and
--          locks after 5 wrong guesses; at most 3 codes per phone per 15 minutes are issued.
-- Author: Courier team
-- Date: 2026-09-27
-- =-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=
CREATE TABLE [Customers].[PhoneOtp] (
    [Id]         BIGINT        IDENTITY (1, 1) NOT NULL,
    [TenantId]   BIGINT        NOT NULL,
    [Phone]      NVARCHAR (20) NOT NULL,
    [CodeHash]   BINARY (32)   NOT NULL,
    [ExpiresOn]  DATETIME2 (0) NOT NULL,
    [Attempts]   INT           DEFAULT ((0)) NOT NULL,
    [ConsumedOn] DATETIME2 (0) NULL,
    [UpdatedId]  BIGINT        NULL,
    [UpdatedOn]  DATETIME2 (7) DEFAULT (getutcdate()) NOT NULL,
    [Created]    DATETIME2 (0) DEFAULT (getutcdate()) NOT NULL,
    PRIMARY KEY CLUSTERED ([Id] ASC),
    CONSTRAINT [FK_PhoneOtp_Tenant] FOREIGN KEY ([TenantId]) REFERENCES [Platform].[Tenant] ([Id]),
    CONSTRAINT [FK_PhoneOtp_User] FOREIGN KEY ([UpdatedId]) REFERENCES [Identity].[User] ([Id])
);


GO
CREATE NONCLUSTERED INDEX [IX_PhoneOtp_Tenant_Phone_Created]
    ON [Customers].[PhoneOtp]([TenantId] ASC, [Phone] ASC, [Created] ASC);
