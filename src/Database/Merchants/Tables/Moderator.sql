-- =-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=
-- TABLE: Merchants.Moderator
-- Purpose: Someone who works inside a merchant account with a sign-in of their own, and what they may do
--          (Permissions is a bit field; see Domain.Merchants.MerchantPermissions). The row points at the
--          account's main profile, not at a business, because a moderator works in every business of the
--          account, so it carries AccountId instead of the usual MerchantId and is not merchant-filtered.
--          Stopping a moderator archives the row and locks the login out; the row is kept for the record.
-- Author: Courier team
-- Date: 2026-10-06
-- =-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=
CREATE TABLE [Merchants].[Moderator] (
    [Id]          BIGINT         IDENTITY (1, 1) NOT NULL,
    [TenantId]    BIGINT         NOT NULL,
    [AccountId]   BIGINT         NOT NULL,
    [UserId]      BIGINT         NOT NULL,
    [Name]        NVARCHAR (200) NOT NULL,
    [Phone]       NVARCHAR (20)  NULL,
    [Permissions] INT            DEFAULT ((0)) NOT NULL,
    [Archived]    BIT            DEFAULT ((0)) NOT NULL,
    [UpdatedId]   BIGINT         NULL,
    [UpdatedOn]   DATETIME2 (7)  DEFAULT (getutcdate()) NOT NULL,
    [Created]     DATETIME2 (0)  DEFAULT (getutcdate()) NOT NULL,
    PRIMARY KEY CLUSTERED ([Id] ASC),
    CONSTRAINT [FK_Moderator_Tenant] FOREIGN KEY ([TenantId]) REFERENCES [Platform].[Tenant] ([Id]),
    CONSTRAINT [FK_Moderator_Merchant] FOREIGN KEY ([AccountId]) REFERENCES [Merchants].[Merchant] ([Id]),
    CONSTRAINT [FK_Moderator_User_UserId] FOREIGN KEY ([UserId]) REFERENCES [Identity].[User] ([Id]),
    CONSTRAINT [FK_Moderator_User] FOREIGN KEY ([UpdatedId]) REFERENCES [Identity].[User] ([Id])
);


GO
CREATE UNIQUE NONCLUSTERED INDEX [UX_Moderator_Tenant_User]
    ON [Merchants].[Moderator]([TenantId] ASC, [UserId] ASC);


GO
CREATE NONCLUSTERED INDEX [IX_Moderator_Tenant_Account]
    ON [Merchants].[Moderator]([TenantId] ASC, [AccountId] ASC);
