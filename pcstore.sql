/*
  PC Store - Data-First Schema (SQL Server)
  Goal: e-commerce for PC components + "Build PC" compatibility checking.

  Notes:
  - Keep this file as the source of truth for the database design (data-first).
  - App code is written to match these tables/columns.
*/

-- Create database (optional)
-- CREATE DATABASE PcStore;
-- GO
-- USE PcStore;
-- GO

/* =========================
   Reference / Lookup Tables
   ========================= */
CREATE TABLE dbo.Brand (
    BrandId         INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_Brand PRIMARY KEY,
    Name            NVARCHAR(100) NOT NULL CONSTRAINT UQ_Brand_Name UNIQUE
);

CREATE TABLE dbo.ComponentCategory (
    ComponentCategoryId INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_ComponentCategory PRIMARY KEY,
    Code                NVARCHAR(40) NOT NULL CONSTRAINT UQ_ComponentCategory_Code UNIQUE, -- CPU, MAINBOARD, RAM, SSD, HDD, GPU, PSU, CASE, MONITOR, KEYBOARD, MOUSE, HEADSET, COOLER_AIR, COOLER_AIO, FAN, CHAIR, ACCESSORY
    DisplayName         NVARCHAR(100) NOT NULL
);

CREATE TABLE dbo.Socket (
    SocketId        INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_Socket PRIMARY KEY,
    Code            NVARCHAR(40) NOT NULL CONSTRAINT UQ_Socket_Code UNIQUE, -- LGA1700, AM5...
    DisplayName     NVARCHAR(100) NOT NULL
);

CREATE TABLE dbo.RamStandard (
    RamStandardId   INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_RamStandard PRIMARY KEY,
    Code            NVARCHAR(20) NOT NULL CONSTRAINT UQ_RamStandard_Code UNIQUE, -- DDR4, DDR5
    DisplayName     NVARCHAR(50) NOT NULL
);

CREATE TABLE dbo.FormFactor (
    FormFactorId    INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_FormFactor PRIMARY KEY,
    Code            NVARCHAR(30) NOT NULL CONSTRAINT UQ_FormFactor_Code UNIQUE, -- ATX, mATX, ITX
    DisplayName     NVARCHAR(50) NOT NULL
);

/* =========================
   Product / Inventory Tables
   ========================= */
CREATE TABLE dbo.Component (
    ComponentId         INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_Component PRIMARY KEY,
    ComponentCategoryId INT NOT NULL CONSTRAINT FK_Component_Category REFERENCES dbo.ComponentCategory(ComponentCategoryId),
    BrandId             INT NULL CONSTRAINT FK_Component_Brand REFERENCES dbo.Brand(BrandId),
    Sku                 NVARCHAR(60) NOT NULL CONSTRAINT UQ_Component_Sku UNIQUE,
    Name                NVARCHAR(200) NOT NULL,
    PriceVnd            DECIMAL(18,2) NOT NULL CONSTRAINT CK_Component_Price CHECK (PriceVnd >= 0),
    StockQty            INT NOT NULL CONSTRAINT CK_Component_Stock CHECK (StockQty >= 0),
    IsActive            BIT NOT NULL CONSTRAINT DF_Component_IsActive DEFAULT (1),
    CreatedAtUtc        DATETIME2(3) NOT NULL CONSTRAINT DF_Component_CreatedAtUtc DEFAULT (SYSUTCDATETIME())
);

CREATE TABLE dbo.CpuSpec (
    ComponentId     INT NOT NULL CONSTRAINT PK_CpuSpec PRIMARY KEY
        CONSTRAINT FK_CpuSpec_Component REFERENCES dbo.Component(ComponentId),
    SocketId        INT NOT NULL CONSTRAINT FK_CpuSpec_Socket REFERENCES dbo.Socket(SocketId),
    Generation      NVARCHAR(30) NULL,
    TdpWatt         INT NOT NULL CONSTRAINT CK_CpuSpec_Tdp CHECK (TdpWatt > 0)
);

