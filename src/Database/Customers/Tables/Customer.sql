-- =-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=
-- TABLE: Customers.Customer
-- Purpose: A person who receives parcels, recognised across every merchant of the tenant by phone number. Phone
--          is E.164 (+8801XXXXXXXXX) and unique per tenant; the same phone in another tenant is another customer.
--          Shared by all merchants of the tenant - that is what makes grouping possible.
-- Author: Courier team
-- Date: 2026-09-27
-- =-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=
CREATE TABLE [Customers].[Customer] (
    [Id]            BIGINT         IDENTITY (1, 1) NOT NULL,
    [TenantId]      BIGINT         NOT NULL,
    [Phone]         NVARCHAR (20)  NOT NULL,
    [Name]          NVARCHAR (200) NULL,
    -- Refusals and no-shows lower it; a low score means the delivery fee is paid before dispatch
    [TrustScore]    INT            DEFAULT ((100)) NOT NULL,
    [PhoneVerified] BIT            DEFAULT ((0)) NOT NULL,
    [Archived]      BIT            DEFAULT ((0)) NOT NULL,
    [UpdatedId]     BIGINT         NULL,
    [UpdatedOn]     DATETIME2 (7)  DEFAULT (getutcdate()) NOT NULL,
    [Created]       DATETIME2 (0)  DEFAULT (getutcdate()) NOT NULL,
    PRIMARY KEY CLUSTERED ([Id] ASC),
    CONSTRAINT [FK_Customer_Tenant] FOREIGN KEY ([TenantId]) REFERENCES [Platform].[Tenant] ([Id]),
    CONSTRAINT [FK_Customer_User] FOREIGN KEY ([UpdatedId]) REFERENCES [Identity].[User] ([Id])
);


GO
CREATE UNIQUE NONCLUSTERED INDEX [UX_Customer_Tenant_Phone]
    ON [Customers].[Customer]([TenantId] ASC, [Phone] ASC);
