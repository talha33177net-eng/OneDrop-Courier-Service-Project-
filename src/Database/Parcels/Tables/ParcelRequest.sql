-- =-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=
-- TABLE: Parcels.ParcelRequest
-- Purpose: A merchant's request about a parcel the courier already has, answered by the courier. Kind (TINYINT enum
--          Domain.Parcels.ParcelRequestKind): 1 Cancel (the parcel comes back as a return), 2 ChangeCod (NewCodAmount
--          is the cash asked for; CodAmount is what it was when asked). Status (Domain.Parcels.ParcelRequestStatus):
--          1 Open, 2 Approved (the parcel was changed), 3 Refused (Answer says why). RequestedById and AnsweredById are
--          the logins that asked and answered. One open request per parcel.
-- Author: Courier team
-- Date: 2026-10-07
-- =-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=
CREATE TABLE [Parcels].[ParcelRequest] (
    [Id]            BIGINT          IDENTITY (1, 1) NOT NULL,
    [TenantId]      BIGINT          NOT NULL,
    [MerchantId]    BIGINT          NOT NULL,
    [ParcelId]      BIGINT          NOT NULL,
    [Kind]          TINYINT         NOT NULL,
    [CodAmount]     DECIMAL (12, 2) NOT NULL,
    [NewCodAmount]  DECIMAL (12, 2) NULL,
    [Reason]        NVARCHAR (200)  NOT NULL,
    [RequestedById] BIGINT          NULL,
    [Status]        TINYINT         NOT NULL,
    [Answer]        NVARCHAR (200)  NULL,
    [AnsweredOn]    DATETIME2 (7)   NULL,
    [AnsweredById]  BIGINT          NULL,
    [RowVersion]    ROWVERSION      NOT NULL,
    [UpdatedId]     BIGINT          NULL,
    [UpdatedOn]     DATETIME2 (7)   DEFAULT (getutcdate()) NOT NULL,
    [Created]       DATETIME2 (0)   DEFAULT (getutcdate()) NOT NULL,
    PRIMARY KEY CLUSTERED ([Id] ASC),
    CONSTRAINT [FK_ParcelRequest_Tenant] FOREIGN KEY ([TenantId]) REFERENCES [Platform].[Tenant] ([Id]),
    CONSTRAINT [FK_ParcelRequest_Merchant] FOREIGN KEY ([MerchantId]) REFERENCES [Merchants].[Merchant] ([Id]),
    CONSTRAINT [FK_ParcelRequest_Parcel] FOREIGN KEY ([ParcelId]) REFERENCES [Parcels].[Parcel] ([Id]),
    CONSTRAINT [FK_ParcelRequest_User_RequestedById] FOREIGN KEY ([RequestedById]) REFERENCES [Identity].[User] ([Id]),
    CONSTRAINT [FK_ParcelRequest_User_AnsweredById] FOREIGN KEY ([AnsweredById]) REFERENCES [Identity].[User] ([Id]),
    CONSTRAINT [FK_ParcelRequest_User_UpdatedId] FOREIGN KEY ([UpdatedId]) REFERENCES [Identity].[User] ([Id]),
    CONSTRAINT [chk_ParcelRequest_Kind] CHECK ([Kind] BETWEEN 1 AND 2),
    CONSTRAINT [chk_ParcelRequest_Status] CHECK ([Status] BETWEEN 1 AND 3),
    CONSTRAINT [chk_ParcelRequest_Cod] CHECK (([Kind] = 1 AND [NewCodAmount] IS NULL) OR ([Kind] = 2 AND [NewCodAmount] >= (0))),
    CONSTRAINT [chk_ParcelRequest_Answered] CHECK (([Status] = 1 AND [AnsweredOn] IS NULL) OR ([Status] IN (2, 3) AND [AnsweredOn] IS NOT NULL))
);


GO
-- A parcel has one open request at a time
CREATE UNIQUE NONCLUSTERED INDEX [UX_ParcelRequest_Parcel_Open]
    ON [Parcels].[ParcelRequest]([ParcelId] ASC) WHERE ([Status] = 1);


GO
CREATE NONCLUSTERED INDEX [IX_ParcelRequest_Tenant_Status]
    ON [Parcels].[ParcelRequest]([TenantId] ASC, [Status] ASC)
    INCLUDE([MerchantId]);


GO
CREATE NONCLUSTERED INDEX [IX_ParcelRequest_Tenant_Merchant]
    ON [Parcels].[ParcelRequest]([TenantId] ASC, [MerchantId] ASC);