CREATE TABLE dbo.MainboardSpec (
    ComponentId         INT NOT NULL CONSTRAINT PK_MainboardSpec PRIMARY KEY
        CONSTRAINT FK_MainboardSpec_Component REFERENCES dbo.Component(ComponentId),
    SocketId            INT NOT NULL CONSTRAINT FK_MainboardSpec_Socket REFERENCES dbo.Socket(SocketId),
    Chipset             NVARCHAR(40) NOT NULL,
    RamStandardId       INT NOT NULL CONSTRAINT FK_MainboardSpec_RamStandard REFERENCES dbo.RamStandard(RamStandardId),
    FormFactorId        INT NOT NULL CONSTRAINT FK_MainboardSpec_FormFactor REFERENCES dbo.FormFactor(FormFactorId),
    PcieSlotVersion     NVARCHAR(10) NULL
);

CREATE TABLE dbo.RamSpec (
    ComponentId     INT NOT NULL CONSTRAINT PK_RamSpec PRIMARY KEY
        CONSTRAINT FK_RamSpec_Component REFERENCES dbo.Component(ComponentId),
    RamStandardId   INT NOT NULL CONSTRAINT FK_RamSpec_RamStandard REFERENCES dbo.RamStandard(RamStandardId),
    CapacityGb      INT NOT NULL CONSTRAINT CK_RamSpec_Capacity CHECK (CapacityGb > 0),
    SpeedMhz        INT NULL
);

CREATE TABLE dbo.GpuSpec (
    ComponentId     INT NOT NULL CONSTRAINT PK_GpuSpec PRIMARY KEY
        CONSTRAINT FK_GpuSpec_Component REFERENCES dbo.Component(ComponentId),
    TdpWatt         INT NOT NULL CONSTRAINT CK_GpuSpec_Tdp CHECK (TdpWatt > 0)
);

CREATE TABLE dbo.PsuSpec (
    ComponentId     INT NOT NULL CONSTRAINT PK_PsuSpec PRIMARY KEY
        CONSTRAINT FK_PsuSpec_Component REFERENCES dbo.Component(ComponentId),
    CapacityWatt    INT NOT NULL CONSTRAINT CK_PsuSpec_Capacity CHECK (CapacityWatt > 0),
    Efficiency      NVARCHAR(20) NULL
);

/* =========================
   Build PC (Configuration)
   ========================= */
CREATE TABLE dbo.BuildConfiguration (
    BuildConfigurationId INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_BuildConfiguration PRIMARY KEY,
    Name                NVARCHAR(120) NOT NULL,
    CreatedAtUtc        DATETIME2(3) NOT NULL CONSTRAINT DF_BuildConfiguration_CreatedAtUtc DEFAULT (SYSUTCDATETIME())
);

CREATE TABLE dbo.BuildConfigurationItem (
    BuildConfigurationItemId INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_BuildConfigurationItem PRIMARY KEY,
    BuildConfigurationId     INT NOT NULL CONSTRAINT FK_BuildConfigurationItem_Config REFERENCES dbo.BuildConfiguration(BuildConfigurationId),
    ComponentCategoryId      INT NOT NULL CONSTRAINT FK_BuildConfigurationItem_Category REFERENCES dbo.ComponentCategory(ComponentCategoryId),
    ComponentId              INT NOT NULL CONSTRAINT FK_BuildConfigurationItem_Component REFERENCES dbo.Component(ComponentId),
    Qty                      INT NOT NULL CONSTRAINT CK_BuildConfigurationItem_Qty CHECK (Qty > 0),
    CONSTRAINT UQ_BuildConfigurationItem_UniqueCategory UNIQUE (BuildConfigurationId, ComponentCategoryId)
);

/* =========================
   Customer / User Accounts / Cart / Orders
   ========================= */
CREATE TABLE dbo.Customer (
    CustomerId      INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_Customer PRIMARY KEY,
    -- Auth / account info
    Username        NVARCHAR(80) NOT NULL CONSTRAINT UQ_Customer_Username UNIQUE,
    Email           NVARCHAR(200) NOT NULL CONSTRAINT UQ_Customer_Email UNIQUE,
    PasswordHash    VARBINARY(256) NOT NULL,
    PasswordSalt    VARBINARY(128) NOT NULL,
    PasswordResetToken NVARCHAR(120) NULL,
    PasswordResetExpiresAtUtc DATETIME2(3) NULL,
    -- Profile
    FullName        NVARCHAR(200) NULL,
    AvatarUrl       NVARCHAR(400) NULL,
    IsActive        BIT NOT NULL CONSTRAINT DF_Customer_IsActive DEFAULT (1),
    CreatedAtUtc    DATETIME2(3) NOT NULL CONSTRAINT DF_Customer_CreatedAtUtc DEFAULT (SYSUTCDATETIME())
);

