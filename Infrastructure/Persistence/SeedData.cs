using ban_link_kien_PC.Domain.Auth;
using ban_link_kien_PC.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;

namespace ban_link_kien_PC.Infrastructure.Persistence;

/// <summary>
/// Seed dữ liệu mẫu. Tên sản phẩm / giá lấy cảm hứng từ cửa hàng tham chiếu (ví dụ ttgshop.vn) — chỉ phục vụ demo.
/// Catalog Build PC hướng thị trường 2025–2026 (AM5 Zen 4/5, LGA1700, GPU 40/50-series).
/// Để nạp lại toàn bộ seed từ đầu: xóa database <c>pcstore</c> trên SQL Server rồi chạy lại app
/// (hoặc dùng InMemory khi không cấu hình SqlServer).
/// </summary>
public static class SeedData
{
    public static async Task EnsureSeededAsync(IServiceProvider services, CancellationToken ct = default)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PcStoreDbContext>();

        await db.Database.EnsureCreatedAsync(ct);
        await EnsureSchemaUpgradesAsync(db, ct);

        if (!await db.ComponentCategories.AnyAsync(ct))
        {
            SeedReferenceData(db);
            await db.SaveChangesAsync(ct);

            var now = DateTime.UtcNow;
            int Cat(string code) => db.ComponentCategories.Single(x => x.Code == code).ComponentCategoryId;
            int Brand(string name) => db.Brands.Single(x => x.Name == name).BrandId;
            int Sock(string code) => db.Sockets.Single(x => x.Code == code).SocketId;
            int RamStd(string code) => db.RamStandards.Single(x => x.Code == code).RamStandardId;
            int Form(string code) => db.FormFactors.Single(x => x.Code == code).FormFactorId;

            var components = BuildTtgStyleComponents(Cat, Brand, now);
            db.Components.AddRange(components);
            await db.SaveChangesAsync(ct);

            foreach (var spec in BuildCpuSpecs(components, Sock))
                db.CpuSpecs.Add(spec);
            foreach (var spec in BuildMainboardSpecs(components, Sock, RamStd, Form))
                db.MainboardSpecs.Add(spec);
            foreach (var spec in BuildRamSpecs(components, RamStd))
                db.RamSpecs.Add(spec);
            foreach (var spec in BuildGpuSpecs(components))
                db.GpuSpecs.Add(spec);
            foreach (var spec in BuildPsuSpecs(components))
                db.PsuSpecs.Add(spec);

            await db.SaveChangesAsync(ct);
        }
        else
        {
            await EnsureMarketCatalogUpgradeAsync(db, ct);
        }

        if (!await db.Customers.AnyAsync(ct))
        {
            var hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher>();
            var (hash, salt) = hasher.Hash("123456");
            db.Customers.Add(new CustomerEntity
            {
                Username = "demo",
                Email = "demo@example.com",
                PasswordHash = hash,
                PasswordSalt = salt,
                FullName = "Demo User",
                AvatarUrl = null,
                IsActive = true,
                CreatedAtUtc = DateTime.UtcNow
            });
            await db.SaveChangesAsync(ct);
        }

        var hasAdmin = await db.Customers.AnyAsync(x => x.Username == "admin", ct);
        if (!hasAdmin)
        {
            var hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher>();
            var (hash, salt) = hasher.Hash("123456");
            db.Customers.Add(new CustomerEntity
            {
                Username = "admin",
                Email = "admin@techarena.local",
                PasswordHash = hash,
                PasswordSalt = salt,
                FullName = "Administrator",
                AvatarUrl = null,
                IsActive = true,
                CreatedAtUtc = DateTime.UtcNow
            });
            await db.SaveChangesAsync(ct);
        }

