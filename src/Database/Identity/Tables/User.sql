-- =-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=
-- TABLE: Identity.User
-- Purpose: Logins for every role. Staff sign in by email and password, customers by phone OTP with the phone as
--          user name. User names are unique per tenant (NULL TenantId = platform staff), so one phone
--          can be a customer in two cities. The Identity columns follow ASP.NET Core Identity exactly.
-- Author: Courier team
-- Date: 2026-09-27
-- =-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=
CREATE TABLE [Identity].[User] (
    [Id]                   BIGINT             IDENTITY (1, 1) NOT NULL,
    [TenantId]             BIGINT             NULL,
    [MerchantId]           BIGINT             NULL,
    [CustomerId]           BIGINT             NULL,
    [DisplayName]          NVARCHAR (200)     NOT NULL,
    [UserName]             NVARCHAR (256)     NULL,
    [NormalizedUserName]   NVARCHAR (256)     NULL,
    [Email]                NVARCHAR (256)     NULL,
    [NormalizedEmail]      NVARCHAR (256)     NULL,
    [EmailConfirmed]       BIT                NOT NULL,
    [PasswordHash]         NVARCHAR (MAX)     NULL,
    [SecurityStamp]        NVARCHAR (MAX)     NULL,
    [ConcurrencyStamp]     NVARCHAR (MAX)     NULL,
    [PhoneNumber]          NVARCHAR (MAX)     NULL,
    [PhoneNumberConfirmed] BIT                NOT NULL,
    [TwoFactorEnabled]     BIT                NOT NULL,
    [LockoutEnd]           DATETIMEOFFSET (7) NULL,
    [LockoutEnabled]       BIT                NOT NULL,
    [AccessFailedCount]    INT                NOT NULL,
    [Archived]             BIT                DEFAULT ((0)) NOT NULL,
    [Created]              DATETIME2 (0)      DEFAULT (getutcdate()) NOT NULL,
    PRIMARY KEY CLUSTERED ([Id] ASC),
    CONSTRAINT [FK_User_Tenant] FOREIGN KEY ([TenantId]) REFERENCES [Platform].[Tenant] ([Id]),
    CONSTRAINT [FK_User_Merchant] FOREIGN KEY ([MerchantId]) REFERENCES [Merchants].[Merchant] ([Id]),
    CONSTRAINT [FK_User_Customer] FOREIGN KEY ([CustomerId]) REFERENCES [Customers].[Customer] ([Id])
);


GO
-- No TenantId filter: SQL Server treats NULLs as equal in a unique index, so platform user names stay unique too
CREATE UNIQUE NONCLUSTERED INDEX [UX_User_Tenant_UserName]
    ON [Identity].[User]([TenantId] ASC, [NormalizedUserName] ASC) WHERE ([NormalizedUserName] IS NOT NULL);


GO
CREATE NONCLUSTERED INDEX [IX_User_NormalizedEmail]
    ON [Identity].[User]([NormalizedEmail] ASC);


GO
CREATE NONCLUSTERED INDEX [IX_User_MerchantId]
    ON [Identity].[User]([MerchantId] ASC) WHERE ([MerchantId] IS NOT NULL);


GO
CREATE NONCLUSTERED INDEX [IX_User_CustomerId]
    ON [Identity].[User]([CustomerId] ASC) WHERE ([CustomerId] IS NOT NULL);