CREATE TABLE dbo.Cart (
    CartId          INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_Cart PRIMARY KEY,
    CustomerId      INT NULL CONSTRAINT FK_Cart_Customer REFERENCES dbo.Customer(CustomerId),
    SessionKey      NVARCHAR(80) NULL, -- for anonymous carts
    CreatedAtUtc    DATETIME2(3) NOT NULL CONSTRAINT DF_Cart_CreatedAtUtc DEFAULT (SYSUTCDATETIME())
);

CREATE TABLE dbo.CartItem (
    CartItemId      INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_CartItem PRIMARY KEY,
    CartId          INT NOT NULL CONSTRAINT FK_CartItem_Cart REFERENCES dbo.Cart(CartId),
    ComponentId     INT NOT NULL CONSTRAINT FK_CartItem_Component REFERENCES dbo.Component(ComponentId),
    Qty             INT NOT NULL CONSTRAINT CK_CartItem_Qty CHECK (Qty > 0),
    CONSTRAINT UQ_CartItem UNIQUE (CartId, ComponentId)
);

CREATE TABLE dbo.[Order] (
    OrderId         INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_Order PRIMARY KEY,
    CustomerId      INT NULL CONSTRAINT FK_Order_Customer REFERENCES dbo.Customer(CustomerId),
    CartId          INT NULL CONSTRAINT FK_Order_Cart REFERENCES dbo.Cart(CartId),
    ReceiverName    NVARCHAR(200) NOT NULL,
    ReceiverPhone   NVARCHAR(30) NOT NULL,
    ReceiverEmail   NVARCHAR(200) NULL,
    ShippingAddress NVARCHAR(500) NOT NULL,
    Note            NVARCHAR(500) NULL,
    PaymentMethodCode NVARCHAR(20) NOT NULL CONSTRAINT DF_Order_PaymentMethod DEFAULT (N'COD'),
    TotalPriceVnd   DECIMAL(18,2) NOT NULL CONSTRAINT CK_Order_Total CHECK (TotalPriceVnd >= 0),
    StatusCode      NVARCHAR(30) NOT NULL, -- WAITING_PARTS, ASSEMBLING, TESTING, PACKING, SHIPPED, CANCELLED
    CreatedAtUtc    DATETIME2(3) NOT NULL CONSTRAINT DF_Order_CreatedAtUtc DEFAULT (SYSUTCDATETIME())
);

CREATE TABLE dbo.OrderItem (
    OrderItemId     INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_OrderItem PRIMARY KEY,
    OrderId         INT NOT NULL CONSTRAINT FK_OrderItem_Order REFERENCES dbo.[Order](OrderId),
    ComponentId     INT NOT NULL CONSTRAINT FK_OrderItem_Component REFERENCES dbo.Component(ComponentId),
    UnitPriceVnd    DECIMAL(18,2) NOT NULL CONSTRAINT CK_OrderItem_UnitPrice CHECK (UnitPriceVnd >= 0),
    Qty             INT NOT NULL CONSTRAINT CK_OrderItem_Qty CHECK (Qty > 0)
);

/* =========================
   Observer Pattern (Waitlist)
   ========================= */
CREATE TABLE dbo.StockWaitlist (
    StockWaitlistId INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_StockWaitlist PRIMARY KEY,
    ComponentId     INT NOT NULL CONSTRAINT FK_StockWaitlist_Component REFERENCES dbo.Component(ComponentId),
    CustomerId      INT NOT NULL CONSTRAINT FK_StockWaitlist_Customer REFERENCES dbo.Customer(CustomerId),
    CreatedAtUtc    DATETIME2(3) NOT NULL CONSTRAINT DF_StockWaitlist_CreatedAtUtc DEFAULT (SYSUTCDATETIME()),
    CONSTRAINT UQ_StockWaitlist UNIQUE (ComponentId, CustomerId)
);

CREATE INDEX IX_Component_Category ON dbo.Component(ComponentCategoryId);
CREATE INDEX IX_Component_Active ON dbo.Component(IsActive, StockQty);
CREATE INDEX IX_CpuSpec_Socket ON dbo.CpuSpec(SocketId);
CREATE INDEX IX_MainboardSpec_SocketRam ON dbo.MainboardSpec(SocketId, RamStandardId);
CREATE INDEX IX_Cart_SessionKey ON dbo.Cart(SessionKey);