        await EnsureStaffRbacSeedAsync(db, scope.ServiceProvider, ct);
        await EnsurePhase0DemoDataAsync(db, ct);
    }

    /// <summary>Demo: gắn Hot/BestSeller theo SKU cũ, promo mẫu, điểm loyalty cho user demo.</summary>
    private static async Task EnsurePhase0DemoDataAsync(PcStoreDbContext db, CancellationToken ct)
    {
        // Best-seller SKUs (khớp hardcode cũ trên HomeController — Phase 1 sẽ đọc từ DB)
        var bestSellerSkus = new[]
        {
            "TTG-PB-I5-14600KF-RTX4060TI",
            "TTG-PB-R7-7800X3D-RTX4070",
            "TTG-WS-I7-13700-RTX4070",
            "TTG-WS-I9-14900-RTX4080S",
            "TTG-WS-I5-12600K-RTX4060"
        };
        var hotSkus = new[]
        {
            "TTG-PB-I5-14600KF-RTX4060TI",
            "TTG-CPU-R5-9600X",
            "TTG-VGA-RTX4070-12"
        };

        var components = await db.Components
            .Where(x => bestSellerSkus.Contains(x.Sku) || hotSkus.Contains(x.Sku))
            .ToListAsync(ct);
        var changed = false;
        foreach (var c in components)
        {
            if (bestSellerSkus.Contains(c.Sku, StringComparer.OrdinalIgnoreCase) && !c.IsBestSeller)
            {
                c.IsBestSeller = true;
                changed = true;
            }
            if (hotSkus.Contains(c.Sku, StringComparer.OrdinalIgnoreCase) && !c.IsHot)
            {
                c.IsHot = true;
                changed = true;
            }
        }
        if (changed)
            await db.SaveChangesAsync(ct);

        if (!await db.PromoCodes.AnyAsync(ct))
        {
            db.PromoCodes.Add(new PromoCodeEntity
            {
                Code = "WELCOME10",
                DiscountType = "PERCENT",
                DiscountValue = 10m,
                MinOrderVnd = 1_000_000m,
                MaxUses = 100,
                UsedCount = 0,
                StartsAtUtc = DateTime.UtcNow.AddDays(-1),
                ExpiresAtUtc = DateTime.UtcNow.AddMonths(6),
                ApplicableTier = null,
                IsActive = true,
                Description = "Giảm 10% cho đơn từ 1 triệu (demo Phase 0)",
                CreatedAtUtc = DateTime.UtcNow
            });
            db.PromoCodes.Add(new PromoCodeEntity
            {
                Code = "GOLD50K",
                DiscountType = "FIXED",
                DiscountValue = 50_000m,
                MinOrderVnd = 500_000m,
                MaxUses = 50,
                UsedCount = 0,
                ApplicableTier = "GOLD",
                IsActive = true,
                Description = "Giảm 50.000đ cho khách hạng Vàng trở lên",
                CreatedAtUtc = DateTime.UtcNow
            });
            await db.SaveChangesAsync(ct);
        }

        var demo = await db.Customers.SingleOrDefaultAsync(x => x.Username == "demo", ct);
        if (demo is not null && demo.LoyaltyPoints == 0 && demo.MembershipTier == "NONE")
        {
            demo.LoyaltyPoints = 600;
            demo.MembershipTier = "GOLD";
            demo.ShippingAddress1 = "123 Nguyễn Văn Linh, Q.7, TP.HCM";
            demo.DateOfBirth = new DateTime(1998, 5, 15);
            await db.SaveChangesAsync(ct);
        }
    }

    private static async Task EnsureStaffRbacSeedAsync(PcStoreDbContext db, IServiceProvider sp, CancellationToken ct)
    {
        foreach (var roleName in StaffRoles.PortalRoles)
        {
            if (!await db.Roles.AnyAsync(x => x.RoleName == roleName, ct))
                db.Roles.Add(new RoleEntity { RoleName = roleName });
        }

        if (!await db.Roles.AnyAsync(x => x.RoleName == StaffRoles.LegacyAdmin, ct))
            db.Roles.Add(new RoleEntity { RoleName = StaffRoles.LegacyAdmin });
        if (!await db.Roles.AnyAsync(x => x.RoleName == StaffRoles.LegacyStaff, ct))
            db.Roles.Add(new RoleEntity { RoleName = StaffRoles.LegacyStaff });

        await db.SaveChangesAsync(ct);

        var hasher = sp.GetRequiredService<IPasswordHasher>();
        var roleIds = await db.Roles.AsNoTracking()
            .ToDictionaryAsync(x => x.RoleName, x => x.RoleId, StringComparer.OrdinalIgnoreCase, ct);

        async Task EnsureStaffAsync(string username, string email, string fullName, string phone, string password, string roleName)
        {
            if (!roleIds.TryGetValue(roleName, out var roleId)) return;
            var existing = await db.Staffs.Include(x => x.Role)
                .SingleOrDefaultAsync(x => x.Username == username, ct);
            if (existing is null)
            {
                var (hash, salt) = hasher.Hash(password);
                db.Staffs.Add(new StaffEntity
                {
                    RoleId = roleId,
                    Username = username,
                    Email = email,
                    PasswordHash = hash,
                    PasswordSalt = salt,
                    FullName = fullName,
                    Phone = phone,
                    IsActive = true,
                    CreatedAtUtc = DateTime.UtcNow
                });
                await db.SaveChangesAsync(ct);
                return;
            }

            // Đồng bộ lại mật khẩu + role cho tài khoản demo (tránh lệch hash cũ)
            var (newHash, newSalt) = hasher.Hash(password);
            existing.PasswordHash = newHash;
            existing.PasswordSalt = newSalt;
            existing.IsActive = true;
            existing.RoleId = roleId;
            existing.Email = email;
            existing.FullName = fullName;
            await db.SaveChangesAsync(ct);
        }

        await EnsureStaffAsync("admin", "admin@pcstore.local", "Super Admin", "0900000000", "Admin@123", StaffRoles.SuperAdmin);
        await EnsureStaffAsync("sales", "sales@pcstore.local", "Nhân viên Sales", "0900000002", "Sales@123", StaffRoles.Sales);
        await EnsureStaffAsync("kho", "kho@pcstore.local", "Nhân viên Kho", "0900000003", "Kho@123", StaffRoles.Warehouse);
        await EnsureStaffAsync("kythuat", "kythuat@pcstore.local", "Nhân viên kỹ thuật", "0900000001", "Tech@123", StaffRoles.Technical);
        await EnsureStaffAsync("ketoan", "ketoan@pcstore.local", "Nhân viên kế toán", "0900000004", "Acc@123", StaffRoles.Accounting);

        var legacyAdminId = roleIds.GetValueOrDefault(StaffRoles.LegacyAdmin);
        var legacyStaffId = roleIds.GetValueOrDefault(StaffRoles.LegacyStaff);
        var superId = roleIds.GetValueOrDefault(StaffRoles.SuperAdmin);
        var salesIdAll = roleIds.GetValueOrDefault(StaffRoles.Sales);
        if (legacyAdminId > 0 && superId > 0)
        {
            var toUpgrade = await db.Staffs.Where(x => x.RoleId == legacyAdminId).ToListAsync(ct);
            foreach (var s in toUpgrade) s.RoleId = superId;
            if (toUpgrade.Count > 0) await db.SaveChangesAsync(ct);
        }
        if (legacyStaffId > 0 && salesIdAll > 0)
        {
            var toUpgrade = await db.Staffs.Where(x => x.RoleId == legacyStaffId).ToListAsync(ct);
            foreach (var s in toUpgrade) s.RoleId = salesIdAll;
            if (toUpgrade.Count > 0) await db.SaveChangesAsync(ct);
        }
    }

    private static async Task EnsureSchemaUpgradesAsync(PcStoreDbContext db, CancellationToken ct)
    {
        if (!db.Database.IsRelational())
            return;

        try
        {
            await db.Database.ExecuteSqlRawAsync("""
                IF OBJECT_ID(N'dbo.RamSpec', N'U') IS NOT NULL
                   AND COL_LENGTH(N'dbo.RamSpec', N'ModuleCount') IS NULL
                BEGIN
                    ALTER TABLE dbo.RamSpec ADD ModuleCount int NOT NULL CONSTRAINT DF_RamSpec_ModuleCount DEFAULT (1);
                END
                """, ct);

            await db.Database.ExecuteSqlRawAsync("""
                IF OBJECT_ID(N'dbo.RamSpec', N'U') IS NOT NULL
                   AND COL_LENGTH(N'dbo.RamSpec', N'ModuleCount') IS NOT NULL
                BEGIN
                    UPDATE r SET ModuleCount = 2
                    FROM dbo.RamSpec r
                    INNER JOIN dbo.Component c ON c.ComponentId = r.ComponentId
                    WHERE c.Name LIKE N'%(2x%' AND ISNULL(r.ModuleCount, 1) < 2;
                END
                """, ct);

            await db.Database.ExecuteSqlRawAsync("""
                IF OBJECT_ID(N'dbo.Role', N'U') IS NULL
                BEGIN
                    CREATE TABLE dbo.Role (
                        RoleId INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_Role PRIMARY KEY,
                        RoleName NVARCHAR(50) NOT NULL CONSTRAINT UQ_Role_RoleName UNIQUE
                    );
                END

                IF OBJECT_ID(N'dbo.Staff', N'U') IS NULL
                BEGIN
                    CREATE TABLE dbo.Staff (
                        StaffId INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_Staff PRIMARY KEY,
                        RoleId INT NOT NULL CONSTRAINT FK_Staff_Role REFERENCES dbo.Role(RoleId),
                        Username NVARCHAR(80) NOT NULL CONSTRAINT UQ_Staff_Username UNIQUE,
                        Email NVARCHAR(200) NOT NULL CONSTRAINT UQ_Staff_Email UNIQUE,
                        PasswordHash VARBINARY(256) NOT NULL,
                        PasswordSalt VARBINARY(128) NOT NULL,
                        FullName NVARCHAR(200) NOT NULL,
                        Phone NVARCHAR(30) NULL,
                        IsActive BIT NOT NULL CONSTRAINT DF_Staff_IsActive DEFAULT (1),
                        CreatedAtUtc DATETIME2(3) NOT NULL CONSTRAINT DF_Staff_CreatedAtUtc DEFAULT (SYSUTCDATETIME())
                    );
                END

                -- Bảng Staff cũ thiếu PasswordSalt → bổ sung (khớp StaffEntity: byte[])
                IF OBJECT_ID(N'dbo.Staff', N'U') IS NOT NULL
                   AND COL_LENGTH(N'dbo.Staff', N'PasswordSalt') IS NULL
                BEGIN
                    ALTER TABLE dbo.Staff
                    ADD PasswordSalt VARBINARY(128) NOT NULL
                        CONSTRAINT DF_Staff_PasswordSalt DEFAULT (0x00);
                END

                -- Nếu PasswordHash từng là NVARCHAR (script cũ) → chuyển sang VARBINARY
                IF OBJECT_ID(N'dbo.Staff', N'U') IS NOT NULL
                   AND EXISTS (
                        SELECT 1
                        FROM sys.columns c
                        INNER JOIN sys.types t ON t.user_type_id = c.user_type_id
                        WHERE c.object_id = OBJECT_ID(N'dbo.Staff')
                          AND c.name = N'PasswordHash'
                          AND t.name IN (N'nvarchar', N'varchar', N'nchar', N'char')
                   )
                BEGIN
                    ALTER TABLE dbo.Staff DROP COLUMN PasswordHash;
                    ALTER TABLE dbo.Staff
                    ADD PasswordHash VARBINARY(256) NOT NULL
                        CONSTRAINT DF_Staff_PasswordHash DEFAULT (0x00);
                END

                IF OBJECT_ID(N'dbo.[Order]', N'U') IS NOT NULL
                   AND COL_LENGTH(N'dbo.Order', N'OrderType') IS NULL
                BEGIN
                    ALTER TABLE dbo.[Order]
                    ADD OrderType NVARCHAR(20) NOT NULL
                        CONSTRAINT DF_Order_OrderType DEFAULT (N'ONLINE');
                END

                IF OBJECT_ID(N'dbo.[Order]', N'U') IS NOT NULL
                   AND COL_LENGTH(N'dbo.Order', N'CreatedByStaffId') IS NULL
                BEGIN
                    ALTER TABLE dbo.[Order] ADD CreatedByStaffId INT NULL;
                END

                IF OBJECT_ID(N'dbo.[Order]', N'U') IS NOT NULL
                   AND OBJECT_ID(N'dbo.Staff', N'U') IS NOT NULL
                   AND NOT EXISTS (
                        SELECT 1 FROM sys.foreign_keys
                        WHERE name = N'FK_Order_CreatedByStaff'
                          AND parent_object_id = OBJECT_ID(N'dbo.Order')
                   )
                BEGIN
                    ALTER TABLE dbo.[Order]
                    ADD CONSTRAINT FK_Order_CreatedByStaff
                        FOREIGN KEY (CreatedByStaffId) REFERENCES dbo.Staff(StaffId);
                END

                IF OBJECT_ID(N'dbo.Customer', N'U') IS NOT NULL
                   AND COL_LENGTH(N'dbo.Customer', N'Phone') IS NULL
                BEGIN
                    ALTER TABLE dbo.Customer ADD Phone NVARCHAR(30) NULL;
                END

                IF OBJECT_ID(N'dbo.[Order]', N'U') IS NOT NULL
                   AND COL_LENGTH(N'dbo.Order', N'TransactionId') IS NULL
                BEGIN
                    ALTER TABLE dbo.[Order] ADD TransactionId NVARCHAR(100) NULL;
                END

                IF OBJECT_ID(N'dbo.[Order]', N'U') IS NOT NULL
                   AND COL_LENGTH(N'dbo.Order', N'RefundTransactionId') IS NULL
                BEGIN
                    ALTER TABLE dbo.[Order] ADD RefundTransactionId NVARCHAR(100) NULL;
                END

                IF OBJECT_ID(N'dbo.[Order]', N'U') IS NOT NULL
                   AND COL_LENGTH(N'dbo.Order', N'RefundedAtUtc') IS NULL
                BEGIN
                    ALTER TABLE dbo.[Order] ADD RefundedAtUtc DATETIME2(3) NULL;
                END

                IF OBJECT_ID(N'dbo.[Order]', N'U') IS NOT NULL
                   AND EXISTS (
                        SELECT 1 FROM sys.columns
                        WHERE object_id = OBJECT_ID(N'dbo.[Order]') AND name = N'Note' AND max_length = 1000
                   )
                BEGIN
                    ALTER TABLE dbo.[Order] ALTER COLUMN Note NVARCHAR(2000) NULL;
                END

                IF OBJECT_ID(N'dbo.ProductReviews', N'U') IS NULL
                BEGIN
                    CREATE TABLE dbo.ProductReviews (
                        ReviewId INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_ProductReviews PRIMARY KEY,
                        ProductId INT NOT NULL CONSTRAINT FK_ProductReviews_Component REFERENCES dbo.Component(ComponentId),
                        CustomerName NVARCHAR(200) NOT NULL,
                        CustomerId INT NULL,
                        Rating INT NOT NULL,
                        Comment NVARCHAR(2000) NOT NULL,
                        IsApproved BIT NOT NULL CONSTRAINT DF_ProductReviews_IsApproved DEFAULT (1),
                        CreatedAt DATETIME2(3) NOT NULL CONSTRAINT DF_ProductReviews_CreatedAt DEFAULT (SYSUTCDATETIME())
                    );
                    CREATE INDEX IX_ProductReviews_ProductId ON dbo.ProductReviews(ProductId);
                END
                """, ct);

            await EnsurePhase0SchemaAsync(db, ct);
        }
        catch
        {
            // Ignore providers that do not support these DDL statements.
        }
    }

    /// <summary>
    /// Phase 0: cột Hot/BestSeller, Loyalty, Promo, Q&A, Warranty, mở rộng Order fulfillment.
    /// Idempotent — an toàn khi chạy lại trên DB đã có dữ liệu.
    /// </summary>
    private static async Task EnsurePhase0SchemaAsync(PcStoreDbContext db, CancellationToken ct)
    {
        await db.Database.ExecuteSqlRawAsync("""
            -- Component: Hot / BestSeller
            IF OBJECT_ID(N'dbo.Component', N'U') IS NOT NULL
               AND COL_LENGTH(N'dbo.Component', N'IsHot') IS NULL
                ALTER TABLE dbo.Component ADD IsHot BIT NOT NULL CONSTRAINT DF_Component_IsHot DEFAULT (0);

            IF OBJECT_ID(N'dbo.Component', N'U') IS NOT NULL
               AND COL_LENGTH(N'dbo.Component', N'IsBestSeller') IS NULL
                ALTER TABLE dbo.Component ADD IsBestSeller BIT NOT NULL CONSTRAINT DF_Component_IsBestSeller DEFAULT (0);

            IF OBJECT_ID(N'dbo.Component', N'U') IS NOT NULL
               AND COL_LENGTH(N'dbo.Component', N'ImageUrl') IS NULL
                ALTER TABLE dbo.Component ADD ImageUrl NVARCHAR(400) NULL;

            -- Customer: loyalty + profile
            IF OBJECT_ID(N'dbo.Customer', N'U') IS NOT NULL
               AND COL_LENGTH(N'dbo.Customer', N'LoyaltyPoints') IS NULL
                ALTER TABLE dbo.Customer ADD LoyaltyPoints INT NOT NULL CONSTRAINT DF_Customer_LoyaltyPoints DEFAULT (0);

            IF OBJECT_ID(N'dbo.Customer', N'U') IS NOT NULL
               AND COL_LENGTH(N'dbo.Customer', N'MembershipTier') IS NULL
                ALTER TABLE dbo.Customer ADD MembershipTier NVARCHAR(20) NOT NULL CONSTRAINT DF_Customer_MembershipTier DEFAULT (N'NONE');

            IF OBJECT_ID(N'dbo.Customer', N'U') IS NOT NULL
               AND COL_LENGTH(N'dbo.Customer', N'DateOfBirth') IS NULL
                ALTER TABLE dbo.Customer ADD DateOfBirth DATE NULL;

            IF OBJECT_ID(N'dbo.Customer', N'U') IS NOT NULL
               AND COL_LENGTH(N'dbo.Customer', N'ShippingAddress1') IS NULL
                ALTER TABLE dbo.Customer ADD ShippingAddress1 NVARCHAR(500) NULL;

            IF OBJECT_ID(N'dbo.Customer', N'U') IS NOT NULL
               AND COL_LENGTH(N'dbo.Customer', N'ShippingAddress2') IS NULL
                ALTER TABLE dbo.Customer ADD ShippingAddress2 NVARCHAR(500) NULL;

            IF OBJECT_ID(N'dbo.Customer', N'U') IS NOT NULL
               AND COL_LENGTH(N'dbo.Customer', N'BankAccountInfo') IS NULL
                ALTER TABLE dbo.Customer ADD BankAccountInfo NVARCHAR(300) NULL;

            -- ProductReviews: moderation flag
            IF OBJECT_ID(N'dbo.ProductReviews', N'U') IS NOT NULL
               AND COL_LENGTH(N'dbo.ProductReviews', N'IsApproved') IS NULL
                ALTER TABLE dbo.ProductReviews ADD IsApproved BIT NOT NULL CONSTRAINT DF_ProductReviews_IsApproved DEFAULT (1);

            -- PromoCode
            IF OBJECT_ID(N'dbo.PromoCode', N'U') IS NULL
            BEGIN
                CREATE TABLE dbo.PromoCode (
                    PromoCodeId INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_PromoCode PRIMARY KEY,
                    Code NVARCHAR(40) NOT NULL CONSTRAINT UQ_PromoCode_Code UNIQUE,
                    DiscountType NVARCHAR(20) NOT NULL,
                    DiscountValue DECIMAL(18,2) NOT NULL,
                    MinOrderVnd DECIMAL(18,2) NULL,
                    MaxUses INT NULL,
                    UsedCount INT NOT NULL CONSTRAINT DF_PromoCode_UsedCount DEFAULT (0),
                    StartsAtUtc DATETIME2(3) NULL,
                    ExpiresAtUtc DATETIME2(3) NULL,
                    ApplicableTier NVARCHAR(20) NULL,
                    IsActive BIT NOT NULL CONSTRAINT DF_PromoCode_IsActive DEFAULT (1),
                    Description NVARCHAR(500) NULL,
                    CreatedAtUtc DATETIME2(3) NOT NULL CONSTRAINT DF_PromoCode_CreatedAtUtc DEFAULT (SYSUTCDATETIME())
                );
            END

            -- ProductQuestion (CSKH hỏi đáp)
            IF OBJECT_ID(N'dbo.ProductQuestion', N'U') IS NULL
            BEGIN
                CREATE TABLE dbo.ProductQuestion (
                    ProductQuestionId INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_ProductQuestion PRIMARY KEY,
                    ComponentId INT NOT NULL CONSTRAINT FK_ProductQuestion_Component REFERENCES dbo.Component(ComponentId) ON DELETE CASCADE,
                    CustomerId INT NULL CONSTRAINT FK_ProductQuestion_Customer REFERENCES dbo.Customer(CustomerId),
                    AskerName NVARCHAR(200) NOT NULL,
                    AskerEmail NVARCHAR(200) NULL,
                    Question NVARCHAR(2000) NOT NULL,
                    Answer NVARCHAR(4000) NULL,
                    Status NVARCHAR(20) NOT NULL CONSTRAINT DF_ProductQuestion_Status DEFAULT (N'PENDING'),
                    CreatedAtUtc DATETIME2(3) NOT NULL CONSTRAINT DF_ProductQuestion_CreatedAtUtc DEFAULT (SYSUTCDATETIME()),
                    AnsweredAtUtc DATETIME2(3) NULL
                );
                CREATE INDEX IX_ProductQuestion_ComponentId ON dbo.ProductQuestion(ComponentId);
                CREATE INDEX IX_ProductQuestion_Status ON dbo.ProductQuestion(Status);
            END

            -- WarrantyClaim
            IF OBJECT_ID(N'dbo.WarrantyClaim', N'U') IS NULL
            BEGIN
                CREATE TABLE dbo.WarrantyClaim (
                    WarrantyClaimId INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_WarrantyClaim PRIMARY KEY,
                    OrderId INT NOT NULL CONSTRAINT FK_WarrantyClaim_Order REFERENCES dbo.[Order](OrderId),
                    ComponentId INT NOT NULL CONSTRAINT FK_WarrantyClaim_Component REFERENCES dbo.Component(ComponentId),
                    CustomerId INT NULL CONSTRAINT FK_WarrantyClaim_Customer REFERENCES dbo.Customer(CustomerId),
                    SerialNumber NVARCHAR(100) NULL,
                    IssueDescription NVARCHAR(2000) NOT NULL,
                    Status NVARCHAR(20) NOT NULL CONSTRAINT DF_WarrantyClaim_Status DEFAULT (N'PENDING'),
                    AdminNote NVARCHAR(2000) NULL,
                    CreatedAtUtc DATETIME2(3) NOT NULL CONSTRAINT DF_WarrantyClaim_CreatedAtUtc DEFAULT (SYSUTCDATETIME()),
                    ResolvedAtUtc DATETIME2(3) NULL
                );
                CREATE INDEX IX_WarrantyClaim_OrderId ON dbo.WarrantyClaim(OrderId);
                CREATE INDEX IX_WarrantyClaim_Status ON dbo.WarrantyClaim(Status);
            END

            -- Order: fulfillment + promo + debt
            IF OBJECT_ID(N'dbo.[Order]', N'U') IS NOT NULL
               AND COL_LENGTH(N'dbo.Order', N'AssignedStaffId') IS NULL
                ALTER TABLE dbo.[Order] ADD AssignedStaffId INT NULL;

            IF OBJECT_ID(N'dbo.[Order]', N'U') IS NOT NULL
               AND COL_LENGTH(N'dbo.Order', N'PromoCodeId') IS NULL
                ALTER TABLE dbo.[Order] ADD PromoCodeId INT NULL;

            IF OBJECT_ID(N'dbo.[Order]', N'U') IS NOT NULL
               AND COL_LENGTH(N'dbo.Order', N'DiscountAmountVnd') IS NULL
                ALTER TABLE dbo.[Order] ADD DiscountAmountVnd DECIMAL(18,2) NOT NULL CONSTRAINT DF_Order_DiscountAmountVnd DEFAULT (0);

            IF OBJECT_ID(N'dbo.[Order]', N'U') IS NOT NULL
               AND COL_LENGTH(N'dbo.Order', N'PaymentCollectionStatus') IS NULL
                ALTER TABLE dbo.[Order] ADD PaymentCollectionStatus NVARCHAR(20) NOT NULL CONSTRAINT DF_Order_PaymentCollectionStatus DEFAULT (N'NONE');

            IF OBJECT_ID(N'dbo.[Order]', N'U') IS NOT NULL
               AND COL_LENGTH(N'dbo.Order', N'DepositAmountVnd') IS NULL
                ALTER TABLE dbo.[Order] ADD DepositAmountVnd DECIMAL(18,2) NULL;

            IF OBJECT_ID(N'dbo.[Order]', N'U') IS NOT NULL
               AND COL_LENGTH(N'dbo.Order', N'DeliveredAtUtc') IS NULL
                ALTER TABLE dbo.[Order] ADD DeliveredAtUtc DATETIME2(3) NULL;

            IF OBJECT_ID(N'dbo.[Order]', N'U') IS NOT NULL
               AND COL_LENGTH(N'dbo.Order', N'DebtCollectedAtUtc') IS NULL
                ALTER TABLE dbo.[Order] ADD DebtCollectedAtUtc DATETIME2(3) NULL;

            IF OBJECT_ID(N'dbo.[Order]', N'U') IS NOT NULL
               AND EXISTS (
                    SELECT 1 FROM sys.columns
                    WHERE object_id = OBJECT_ID(N'dbo.[Order]') AND name = N'StatusCode' AND max_length < 80
               )
                ALTER TABLE dbo.[Order] ALTER COLUMN StatusCode NVARCHAR(40) NOT NULL;

            IF OBJECT_ID(N'[dbo].[Order]', N'U') IS NOT NULL
               AND OBJECT_ID(N'dbo.Staff', N'U') IS NOT NULL
               AND COL_LENGTH(N'dbo.Order', N'AssignedStaffId') IS NOT NULL
               AND NOT EXISTS (
                    SELECT 1 FROM sys.foreign_keys
                    WHERE name = N'FK_Order_AssignedStaff'
                      AND parent_object_id = OBJECT_ID(N'[dbo].[Order]')
               )
            BEGIN
                ALTER TABLE dbo.[Order]
                ADD CONSTRAINT FK_Order_AssignedStaff
                    FOREIGN KEY (AssignedStaffId) REFERENCES dbo.Staff(StaffId);
            END

            IF OBJECT_ID(N'[dbo].[Order]', N'U') IS NOT NULL
               AND OBJECT_ID(N'dbo.PromoCode', N'U') IS NOT NULL
               AND COL_LENGTH(N'dbo.Order', N'PromoCodeId') IS NOT NULL
               AND NOT EXISTS (
                    SELECT 1 FROM sys.foreign_keys
                    WHERE name = N'FK_Order_PromoCode'
                      AND parent_object_id = OBJECT_ID(N'[dbo].[Order]')
               )
            BEGIN
                ALTER TABLE dbo.[Order]
                ADD CONSTRAINT FK_Order_PromoCode
                    FOREIGN KEY (PromoCodeId) REFERENCES dbo.PromoCode(PromoCodeId);
            END

            IF OBJECT_ID(N'dbo.Customer', N'U') IS NOT NULL
               AND COL_LENGTH(N'dbo.Customer', N'MembershipTier') IS NOT NULL
               AND NOT EXISTS (
                    SELECT 1 FROM sys.indexes
                    WHERE name = N'IX_Customer_MembershipTier' AND object_id = OBJECT_ID(N'dbo.Customer')
               )
                CREATE INDEX IX_Customer_MembershipTier ON dbo.Customer(MembershipTier);

            -- Index trên Order: bắt buộc dùng OBJECT_ID(N'[dbo].[Order]') vì Order là reserved word
            IF OBJECT_ID(N'[dbo].[Order]', N'U') IS NOT NULL
               AND COL_LENGTH(N'dbo.Order', N'AssignedStaffId') IS NOT NULL
               AND NOT EXISTS (
                    SELECT 1 FROM sys.indexes
                    WHERE name = N'IX_Order_AssignedStaffId' AND object_id = OBJECT_ID(N'[dbo].[Order]')
               )
                CREATE INDEX IX_Order_AssignedStaffId ON dbo.[Order](AssignedStaffId);

            IF OBJECT_ID(N'[dbo].[Order]', N'U') IS NOT NULL
               AND COL_LENGTH(N'dbo.Order', N'PaymentCollectionStatus') IS NOT NULL
               AND NOT EXISTS (
                    SELECT 1 FROM sys.indexes
                    WHERE name = N'IX_Order_PaymentCollectionStatus' AND object_id = OBJECT_ID(N'[dbo].[Order]')
               )
                CREATE INDEX IX_Order_PaymentCollectionStatus ON dbo.[Order](PaymentCollectionStatus);

            IF OBJECT_ID(N'[dbo].[Order]', N'U') IS NOT NULL
               AND NOT EXISTS (
                    SELECT 1 FROM sys.indexes
                    WHERE name = N'IX_Order_StatusCode' AND object_id = OBJECT_ID(N'[dbo].[Order]')
               )
                CREATE INDEX IX_Order_StatusCode ON dbo.[Order](StatusCode);

            -- Phase 2: tránh cộng điểm loyalty trùng
            IF OBJECT_ID(N'[dbo].[Order]', N'U') IS NOT NULL
               AND COL_LENGTH(N'dbo.Order', N'LoyaltyPointsAwarded') IS NULL
                ALTER TABLE dbo.[Order] ADD LoyaltyPointsAwarded BIT NOT NULL
                    CONSTRAINT DF_Order_LoyaltyPointsAwarded DEFAULT (0);

            -- UC11: vận đơn / đơn vị vận chuyển
            IF OBJECT_ID(N'[dbo].[Order]', N'U') IS NOT NULL
               AND COL_LENGTH(N'dbo.Order', N'TrackingNumber') IS NULL
                ALTER TABLE dbo.[Order] ADD TrackingNumber NVARCHAR(80) NULL;

            IF OBJECT_ID(N'[dbo].[Order]', N'U') IS NOT NULL
               AND COL_LENGTH(N'dbo.Order', N'ShippingProvider') IS NULL
                ALTER TABLE dbo.[Order] ADD ShippingProvider NVARCHAR(40) NULL;

            IF OBJECT_ID(N'[dbo].[Order]', N'U') IS NOT NULL
               AND COL_LENGTH(N'dbo.Order', N'ShippedAtUtc') IS NULL
                ALTER TABLE dbo.[Order] ADD ShippedAtUtc DATETIME2(3) NULL;

            IF OBJECT_ID(N'[dbo].[Order]', N'U') IS NOT NULL
               AND COL_LENGTH(N'dbo.Order', N'TrackingNumber') IS NOT NULL
               AND NOT EXISTS (
                    SELECT 1 FROM sys.indexes
                    WHERE name = N'IX_Order_TrackingNumber' AND object_id = OBJECT_ID(N'[dbo].[Order]')
               )
                CREATE INDEX IX_Order_TrackingNumber ON dbo.[Order](TrackingNumber);

            -- Tiến độ lắp ráp: lịch sử trạng thái đơn
            IF OBJECT_ID(N'dbo.OrderTimeline', N'U') IS NULL
            BEGIN
                CREATE TABLE dbo.OrderTimeline (
                    OrderTimelineId INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_OrderTimeline PRIMARY KEY,
                    OrderId INT NOT NULL,
                    StatusCode NVARCHAR(40) NOT NULL,
                    Note NVARCHAR(1000) NULL,
                    ChangedBy NVARCHAR(100) NULL,
                    IssueReasonCode NVARCHAR(40) NULL,
                    CreatedAtUtc DATETIME2(3) NOT NULL CONSTRAINT DF_OrderTimeline_CreatedAtUtc DEFAULT (SYSUTCDATETIME()),
                    CONSTRAINT FK_OrderTimeline_Order FOREIGN KEY (OrderId) REFERENCES dbo.[Order](OrderId) ON DELETE CASCADE
                );
                CREATE INDEX IX_OrderTimeline_OrderId ON dbo.OrderTimeline(OrderId);
                CREATE INDEX IX_OrderTimeline_CreatedAtUtc ON dbo.OrderTimeline(CreatedAtUtc);
            END
            """, ct);
    }

    /// <summary>Thêm SKU mới (Zen 5, B850, RTX 5070…) nếu DB cũ chưa có — không xóa dữ liệu cũ.</summary>
    private static async Task EnsureMarketCatalogUpgradeAsync(PcStoreDbContext db, CancellationToken ct)
    {
        if (await db.Components.AnyAsync(x => x.Sku == "TTG-CPU-R5-9600X", ct))
        {
            await SyncRamModuleCountsAsync(db, ct);
            return;
        }

        int Cat(string code) => db.ComponentCategories.Single(x => x.Code == code).ComponentCategoryId;
        int Brand(string name) => db.Brands.Single(x => x.Name == name).BrandId;
        int Sock(string code) => db.Sockets.Single(x => x.Code == code).SocketId;
        int RamStd(string code) => db.RamStandards.Single(x => x.Code == code).RamStandardId;
        int Form(string code) => db.FormFactors.Single(x => x.Code == code).FormFactorId;

        var now = DateTime.UtcNow;
        ComponentEntity C(int catId, int brandId, string sku, string name, decimal price, int stock) =>
            new()
            {
                ComponentCategoryId = catId,
                BrandId = brandId,
                Sku = sku,
                Name = name,
                PriceVnd = price,
                StockQty = stock,
                IsActive = true,
                CreatedAtUtc = now
            };

        var extras = new List<ComponentEntity>
        {
            C(Cat("CPU"), Brand("AMD"), "TTG-CPU-R5-9600X",
                "AMD Ryzen 5 9600X TRAY (6 nhân, AM5 Zen 5, 65W)", 6_490_000m, 12),
            C(Cat("CPU"), Brand("AMD"), "TTG-CPU-R7-9700X",
                "AMD Ryzen 7 9700X TRAY (8 nhân, AM5 Zen 5, 65W)", 9_290_000m, 8),
            C(Cat("CPU"), Brand("AMD"), "TTG-CPU-R7-9800X3D",
                "AMD Ryzen 7 9800X3D TRAY (8 nhân, AM5 Zen 5 X3D, 120W)", 14_990_000m, 5),
            C(Cat("CPU"), Brand("AMD"), "TTG-CPU-R9-9950X",
                "AMD Ryzen 9 9950X TRAY (16 nhân, AM5 Zen 5, 170W)", 16_990_000m, 3),
            C(Cat("CPU"), Brand("Intel"), "TTG-CPU-I7-14700",
                "Intel Core i7-14700 TRAY (20 nhân, LGA1700, 125W)", 10_490_000m, 6),
            C(Cat("CPU"), Brand("Intel"), "TTG-CPU-I9-14900K",
                "Intel Core i9-14900K (24 nhân, LGA1700, 125W)", 14_990_000m, 4),
            C(Cat("MAINBOARD"), Brand("MSI"), "TTG-MB-B850M-PRO",
                "MSI PRO B850M-P WIFI (AM5, mATX, DDR5)", 4_290_000m, 10),
            C(Cat("MAINBOARD"), Brand("ASUS"), "TTG-MB-TUF-B850-PLUS",
                "ASUS TUF GAMING B850-PLUS WIFI (AM5, ATX, DDR5)", 5_490_000m, 8),
            C(Cat("MAINBOARD"), Brand("Gigabyte"), "TTG-MB-X870-AORUS",
                "Gigabyte X870 AORUS ELITE WIFI7 (AM5, ATX, DDR5)", 7_990_000m, 5),
            C(Cat("GPU"), Brand("NVIDIA"), "TTG-VGA-RTX5070-12",
                "MSI GeForce RTX 5070 12GB Ventus 2X OC", 16_990_000m, 4),
            C(Cat("GPU"), Brand("NVIDIA"), "TTG-VGA-RTX4070S-12",
                "ASUS Dual RTX 4070 Super 12GB", 15_490_000m, 5),
        };

        var existingSkus = await db.Components.AsNoTracking().Select(x => x.Sku).ToListAsync(ct);
        var toAdd = extras.Where(x => !existingSkus.Contains(x.Sku, StringComparer.OrdinalIgnoreCase)).ToList();
        if (toAdd.Count == 0)
        {
            await SyncRamModuleCountsAsync(db, ct);
            return;
        }

        db.Components.AddRange(toAdd);
        await db.SaveChangesAsync(ct);

        int Id(string sku) => db.Components.Local.FirstOrDefault(c => c.Sku == sku)?.ComponentId
            ?? db.Components.Single(c => c.Sku == sku).ComponentId;

        void TryAddCpu(string sku, string socket, string gen, int tdp)
        {
            var id = Id(sku);
            if (db.CpuSpecs.Any(x => x.ComponentId == id)) return;
            db.CpuSpecs.Add(new CpuSpecEntity { ComponentId = id, SocketId = Sock(socket), Generation = gen, TdpWatt = tdp });
        }

        void TryAddMb(string sku, string socket, string chip, string ram, string form, string pcie = "5.0")
        {
            var id = Id(sku);
            if (db.MainboardSpecs.Any(x => x.ComponentId == id)) return;
            db.MainboardSpecs.Add(new MainboardSpecEntity
            {
                ComponentId = id,
                SocketId = Sock(socket),
                Chipset = chip,
                RamStandardId = RamStd(ram),
                FormFactorId = Form(form),
                PcieSlotVersion = pcie
            });
        }

        void TryAddGpu(string sku, int tdp)
        {
            var id = Id(sku);
            if (db.GpuSpecs.Any(x => x.ComponentId == id)) return;
            db.GpuSpecs.Add(new GpuSpecEntity { ComponentId = id, TdpWatt = tdp });
        }

        TryAddCpu("TTG-CPU-R5-9600X", "AM5", "Zen 5", 65);
        TryAddCpu("TTG-CPU-R7-9700X", "AM5", "Zen 5", 65);
        TryAddCpu("TTG-CPU-R7-9800X3D", "AM5", "Zen 5 X3D", 120);
        TryAddCpu("TTG-CPU-R9-9950X", "AM5", "Zen 5", 170);
        TryAddCpu("TTG-CPU-I7-14700", "LGA1700", "Raptor Lake Refresh", 125);
        TryAddCpu("TTG-CPU-I9-14900K", "LGA1700", "Raptor Lake Refresh", 125);
        TryAddMb("TTG-MB-B850M-PRO", "AM5", "B850", "DDR5", "MATX");
        TryAddMb("TTG-MB-TUF-B850-PLUS", "AM5", "B850", "DDR5", "ATX");
        TryAddMb("TTG-MB-X870-AORUS", "AM5", "X870", "DDR5", "ATX");
        TryAddGpu("TTG-VGA-RTX5070-12", 250);
        TryAddGpu("TTG-VGA-RTX4070S-12", 220);

        // Demote legacy 30-series to entry / keep active but they stay for budget.
        await db.SaveChangesAsync(ct);
        await SyncRamModuleCountsAsync(db, ct);
    }

    private static async Task SyncRamModuleCountsAsync(PcStoreDbContext db, CancellationToken ct)
    {
        var rams = await (
            from r in db.RamSpecs
            join c in db.Components on r.ComponentId equals c.ComponentId
            select new { Spec = r, c.Name, c.Sku }).ToListAsync(ct);

        foreach (var row in rams)
        {
            var isKit = row.Name.Contains("(2x", StringComparison.OrdinalIgnoreCase)
                        || row.Sku.Contains("-32-", StringComparison.OrdinalIgnoreCase)
                           && row.Name.Contains("2x", StringComparison.OrdinalIgnoreCase);
            var desired = isKit ? 2 : Math.Max(1, row.Spec.ModuleCount);
            if (row.Spec.ModuleCount != desired)
                row.Spec.ModuleCount = desired;
        }

        await db.SaveChangesAsync(ct);
    }

    private static void SeedReferenceData(PcStoreDbContext db)
    {
        db.ComponentCategories.AddRange(
            new ComponentCategoryEntity { Code = "CPU", DisplayName = "CPU" },
            new ComponentCategoryEntity { Code = "MAINBOARD", DisplayName = "Mainboard" },
            new ComponentCategoryEntity { Code = "RAM", DisplayName = "RAM" },
            new ComponentCategoryEntity { Code = "SSD", DisplayName = "SSD" },
            new ComponentCategoryEntity { Code = "HDD", DisplayName = "HDD" },
            new ComponentCategoryEntity { Code = "GPU", DisplayName = "VGA / GPU" },
            new ComponentCategoryEntity { Code = "PSU", DisplayName = "Nguồn" },
            new ComponentCategoryEntity { Code = "CASE", DisplayName = "Vỏ Case" },
            new ComponentCategoryEntity { Code = "MONITOR", DisplayName = "Màn hình" },
            new ComponentCategoryEntity { Code = "KEYBOARD", DisplayName = "Bàn phím" },
            new ComponentCategoryEntity { Code = "MOUSE", DisplayName = "Chuột" },
            new ComponentCategoryEntity { Code = "HEADSET", DisplayName = "Tai nghe" },
            new ComponentCategoryEntity { Code = "COOLER_AIR", DisplayName = "Tản nhiệt khí" },
            new ComponentCategoryEntity { Code = "COOLER_AIO", DisplayName = "Tản nhiệt nước AIO" },
            new ComponentCategoryEntity { Code = "FAN", DisplayName = "Fan LED case" },
            new ComponentCategoryEntity { Code = "CHAIR", DisplayName = "Ghế gaming" },
            new ComponentCategoryEntity { Code = "PC_PREBUILT", DisplayName = "PC Build San" },
            new ComponentCategoryEntity { Code = "PC_WORKSTATION", DisplayName = "PC Workstation 2D 3D" },
            new ComponentCategoryEntity { Code = "ACCESSORY", DisplayName = "Phụ kiện khác" });

        db.Brands.AddRange(
            new BrandEntity { Name = "Intel" },
            new BrandEntity { Name = "AMD" },
            new BrandEntity { Name = "ASUS" },
            new BrandEntity { Name = "MSI" },
            new BrandEntity { Name = "Gigabyte" },
            new BrandEntity { Name = "Colorful" },
            new BrandEntity { Name = "Corsair" },
            new BrandEntity { Name = "Kingston" },
            new BrandEntity { Name = "G.Skill" },
            new BrandEntity { Name = "Apacer" },
            new BrandEntity { Name = "SSTC" },
            new BrandEntity { Name = "TEAMGROUP" },
            new BrandEntity { Name = "Samsung" },
            new BrandEntity { Name = "Western Digital" },
            new BrandEntity { Name = "NVIDIA" },
            new BrandEntity { Name = "Galax" },
            new BrandEntity { Name = "PowerColor" },
            new BrandEntity { Name = "Sapphire" },
            new BrandEntity { Name = "Seasonic" },
            new BrandEntity { Name = "Cooler Master" },
            new BrandEntity { Name = "NZXT" },
            new BrandEntity { Name = "Phanteks" },
            new BrandEntity { Name = "Deepcool" },
            new BrandEntity { Name = "Dark Flash" },
            new BrandEntity { Name = "AOC" },
            new BrandEntity { Name = "LG" },
            new BrandEntity { Name = "BenQ" },
            new BrandEntity { Name = "Dell" },
            new BrandEntity { Name = "Logitech" },
            new BrandEntity { Name = "Razer" },
            new BrandEntity { Name = "HyperX" },
            new BrandEntity { Name = "Seagate" },
            new BrandEntity { Name = "Keychron" },
            new BrandEntity { Name = "Arctic" });

        db.Sockets.AddRange(
            new SocketEntity { Code = "LGA1700", DisplayName = "LGA 1700" },
            new SocketEntity { Code = "AM5", DisplayName = "AM5" },
            new SocketEntity { Code = "AM4", DisplayName = "AM4" });

        db.RamStandards.AddRange(
            new RamStandardEntity { Code = "DDR4", DisplayName = "DDR4" },
            new RamStandardEntity { Code = "DDR5", DisplayName = "DDR5" });

        db.FormFactors.AddRange(
            new FormFactorEntity { Code = "ATX", DisplayName = "ATX" },
            new FormFactorEntity { Code = "MATX", DisplayName = "Micro-ATX" });
    }

    private static List<ComponentEntity> BuildTtgStyleComponents(
        Func<string, int> cat,
        Func<string, int> brand,
        DateTime now)
    {
        ComponentEntity C(int catId, int brandId, string sku, string name, decimal price, int stock) =>
            new()
            {
                ComponentCategoryId = catId,
                BrandId = brandId,
                Sku = sku,
                Name = name,
                PriceVnd = price,
                StockQty = stock,
                IsActive = true,
                CreatedAtUtc = now
            };

        var list = new List<ComponentEntity>();

        // --- CPU — Intel LGA1700 còn phổ biến + AMD AM4 budget + AM5 Zen 4/5 ---
        list.Add(C(cat("CPU"), brand("Intel"), "TTG-CPU-I5-12400F",
            "Intel Core i5-12400F TRAY (6P+4E, LGA1700, 65W)", 3_350_000m, 24));
        list.Add(C(cat("CPU"), brand("Intel"), "TTG-CPU-I5-13400F",
            "Intel Core i5-13400F TRAY (10 nhân 16 luồng, LGA1700)", 4_190_000m, 18));
        list.Add(C(cat("CPU"), brand("Intel"), "TTG-CPU-I5-14400F",
            "Intel Core i5-14400F TRAY (10 nhân 16 luồng, LGA1700)", 4_650_000m, 20));
        list.Add(C(cat("CPU"), brand("Intel"), "TTG-CPU-I5-13600K",
            "Intel Core i5-13600K (14 nhân, LGA1700, 125W)", 8_200_000m, 10));
        list.Add(C(cat("CPU"), brand("Intel"), "TTG-CPU-I7-13700",
            "Intel Core i7-13700 TRAY (16 nhân, LGA1700, 125W)", 9_890_000m, 8));
        list.Add(C(cat("CPU"), brand("Intel"), "TTG-CPU-I7-14700",
            "Intel Core i7-14700 TRAY (20 nhân, LGA1700, 125W)", 10_490_000m, 6));
        list.Add(C(cat("CPU"), brand("Intel"), "TTG-CPU-I9-14900K",
            "Intel Core i9-14900K (24 nhân, LGA1700, 125W)", 14_990_000m, 4));
        list.Add(C(cat("CPU"), brand("AMD"), "TTG-CPU-R5-5600",
            "AMD Ryzen 5 5600 TRAY (6 nhân 12 luồng, AM4, 65W)", 2_450_000m, 30));
        list.Add(C(cat("CPU"), brand("AMD"), "TTG-CPU-R5-7600",
            "AMD Ryzen 5 7600 TRAY (6 nhân, AM5 Zen 4, 65W)", 4_990_000m, 14));
        list.Add(C(cat("CPU"), brand("AMD"), "TTG-CPU-R7-7800X3D",
            "AMD Ryzen 7 7800X3D TRAY (8 nhân, AM5 Zen 4 X3D, 120W)", 10_990_000m, 6));
        list.Add(C(cat("CPU"), brand("AMD"), "TTG-CPU-R9-7950X",
            "AMD Ryzen 9 7950X TRAY (16 nhân, AM5 Zen 4, 170W)", 14_500_000m, 4));
        list.Add(C(cat("CPU"), brand("AMD"), "TTG-CPU-R5-9600X",
            "AMD Ryzen 5 9600X TRAY (6 nhân, AM5 Zen 5, 65W)", 6_490_000m, 12));
        list.Add(C(cat("CPU"), brand("AMD"), "TTG-CPU-R7-9700X",
            "AMD Ryzen 7 9700X TRAY (8 nhân, AM5 Zen 5, 65W)", 9_290_000m, 8));
        list.Add(C(cat("CPU"), brand("AMD"), "TTG-CPU-R7-9800X3D",
            "AMD Ryzen 7 9800X3D TRAY (8 nhân, AM5 Zen 5 X3D, 120W)", 14_990_000m, 5));
        list.Add(C(cat("CPU"), brand("AMD"), "TTG-CPU-R9-9950X",
            "AMD Ryzen 9 9950X TRAY (16 nhân, AM5 Zen 5, 170W)", 16_990_000m, 3));

        // --- Mainboard ---
        list.Add(C(cat("MAINBOARD"), brand("ASUS"), "TTG-MB-B760M-K-D4",
            "ASUS PRIME B760M-K DDR4 (LGA1700, mATX)", 2_590_000m, 22));
        list.Add(C(cat("MAINBOARD"), brand("MSI"), "TTG-MB-B760M-E-D4",
            "MSI PRO B760M-E DDR4 (LGA1700, mATX)", 2_450_000m, 20));
        list.Add(C(cat("MAINBOARD"), brand("ASUS"), "TTG-MB-B760M-AYW-WIFI-D4",
            "ASUS B760M-AYW WIFI DDR4 (LGA1700, mATX)", 3_290_000m, 12));
        list.Add(C(cat("MAINBOARD"), brand("ASUS"), "TTG-MB-B760M-K-D5",
            "ASUS PRIME B760M-K DDR5 (LGA1700, mATX)", 2_890_000m, 16));
        list.Add(C(cat("MAINBOARD"), brand("MSI"), "TTG-MB-B760M-P-D5",
            "MSI PRO B760M-P DDR5 WIFI (LGA1700, mATX)", 3_450_000m, 14));
        list.Add(C(cat("MAINBOARD"), brand("MSI"), "TTG-MB-Z790-P-D5",
            "MSI PRO Z790-P WIFI DDR5 (LGA1700, ATX)", 5_990_000m, 8));
        list.Add(C(cat("MAINBOARD"), brand("Colorful"), "TTG-MB-B450M-T",
            "Colorful BATTLE-AX B450M-T M.2 V14 (AM4, mATX)", 1_490_000m, 10));
        list.Add(C(cat("MAINBOARD"), brand("MSI"), "TTG-MB-B550M-PRO-VDH",
            "MSI B550M PRO-VDH WIFI (AM4, mATX)", 2_190_000m, 14));
        list.Add(C(cat("MAINBOARD"), brand("MSI"), "TTG-MB-B650M-PRO",
            "MSI PRO B650M-P DDR5 (AM5, mATX)", 2_790_000m, 18));
        list.Add(C(cat("MAINBOARD"), brand("ASUS"), "TTG-MB-B650M-PLUS-WIFI",
            "ASUS TUF GAMING B650M-PLUS WIFI (AM5, mATX)", 3_990_000m, 12));
        list.Add(C(cat("MAINBOARD"), brand("Gigabyte"), "TTG-MB-B650M-AORUS",
            "Gigabyte B650M AORUS ELITE AX (AM5, mATX)", 4_290_000m, 9));
        list.Add(C(cat("MAINBOARD"), brand("ASUS"), "TTG-MB-B760M-A-D5",
            "ASUS PRIME B760M-A WIFI DDR5 (LGA1700, mATX)", 3_150_000m, 15));
        list.Add(C(cat("MAINBOARD"), brand("Gigabyte"), "TTG-MB-B760M-AORUS-D4",
            "Gigabyte B760M AORUS ELITE AX DDR4 (LGA1700, mATX)", 3_650_000m, 10));
        list.Add(C(cat("MAINBOARD"), brand("MSI"), "TTG-MB-B850M-PRO",
            "MSI PRO B850M-P WIFI (AM5, mATX, DDR5)", 4_290_000m, 10));
        list.Add(C(cat("MAINBOARD"), brand("ASUS"), "TTG-MB-TUF-B850-PLUS",
            "ASUS TUF GAMING B850-PLUS WIFI (AM5, ATX, DDR5)", 5_490_000m, 8));
        list.Add(C(cat("MAINBOARD"), brand("Gigabyte"), "TTG-MB-X870-AORUS",
            "Gigabyte X870 AORUS ELITE WIFI7 (AM5, ATX, DDR5)", 7_990_000m, 5));

        // --- RAM (10) ---
        list.Add(C(cat("RAM"), brand("SSTC"), "TTG-RAM-SSTC-16-D4-3200",
            "RAM SSTC 16GB Bus 3200MHz DDR4 Black có tản", 890_000m, 40));
        list.Add(C(cat("RAM"), brand("Kingston"), "TTG-RAM-FURY-16-D4-3600",
            "Kingston FURY Beast 16GB DDR4 3600MHz", 1_050_000m, 35));
        list.Add(C(cat("RAM"), brand("Corsair"), "TTG-RAM-VEN-16-D4-3200",
            "Corsair Vengeance LPX 16GB DDR4 3200MHz", 990_000m, 32));
        list.Add(C(cat("RAM"), brand("G.Skill"), "TTG-RAM-RJ-32-D4-3200",
            "G.Skill Ripjaws V 32GB (2x16) DDR4 3200MHz", 1_890_000m, 20));
        list.Add(C(cat("RAM"), brand("Apacer"), "TTG-RAM-NOX-16-D5-5200",
            "RAM APACER NOX 16GB BUS 5200MHz DDR5 Black", 1_290_000m, 28));
        list.Add(C(cat("RAM"), brand("Kingston"), "TTG-RAM-FURY-16-D5-6000",
            "Kingston FURY Beast 16GB DDR5 6000MHz", 1_450_000m, 25));
        list.Add(C(cat("RAM"), brand("Corsair"), "TTG-RAM-VEN-32-D5-6000",
            "Corsair Vengeance 32GB (2x16) DDR5 6000MHz", 2_690_000m, 15));
        list.Add(C(cat("RAM"), brand("G.Skill"), "TTG-RAM-TRZ5-32-D5-6400",
            "G.Skill Trident Z5 RGB 32GB (2x16) DDR5 6400MHz", 3_290_000m, 10));
        list.Add(C(cat("RAM"), brand("TEAMGROUP"), "TTG-RAM-DELTA-16-D5-5600",
            "TEAMGROUP T-Force Delta RGB 16GB DDR5 5600MHz", 1_190_000m, 22));
        list.Add(C(cat("RAM"), brand("Samsung"), "TTG-RAM-SAM-16-D5-5600",
            "Samsung DDR5 16GB 5600MHz (1x16)", 1_350_000m, 18));

        // --- GPU — mid 40-series + 50-series; 30-series chỉ entry ---
        list.Add(C(cat("GPU"), brand("NVIDIA"), "TTG-VGA-RTX3050-8",
            "Galax GeForce RTX 3050 8GB 1-Click OC (entry)", 4_490_000m, 10));
        list.Add(C(cat("GPU"), brand("NVIDIA"), "TTG-VGA-RTX4060-8",
            "ASUS Dual RTX 4060 OC 8GB", 7_990_000m, 14));
        list.Add(C(cat("GPU"), brand("NVIDIA"), "TTG-VGA-RTX4060TI-8",
            "Gigabyte RTX 4060 Ti Eagle 8GB", 9_890_000m, 10));
        list.Add(C(cat("GPU"), brand("NVIDIA"), "TTG-VGA-RTX4060TI-16",
            "MSI Ventus RTX 4060 Ti 16GB", 11_490_000m, 7));
        list.Add(C(cat("GPU"), brand("NVIDIA"), "TTG-VGA-RTX4070-12",
            "ASUS Dual RTX 4070 12GB", 13_900_000m, 6));
        list.Add(C(cat("GPU"), brand("NVIDIA"), "TTG-VGA-RTX4070S-12",
            "ASUS Dual RTX 4070 Super 12GB", 15_490_000m, 5));
        list.Add(C(cat("GPU"), brand("NVIDIA"), "TTG-VGA-RTX5060-8",
            "MSI GeForce RTX 5060 8GB Ventus White OC", 8_990_000m, 8));
        list.Add(C(cat("GPU"), brand("NVIDIA"), "TTG-VGA-RTX5060TI-16",
            "ASUS PRIME RTX 5060 Ti 16GB OC", 12_990_000m, 5));
        list.Add(C(cat("GPU"), brand("NVIDIA"), "TTG-VGA-RTX5070-12",
            "MSI GeForce RTX 5070 12GB Ventus 2X OC", 16_990_000m, 4));
        list.Add(C(cat("GPU"), brand("AMD"), "TTG-VGA-RX7600-8",
            "Gigabyte Radeon RX 7600 GAMING OC 8GB", 6_890_000m, 9));
        list.Add(C(cat("GPU"), brand("Sapphire"), "TTG-VGA-RX7700XT-12",
            "Sapphire Pulse RX 7700 XT 12GB", 9_290_000m, 7));
        list.Add(C(cat("GPU"), brand("AMD"), "TTG-VGA-RX7800XT-16",
            "Gigabyte RX 7800 GAMING OC 16GB", 11_990_000m, 5));

        // --- PSU ---
        list.Add(C(cat("PSU"), brand("SSTC"), "TTG-PSU-SSTC-550F",
            "Nguồn máy tính SSTC 550F 550W 80 Plus Bronze", 890_000m, 25));
        list.Add(C(cat("PSU"), brand("Gigabyte"), "TTG-PSU-P650SS",
            "Nguồn Gigabyte P650SS 650W 80 Plus Silver ATX3.0", 1_490_000m, 30));
        list.Add(C(cat("PSU"), brand("Cooler Master"), "TTG-PSU-MWE650",
            "Cooler Master MWE Bronze V2 650W", 1_290_000m, 22));
        list.Add(C(cat("PSU"), brand("Corsair"), "TTG-PSU-CV650",
            "Corsair CV650 650W 80 Plus Bronze", 1_390_000m, 24));
        list.Add(C(cat("PSU"), brand("MSI"), "TTG-PSU-A650BN",
            "MSI MAG A650BN 650W 80 Plus Bronze", 1_190_000m, 26));
        list.Add(C(cat("PSU"), brand("Corsair"), "TTG-PSU-RM750E",
            "Corsair RM750e 750W 80+ Gold ATX3.1", 2_190_000m, 14));
        list.Add(C(cat("PSU"), brand("Seasonic"), "TTG-PSU-FOCUS750",
            "Seasonic Focus GX-750 750W 80+ Gold Full Modular", 2_490_000m, 12));
        list.Add(C(cat("PSU"), brand("Gigabyte"), "TTG-PSU-UD850GM",
            "Gigabyte UD850GM PG5 850W 80+ Gold", 2_890_000m, 10));
        list.Add(C(cat("PSU"), brand("ASUS"), "TTG-PSU-ROG850",
            "ASUS ROG STRIX 850W 80+ Gold ATX3.0", 3_990_000m, 6));

        // --- SSD (10) ---
        list.Add(C(cat("SSD"), brand("SSTC"), "TTG-SSD-SSTC-E130-512",
            "SSD SSTC Oceanic E130 512GB M.2 NVMe Gen3", 690_000m, 45));
        list.Add(C(cat("SSD"), brand("Samsung"), "TTG-SSD-980-500",
            "Samsung 980 500GB NVMe M.2 PCIe 3.0", 990_000m, 30));
        list.Add(C(cat("SSD"), brand("Western Digital"), "TTG-SSD-SN580-1T",
            "WD Blue SN580 1TB NVMe Gen4", 1_490_000m, 28));
        list.Add(C(cat("SSD"), brand("Kingston"), "TTG-SSD-NV2-1T",
            "Kingston NV2 1TB M.2 NVMe Gen4", 1_290_000m, 35));
        list.Add(C(cat("SSD"), brand("Samsung"), "TTG-SSD-990PRO-1T",
            "Samsung 990 PRO 1TB NVMe Gen4", 2_690_000m, 18));
        list.Add(C(cat("SSD"), brand("Corsair"), "TTG-SSD-MP600-1T",
            "Corsair MP600 Elite 1TB Gen4", 2_190_000m, 12));
        list.Add(C(cat("SSD"), brand("Gigabyte"), "TTG-SSD-AORUS-1T",
            "Gigabyte AORUS Gen4 7000s 1TB", 2_390_000m, 10));
        list.Add(C(cat("SSD"), brand("Samsung"), "TTG-SSD-870EVO-1T",
            "Samsung 870 EVO 1TB SATA3 2.5\"", 1_690_000m, 20));
        list.Add(C(cat("SSD"), brand("Kingston"), "TTG-SSD-KC3000-2T",
            "Kingston KC3000 2TB NVMe Gen4", 4_290_000m, 8));
        list.Add(C(cat("SSD"), brand("Western Digital"), "TTG-SSD-SN850X-2T",
            "WD Black SN850X 2TB NVMe Gen4", 4_990_000m, 7));

        // --- HDD (2) ---
        list.Add(C(cat("HDD"), brand("Western Digital"), "TTG-HDD-WD2T-BLUE",
            "Ổ cứng HDD WD Blue 2TB 7200rpm SATA", 1_390_000m, 15));
        list.Add(C(cat("HDD"), brand("Seagate"), "TTG-HDD-SG4T-BARR",
            "Seagate BarraCuda 4TB 5400rpm SATA", 2_190_000m, 10));

        // --- CASE (5) ---
        list.Add(C(cat("CASE"), brand("NZXT"), "TTG-CASE-H5FLOW",
            "Vỏ case NZXT H5 Flow RGB Mid Tower", 2_490_000m, 8));
        list.Add(C(cat("CASE"), brand("Phanteks"), "TTG-CASE-XT523",
            "Vỏ Phanteks XT523 Ultra Mid Tower Black", 1_890_000m, 10));
        list.Add(C(cat("CASE"), brand("Cooler Master"), "TTG-CASE-TD500",
            "Cooler Master MasterBox TD500 Mesh V2", 1_990_000m, 9));
        list.Add(C(cat("CASE"), brand("Dark Flash"), "TTG-CASE-DLXM22",
            "Case Dark Flash DLX-M22 Mesh RGB", 890_000m, 14));
        list.Add(C(cat("CASE"), brand("MSI"), "TTG-CASE-MAGFORGE",
            "MSI MAG FORGE 320R Airflow RGB", 1_290_000m, 12));

        // --- MONITOR (6) ---
        list.Add(C(cat("MONITOR"), brand("AOC"), "TTG-MNT-AOC24G4",
            "Màn AOC 24G4 24\" IPS 180Hz FHD", 2_990_000m, 16));
        list.Add(C(cat("MONITOR"), brand("LG"), "TTG-MNT-LG27GP850",
            "Màn LG UltraGear 27GP850-B 27\" NanoIPS 165Hz QHD", 7_990_000m, 8));
        list.Add(C(cat("MONITOR"), brand("BenQ"), "TTG-MNT-BENQEX240",
            "BenQ MOBIUZ EX240 24\" IPS 165Hz", 3_490_000m, 11));
        list.Add(C(cat("MONITOR"), brand("Dell"), "TTG-MNT-DELL2721D",
            "Dell G2724D 27\" IPS 165Hz QHD", 5_990_000m, 9));
        list.Add(C(cat("MONITOR"), brand("AOC"), "TTG-MNT-AOCQ27G3X",
            "AOC Q27G3XMN 27\" VA QHD 180Hz MiniLED", 6_490_000m, 7));
        list.Add(C(cat("MONITOR"), brand("LG"), "TTG-MNT-LG32UQ850",
            "LG UltraFine 32UQ850-W 32\" 4K IPS Black", 12_990_000m, 4));

        // --- KEYBOARD (2) ---
        list.Add(C(cat("KEYBOARD"), brand("Logitech"), "TTG-KB-G713",
            "Logitech G713 GX TKL Lightsync White", 2_290_000m, 14));
        list.Add(C(cat("KEYBOARD"), brand("Razer"), "TTG-KB-BWTEV3",
            "Razer BlackWidow V3 Tenkeyless Green Switch", 1_990_000m, 16));

        // --- MOUSE (2) ---
        list.Add(C(cat("MOUSE"), brand("Logitech"), "TTG-MS-G502X",
            "Logitech G502 X Lightspeed Wireless", 1_890_000m, 22));
        list.Add(C(cat("MOUSE"), brand("Razer"), "TTG-MS-DAV3",
            "Razer DeathAdder V3 Pro Black", 2_490_000m, 18));

        // --- HEADSET (2) ---
        list.Add(C(cat("HEADSET"), brand("HyperX"), "TTG-HS-CLOD2",
            "HyperX Cloud II Wireless 7.1", 2_990_000m, 15));
        list.Add(C(cat("HEADSET"), brand("Razer"), "TTG-HS-BKSHV3",
            "Razer BlackShark V3 Pro Wireless", 4_290_000m, 10));

        // --- COOLER (3) ---
        list.Add(C(cat("COOLER_AIR"), brand("Cooler Master"), "TTG-AIR-H212",
            "Cooler Master Hyper 212 Spectrum V3", 590_000m, 25));
        list.Add(C(cat("COOLER_AIR"), brand("Deepcool"), "TTG-AIR-AG400",
            "Deepcool AG400 LED (tản khí)", 450_000m, 30));
        list.Add(C(cat("COOLER_AIO"), brand("Cooler Master"), "TTG-AIO-ML240L",
            "Cooler Master MasterLiquid ML240L ARGB V2", 1_690_000m, 12));

        // --- FAN (2) ---
        list.Add(C(cat("FAN"), brand("Deepcool"), "TTG-FAN-CF120",
            "Deepcool CF120 Plus 3in1 120mm RGB", 890_000m, 20));
        list.Add(C(cat("FAN"), brand("Cooler Master"), "TTG-FAN-MF120",
            "Cooler Master MasterFan MF120 HALO2 3 pack", 990_000m, 18));

        // --- CHAIR (1) ---
        list.Add(C(cat("CHAIR"), brand("Razer"), "TTG-CHAIR-ISkur",
            "Ghế Razer Iskur V2 X — Ergonomic Gaming", 8_990_000m, 4));

        // --- ACCESSORY (2) ---
        list.Add(C(cat("ACCESSORY"), brand("Logitech"), "TTG-ACC-LITEPAD",
            "Logitech Litra Glow LED streaming", 1_290_000m, 12));
        list.Add(C(cat("ACCESSORY"), brand("NZXT"), "TTG-ACC-INTHUB",
            "Bộ hub NZXT Internal USB Hub Gen3", 690_000m, 15));

        // --- PC WORKSTATION 2D/3D (8) ---
        list.Add(C(cat("PC_WORKSTATION"), brand("Intel"), "TTG-WS-I5-12600K-RTX4060",
            "PC Workstation 2D/3D i5-12600K | 32GB DDR4 | RTX 4060 8GB", 31_990_000m, 6));
        list.Add(C(cat("PC_WORKSTATION"), brand("Intel"), "TTG-WS-I7-13700-RTX4070",
            "PC Workstation 2D/3D i7-13700 | 32GB DDR5 | RTX 4070 12GB", 45_990_000m, 5));
        list.Add(C(cat("PC_WORKSTATION"), brand("Intel"), "TTG-WS-I7-14700-RTX4070S",
            "PC Workstation 3D Render i7-14700 | 64GB DDR5 | RTX 4070 Super", 56_900_000m, 4));
        list.Add(C(cat("PC_WORKSTATION"), brand("Intel"), "TTG-WS-I9-14900-RTX4080S",
            "PC Workstation CAD/BIM i9-14900 | 64GB DDR5 | RTX 4080 Super", 82_500_000m, 2));
        list.Add(C(cat("PC_WORKSTATION"), brand("AMD"), "TTG-WS-R7-7700-RTX4060TI",
            "PC Workstation 2D/3D Ryzen 7 7700 | 32GB DDR5 | RTX 4060 Ti 16GB", 39_500_000m, 5));
        list.Add(C(cat("PC_WORKSTATION"), brand("AMD"), "TTG-WS-R9-7900-RTX4070TI",
            "PC Workstation dựng hình Ryzen 9 7900 | 64GB DDR5 | RTX 4070 Ti", 62_900_000m, 3));
        list.Add(C(cat("PC_WORKSTATION"), brand("AMD"), "TTG-WS-R9-7950X-RTX4080S",
            "PC Workstation render Ryzen 9 7950X | 64GB DDR5 | RTX 4080 Super", 86_900_000m, 2));
        list.Add(C(cat("PC_WORKSTATION"), brand("AMD"), "TTG-WS-R9-9950X-RTX4090",
            "PC Workstation AI/3D Ryzen 9 9950X | 128GB DDR5 | RTX 4090 24GB", 129_000_000m, 1));

        // --- PC PREBUILT (8) ---
        list.Add(C(cat("PC_PREBUILT"), brand("Intel"), "TTG-PB-I5-14400F-RTX4060",
            "PC Gaming i5-14400F | 16GB DDR4 | RTX 4060 8GB", 24_990_000m, 7));
        list.Add(C(cat("PC_PREBUILT"), brand("Intel"), "TTG-PB-I5-14600KF-RTX4060TI",
            "PC Gaming i5-14600KF | 32GB DDR5 | RTX 4060 Ti 16GB", 33_990_000m, 6));
        list.Add(C(cat("PC_PREBUILT"), brand("Intel"), "TTG-PB-I7-14700-RTX4070S",
            "PC Gaming i7-14700 | 32GB DDR5 | RTX 4070 Super", 52_990_000m, 4));
        list.Add(C(cat("PC_PREBUILT"), brand("Intel"), "TTG-PB-I9-14900K-RTX4080S",
            "PC Flagship i9-14900K | 64GB DDR5 | RTX 4080 Super", 84_990_000m, 2));
        list.Add(C(cat("PC_PREBUILT"), brand("AMD"), "TTG-PB-R5-7600-RTX4060",
            "PC Gaming Ryzen 5 7600 | 16GB DDR5 | RTX 4060 8GB", 26_490_000m, 8));
        list.Add(C(cat("PC_PREBUILT"), brand("AMD"), "TTG-PB-R7-7800X3D-RTX4070",
            "PC Esports Ryzen 7 7800X3D | 32GB DDR5 | RTX 4070 12GB", 46_990_000m, 5));
        list.Add(C(cat("PC_PREBUILT"), brand("AMD"), "TTG-PB-R9-7900-RTX4070TI",
            "PC Creator Ryzen 9 7900 | 64GB DDR5 | RTX 4070 Ti", 63_990_000m, 3));
        list.Add(C(cat("PC_PREBUILT"), brand("AMD"), "TTG-PB-R9-9950X-RTX4090",
            "PC Extreme Ryzen 9 9950X | 128GB DDR5 | RTX 4090 24GB", 132_000_000m, 1));

        // --- Bổ sung ~8 món để đạt ~100 SKU (cùng phong cách TTG / thị trường VN) ---
        list.Add(C(cat("SSD"), brand("Apacer"), "TTG-SSD-APACER-512",
            "SSD Apacer AS2280P4 512GB NVMe Gen4", 790_000m, 32));
        list.Add(C(cat("SSD"), brand("TEAMGROUP"), "TTG-SSD-TG-MP33-512",
            "TEAMGROUP MP33 512GB M.2 NVMe Gen3", 650_000m, 38));
        list.Add(C(cat("CASE"), brand("Deepcool"), "TTG-CASE-MATREXX55",
            "Case Deepcool MATREXX 55 MESH ADD-RGB 4F", 1_590_000m, 11));
        list.Add(C(cat("MONITOR"), brand("MSI"), "TTG-MNT-MSI-G2412",
            "MSI G2412 24\" IPS 170Hz FHD", 3_290_000m, 13));
        list.Add(C(cat("KEYBOARD"), brand("Keychron"), "TTG-KB-K2-V2",
            "Keychron K2 V2 Wireless Mechanical RGB 75%", 2_590_000m, 12));
        list.Add(C(cat("MOUSE"), brand("HyperX"), "TTG-MS-PULSEFIRE-HST2",
            "HyperX Pulsefire Haste 2 Wireless 61g", 1_590_000m, 17));
        list.Add(C(cat("ACCESSORY"), brand("Razer"), "TTG-ACC-GIGANTUSV2",
            "Razer Gigantus V2 XXLarge — Mousepad 940x410mm", 890_000m, 20));
        list.Add(C(cat("FAN"), brand("Arctic"), "TTG-FAN-P12-PST",
            "Arctic P12 PWM PST 120mm (5 pack)", 490_000m, 28));

        return list;
    }

    private static IEnumerable<CpuSpecEntity> BuildCpuSpecs(IReadOnlyList<ComponentEntity> all, Func<string, int> sock)
    {
        int Id(string sku) => all.Single(c => c.Sku == sku).ComponentId;

        return new[]
        {
            new CpuSpecEntity { ComponentId = Id("TTG-CPU-I5-12400F"), SocketId = sock("LGA1700"), Generation = "Alder Lake", TdpWatt = 65 },
            new CpuSpecEntity { ComponentId = Id("TTG-CPU-I5-13400F"), SocketId = sock("LGA1700"), Generation = "Raptor Lake", TdpWatt = 65 },
            new CpuSpecEntity { ComponentId = Id("TTG-CPU-I5-14400F"), SocketId = sock("LGA1700"), Generation = "Raptor Lake Refresh", TdpWatt = 65 },
            new CpuSpecEntity { ComponentId = Id("TTG-CPU-I5-13600K"), SocketId = sock("LGA1700"), Generation = "Raptor Lake", TdpWatt = 125 },
            new CpuSpecEntity { ComponentId = Id("TTG-CPU-I7-13700"), SocketId = sock("LGA1700"), Generation = "Raptor Lake", TdpWatt = 125 },
            new CpuSpecEntity { ComponentId = Id("TTG-CPU-I7-14700"), SocketId = sock("LGA1700"), Generation = "Raptor Lake Refresh", TdpWatt = 125 },
            new CpuSpecEntity { ComponentId = Id("TTG-CPU-I9-14900K"), SocketId = sock("LGA1700"), Generation = "Raptor Lake Refresh", TdpWatt = 125 },
            new CpuSpecEntity { ComponentId = Id("TTG-CPU-R5-5600"), SocketId = sock("AM4"), Generation = "Zen 3", TdpWatt = 65 },
            new CpuSpecEntity { ComponentId = Id("TTG-CPU-R5-7600"), SocketId = sock("AM5"), Generation = "Zen 4", TdpWatt = 65 },
            new CpuSpecEntity { ComponentId = Id("TTG-CPU-R7-7800X3D"), SocketId = sock("AM5"), Generation = "Zen 4 X3D", TdpWatt = 120 },
            new CpuSpecEntity { ComponentId = Id("TTG-CPU-R9-7950X"), SocketId = sock("AM5"), Generation = "Zen 4", TdpWatt = 170 },
            new CpuSpecEntity { ComponentId = Id("TTG-CPU-R5-9600X"), SocketId = sock("AM5"), Generation = "Zen 5", TdpWatt = 65 },
            new CpuSpecEntity { ComponentId = Id("TTG-CPU-R7-9700X"), SocketId = sock("AM5"), Generation = "Zen 5", TdpWatt = 65 },
            new CpuSpecEntity { ComponentId = Id("TTG-CPU-R7-9800X3D"), SocketId = sock("AM5"), Generation = "Zen 5 X3D", TdpWatt = 120 },
            new CpuSpecEntity { ComponentId = Id("TTG-CPU-R9-9950X"), SocketId = sock("AM5"), Generation = "Zen 5", TdpWatt = 170 }
        };
    }

    private static IEnumerable<MainboardSpecEntity> BuildMainboardSpecs(
        IReadOnlyList<ComponentEntity> all,
        Func<string, int> sock,
        Func<string, int> ram,
        Func<string, int> form)
    {
        int Id(string sku) => all.Single(c => c.Sku == sku).ComponentId;

        static MainboardSpecEntity Mb(int compId, int socketId, string chip, int ramId, int formId, string pcie = "4.0") =>
            new()
            {
                ComponentId = compId,
                SocketId = socketId,
                Chipset = chip,
                RamStandardId = ramId,
                FormFactorId = formId,
                PcieSlotVersion = pcie
            };

        return new[]
        {
            Mb(Id("TTG-MB-B760M-K-D4"), sock("LGA1700"), "B760", ram("DDR4"), form("MATX")),
            Mb(Id("TTG-MB-B760M-E-D4"), sock("LGA1700"), "B760", ram("DDR4"), form("MATX")),
            Mb(Id("TTG-MB-B760M-AYW-WIFI-D4"), sock("LGA1700"), "B760", ram("DDR4"), form("MATX")),
            Mb(Id("TTG-MB-B760M-K-D5"), sock("LGA1700"), "B760", ram("DDR5"), form("MATX")),
            Mb(Id("TTG-MB-B760M-P-D5"), sock("LGA1700"), "B760", ram("DDR5"), form("MATX")),
            Mb(Id("TTG-MB-Z790-P-D5"), sock("LGA1700"), "Z790", ram("DDR5"), form("ATX")),
            Mb(Id("TTG-MB-B450M-T"), sock("AM4"), "B450", ram("DDR4"), form("MATX")),
            Mb(Id("TTG-MB-B550M-PRO-VDH"), sock("AM4"), "B550", ram("DDR4"), form("MATX")),
            Mb(Id("TTG-MB-B650M-PRO"), sock("AM5"), "B650", ram("DDR5"), form("MATX")),
            Mb(Id("TTG-MB-B650M-PLUS-WIFI"), sock("AM5"), "B650", ram("DDR5"), form("MATX")),
            Mb(Id("TTG-MB-B650M-AORUS"), sock("AM5"), "B650", ram("DDR5"), form("MATX")),
            Mb(Id("TTG-MB-B760M-A-D5"), sock("LGA1700"), "B760", ram("DDR5"), form("MATX")),
            Mb(Id("TTG-MB-B760M-AORUS-D4"), sock("LGA1700"), "B760", ram("DDR4"), form("MATX")),
            Mb(Id("TTG-MB-B850M-PRO"), sock("AM5"), "B850", ram("DDR5"), form("MATX"), "5.0"),
            Mb(Id("TTG-MB-TUF-B850-PLUS"), sock("AM5"), "B850", ram("DDR5"), form("ATX"), "5.0"),
            Mb(Id("TTG-MB-X870-AORUS"), sock("AM5"), "X870", ram("DDR5"), form("ATX"), "5.0")
        };
    }

    private static IEnumerable<RamSpecEntity> BuildRamSpecs(
        IReadOnlyList<ComponentEntity> all,
        Func<string, int> ram)
    {
        int Id(string sku) => all.Single(c => c.Sku == sku).ComponentId;

        static RamSpecEntity R(int id, int std, int gb, int mhz, int modules) =>
            new() { ComponentId = id, RamStandardId = std, CapacityGb = gb, SpeedMhz = mhz, ModuleCount = modules };

        return new[]
        {
            R(Id("TTG-RAM-SSTC-16-D4-3200"), ram("DDR4"), 16, 3200, 1),
            R(Id("TTG-RAM-FURY-16-D4-3600"), ram("DDR4"), 16, 3600, 1),
            R(Id("TTG-RAM-VEN-16-D4-3200"), ram("DDR4"), 16, 3200, 1),
            R(Id("TTG-RAM-RJ-32-D4-3200"), ram("DDR4"), 32, 3200, 2),
            R(Id("TTG-RAM-NOX-16-D5-5200"), ram("DDR5"), 16, 5200, 1),
            R(Id("TTG-RAM-FURY-16-D5-6000"), ram("DDR5"), 16, 6000, 1),
            R(Id("TTG-RAM-VEN-32-D5-6000"), ram("DDR5"), 32, 6000, 2),
            R(Id("TTG-RAM-TRZ5-32-D5-6400"), ram("DDR5"), 32, 6400, 2),
            R(Id("TTG-RAM-DELTA-16-D5-5600"), ram("DDR5"), 16, 5600, 1),
            R(Id("TTG-RAM-SAM-16-D5-5600"), ram("DDR5"), 16, 5600, 1)
        };
    }

    private static IEnumerable<GpuSpecEntity> BuildGpuSpecs(IReadOnlyList<ComponentEntity> all)
    {
        int Id(string sku) => all.Single(c => c.Sku == sku).ComponentId;

        return new[]
        {
            new GpuSpecEntity { ComponentId = Id("TTG-VGA-RTX3050-8"), TdpWatt = 130 },
            new GpuSpecEntity { ComponentId = Id("TTG-VGA-RTX4060-8"), TdpWatt = 115 },
            new GpuSpecEntity { ComponentId = Id("TTG-VGA-RTX4060TI-8"), TdpWatt = 160 },
            new GpuSpecEntity { ComponentId = Id("TTG-VGA-RTX4060TI-16"), TdpWatt = 165 },
            new GpuSpecEntity { ComponentId = Id("TTG-VGA-RTX4070-12"), TdpWatt = 200 },
            new GpuSpecEntity { ComponentId = Id("TTG-VGA-RTX4070S-12"), TdpWatt = 220 },
            new GpuSpecEntity { ComponentId = Id("TTG-VGA-RTX5060-8"), TdpWatt = 150 },
            new GpuSpecEntity { ComponentId = Id("TTG-VGA-RTX5060TI-16"), TdpWatt = 180 },
            new GpuSpecEntity { ComponentId = Id("TTG-VGA-RTX5070-12"), TdpWatt = 250 },
            new GpuSpecEntity { ComponentId = Id("TTG-VGA-RX7600-8"), TdpWatt = 165 },
            new GpuSpecEntity { ComponentId = Id("TTG-VGA-RX7700XT-12"), TdpWatt = 245 },
            new GpuSpecEntity { ComponentId = Id("TTG-VGA-RX7800XT-16"), TdpWatt = 263 }
        };
    }

    private static IEnumerable<PsuSpecEntity> BuildPsuSpecs(IReadOnlyList<ComponentEntity> all)
    {
        int Id(string sku) => all.Single(c => c.Sku == sku).ComponentId;

        return new[]
        {
            new PsuSpecEntity { ComponentId = Id("TTG-PSU-SSTC-550F"), CapacityWatt = 550, Efficiency = "80+ Bronze" },
            new PsuSpecEntity { ComponentId = Id("TTG-PSU-P650SS"), CapacityWatt = 650, Efficiency = "80+ Silver" },
            new PsuSpecEntity { ComponentId = Id("TTG-PSU-MWE650"), CapacityWatt = 650, Efficiency = "80+ Bronze" },
            new PsuSpecEntity { ComponentId = Id("TTG-PSU-CV650"), CapacityWatt = 650, Efficiency = "80+ Bronze" },
            new PsuSpecEntity { ComponentId = Id("TTG-PSU-A650BN"), CapacityWatt = 650, Efficiency = "80+ Bronze" },
            new PsuSpecEntity { ComponentId = Id("TTG-PSU-RM750E"), CapacityWatt = 750, Efficiency = "80+ Gold" },
            new PsuSpecEntity { ComponentId = Id("TTG-PSU-FOCUS750"), CapacityWatt = 750, Efficiency = "80+ Gold" },
            new PsuSpecEntity { ComponentId = Id("TTG-PSU-UD850GM"), CapacityWatt = 850, Efficiency = "80+ Gold" },
            new PsuSpecEntity { ComponentId = Id("TTG-PSU-ROG850"), CapacityWatt = 850, Efficiency = "80+ Gold" }
        };
    }
}
