using ban_link_kien_PC.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;

namespace ban_link_kien_PC.Infrastructure.Persistence;

public sealed class PcStoreDbContext : DbContext
{
    public PcStoreDbContext(DbContextOptions<PcStoreDbContext> options) : base(options) { }

    public DbSet<BrandEntity> Brands => Set<BrandEntity>();
    public DbSet<ComponentCategoryEntity> ComponentCategories => Set<ComponentCategoryEntity>();
    public DbSet<SocketEntity> Sockets => Set<SocketEntity>();
    public DbSet<RamStandardEntity> RamStandards => Set<RamStandardEntity>();
    public DbSet<FormFactorEntity> FormFactors => Set<FormFactorEntity>();

    public DbSet<ComponentEntity> Components => Set<ComponentEntity>();
    public DbSet<CpuSpecEntity> CpuSpecs => Set<CpuSpecEntity>();
    public DbSet<MainboardSpecEntity> MainboardSpecs => Set<MainboardSpecEntity>();
    public DbSet<RamSpecEntity> RamSpecs => Set<RamSpecEntity>();
    public DbSet<GpuSpecEntity> GpuSpecs => Set<GpuSpecEntity>();
    public DbSet<PsuSpecEntity> PsuSpecs => Set<PsuSpecEntity>();

    public DbSet<BuildConfigurationEntity> BuildConfigurations => Set<BuildConfigurationEntity>();
    public DbSet<BuildConfigurationItemEntity> BuildConfigurationItems => Set<BuildConfigurationItemEntity>();

    public DbSet<CustomerEntity> Customers => Set<CustomerEntity>();
    public DbSet<CartEntity> Carts => Set<CartEntity>();
    public DbSet<CartItemEntity> CartItems => Set<CartItemEntity>();

    public DbSet<RoleEntity> Roles => Set<RoleEntity>();
    public DbSet<StaffEntity> Staffs => Set<StaffEntity>();

    public DbSet<OrderEntity> Orders => Set<OrderEntity>();
    public DbSet<OrderItemEntity> OrderItems => Set<OrderItemEntity>();
    public DbSet<OrderTimelineEntity> OrderTimelines => Set<OrderTimelineEntity>();
    public DbSet<ProductReviewEntity> ProductReviews => Set<ProductReviewEntity>();
    public DbSet<PromoCodeEntity> PromoCodes => Set<PromoCodeEntity>();
    public DbSet<ProductQuestionEntity> ProductQuestions => Set<ProductQuestionEntity>();
    public DbSet<WarrantyClaimEntity> WarrantyClaims => Set<WarrantyClaimEntity>();

    public DbSet<StockWaitlistEntity> StockWaitlists => Set<StockWaitlistEntity>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<BrandEntity>(b =>
        {
            b.ToTable("Brand", "dbo");
            b.HasKey(x => x.BrandId);
            b.HasIndex(x => x.Name).IsUnique();
            b.Property(x => x.Name).HasMaxLength(100).IsRequired();
        });

        modelBuilder.Entity<ComponentCategoryEntity>(b =>
        {
            b.ToTable("ComponentCategory", "dbo");
            b.HasKey(x => x.ComponentCategoryId);
            b.HasIndex(x => x.Code).IsUnique();
            b.Property(x => x.Code).HasMaxLength(40).IsRequired();
            b.Property(x => x.DisplayName).HasMaxLength(100).IsRequired();
        });

        modelBuilder.Entity<SocketEntity>(b =>
        {
            b.ToTable("Socket", "dbo");
            b.HasKey(x => x.SocketId);
            b.HasIndex(x => x.Code).IsUnique();
            b.Property(x => x.Code).HasMaxLength(40).IsRequired();
            b.Property(x => x.DisplayName).HasMaxLength(100).IsRequired();
        });

        modelBuilder.Entity<RamStandardEntity>(b =>
        {
            b.ToTable("RamStandard", "dbo");
            b.HasKey(x => x.RamStandardId);
            b.HasIndex(x => x.Code).IsUnique();
            b.Property(x => x.Code).HasMaxLength(20).IsRequired();
            b.Property(x => x.DisplayName).HasMaxLength(50).IsRequired();
        });

        modelBuilder.Entity<FormFactorEntity>(b =>
        {
            b.ToTable("FormFactor", "dbo");
            b.HasKey(x => x.FormFactorId);
            b.HasIndex(x => x.Code).IsUnique();
            b.Property(x => x.Code).HasMaxLength(30).IsRequired();
            b.Property(x => x.DisplayName).HasMaxLength(50).IsRequired();
        });

        modelBuilder.Entity<ComponentEntity>(b =>
        {
            b.ToTable("Component", "dbo");
            b.HasKey(x => x.ComponentId);
            b.HasIndex(x => x.Sku).IsUnique();
            b.Property(x => x.Sku).HasMaxLength(60).IsRequired();
            b.Property(x => x.Name).HasMaxLength(200).IsRequired();
            b.Property(x => x.PriceVnd).HasPrecision(18, 2).IsRequired();
            b.Property(x => x.StockQty).IsRequired();
            b.Property(x => x.IsActive).IsRequired();
            b.Property(x => x.IsHot).IsRequired().HasDefaultValue(false);
            b.Property(x => x.IsBestSeller).IsRequired().HasDefaultValue(false);
            b.Property(x => x.ImageUrl).HasMaxLength(400);
            b.Property(x => x.CreatedAtUtc).IsRequired();

            b.HasOne(x => x.Category)
                .WithMany()
                .HasForeignKey(x => x.ComponentCategoryId)
                .OnDelete(DeleteBehavior.Restrict);

            b.HasOne(x => x.Brand)
                .WithMany()
                .HasForeignKey(x => x.BrandId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<CpuSpecEntity>(b =>
        {
            b.ToTable("CpuSpec", "dbo");
            b.HasKey(x => x.ComponentId);
            b.Property(x => x.Generation).HasMaxLength(30);
            b.HasOne(x => x.Component).WithOne(x => x.CpuSpec).HasForeignKey<CpuSpecEntity>(x => x.ComponentId);
            b.HasOne(x => x.Socket).WithMany().HasForeignKey(x => x.SocketId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<MainboardSpecEntity>(b =>
        {
            b.ToTable("MainboardSpec", "dbo");
            b.HasKey(x => x.ComponentId);
            b.Property(x => x.Chipset).HasMaxLength(40).IsRequired();
            b.Property(x => x.PcieSlotVersion).HasMaxLength(10);
            b.HasOne(x => x.Component).WithOne(x => x.MainboardSpec).HasForeignKey<MainboardSpecEntity>(x => x.ComponentId);
            b.HasOne(x => x.Socket).WithMany().HasForeignKey(x => x.SocketId).OnDelete(DeleteBehavior.Restrict);
            b.HasOne(x => x.RamStandard).WithMany().HasForeignKey(x => x.RamStandardId).OnDelete(DeleteBehavior.Restrict);
            b.HasOne(x => x.FormFactor).WithMany().HasForeignKey(x => x.FormFactorId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<RamSpecEntity>(b =>
        {
            b.ToTable("RamSpec", "dbo");
            b.HasKey(x => x.ComponentId);
            b.Property(x => x.ModuleCount).HasDefaultValue(1);
            b.HasOne(x => x.Component).WithOne(x => x.RamSpec).HasForeignKey<RamSpecEntity>(x => x.ComponentId);
            b.HasOne(x => x.RamStandard).WithMany().HasForeignKey(x => x.RamStandardId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<GpuSpecEntity>(b =>
        {
            b.ToTable("GpuSpec", "dbo");
            b.HasKey(x => x.ComponentId);
            b.HasOne(x => x.Component).WithOne(x => x.GpuSpec).HasForeignKey<GpuSpecEntity>(x => x.ComponentId);
        });

        modelBuilder.Entity<PsuSpecEntity>(b =>
        {
            b.ToTable("PsuSpec", "dbo");
            b.HasKey(x => x.ComponentId);
            b.Property(x => x.Efficiency).HasMaxLength(20);
            b.HasOne(x => x.Component).WithOne(x => x.PsuSpec).HasForeignKey<PsuSpecEntity>(x => x.ComponentId);
        });

        modelBuilder.Entity<BuildConfigurationEntity>(b =>
        {
            b.ToTable("BuildConfiguration", "dbo");
            b.HasKey(x => x.BuildConfigurationId);
            b.Property(x => x.Name).HasMaxLength(120).IsRequired();
            b.Property(x => x.CreatedAtUtc).IsRequired();
        });

        modelBuilder.Entity<BuildConfigurationItemEntity>(b =>
        {
            b.ToTable("BuildConfigurationItem", "dbo");
            b.HasKey(x => x.BuildConfigurationItemId);
            b.HasIndex(x => new { x.BuildConfigurationId, x.ComponentCategoryId }).IsUnique();
            b.HasOne(x => x.BuildConfiguration).WithMany(x => x.Items).HasForeignKey(x => x.BuildConfigurationId);
            b.HasOne(x => x.Component).WithMany().HasForeignKey(x => x.ComponentId).OnDelete(DeleteBehavior.Restrict);
            b.HasOne(x => x.Category).WithMany().HasForeignKey(x => x.ComponentCategoryId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<CustomerEntity>(b =>
        {
            b.ToTable("Customer", "dbo");
            b.HasKey(x => x.CustomerId);
            b.HasIndex(x => x.Username).IsUnique();
            b.HasIndex(x => x.Email).IsUnique();
            b.Property(x => x.Username).HasMaxLength(80).IsRequired();
            b.Property(x => x.Email).HasMaxLength(200).IsRequired();
            b.Property(x => x.PasswordHash).IsRequired();
            b.Property(x => x.PasswordSalt).IsRequired();
            b.Property(x => x.FullName).HasMaxLength(200);
            b.Property(x => x.Phone).HasMaxLength(30);
            b.Property(x => x.AvatarUrl).HasMaxLength(400);
            b.Property(x => x.ShippingAddress1).HasMaxLength(500);
            b.Property(x => x.ShippingAddress2).HasMaxLength(500);
            b.Property(x => x.BankAccountInfo).HasMaxLength(300);
            b.Property(x => x.LoyaltyPoints).IsRequired().HasDefaultValue(0);
            b.Property(x => x.MembershipTier).HasMaxLength(20).IsRequired().HasDefaultValue("NONE");
            b.HasIndex(x => x.Phone);
            b.HasIndex(x => x.MembershipTier);
            b.Property(x => x.PasswordResetToken).HasMaxLength(120);
            b.Property(x => x.IsActive).IsRequired();
            b.Property(x => x.CreatedAtUtc).IsRequired();
        });

        modelBuilder.Entity<CartEntity>(b =>
        {
            b.ToTable("Cart", "dbo");
            b.HasKey(x => x.CartId);
            b.Property(x => x.SessionKey).HasMaxLength(80);
            b.HasIndex(x => x.SessionKey);
            b.Property(x => x.CreatedAtUtc).IsRequired();
        });

        modelBuilder.Entity<CartItemEntity>(b =>
        {
            b.ToTable("CartItem", "dbo");
            b.HasKey(x => x.CartItemId);
            b.HasIndex(x => new { x.CartId, x.ComponentId }).IsUnique();
            b.HasOne(x => x.Cart).WithMany(x => x.Items).HasForeignKey(x => x.CartId);
            b.HasOne(x => x.Component).WithMany().HasForeignKey(x => x.ComponentId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<RoleEntity>(b =>
        {
            b.ToTable("Role", "dbo");
            b.HasKey(x => x.RoleId);
            b.HasIndex(x => x.RoleName).IsUnique();
            b.Property(x => x.RoleName).HasMaxLength(50).IsRequired();
        });

        modelBuilder.Entity<StaffEntity>(b =>
        {
            b.ToTable("Staff", "dbo");
            b.HasKey(x => x.StaffId);
            b.HasIndex(x => x.Username).IsUnique();
            b.HasIndex(x => x.Email).IsUnique();
            b.Property(x => x.Username).HasMaxLength(80).IsRequired();
            b.Property(x => x.Email).HasMaxLength(200).IsRequired();
            b.Property(x => x.PasswordHash).IsRequired();
            b.Property(x => x.PasswordSalt).IsRequired();
            b.Property(x => x.FullName).HasMaxLength(200).IsRequired();
            b.Property(x => x.Phone).HasMaxLength(30);
            b.Property(x => x.IsActive).IsRequired();
            b.Property(x => x.CreatedAtUtc).IsRequired();
            b.HasOne(x => x.Role)
                .WithMany(x => x.Staffs)
                .HasForeignKey(x => x.RoleId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<OrderEntity>(b =>
        {
            b.ToTable("Order", "dbo");
            b.HasKey(x => x.OrderId);
            b.Property(x => x.ReceiverName).HasMaxLength(200).IsRequired();
            b.Property(x => x.ReceiverPhone).HasMaxLength(30).IsRequired();
            b.Property(x => x.ReceiverEmail).HasMaxLength(200);
            b.Property(x => x.ShippingAddress).HasMaxLength(500).IsRequired();
            b.Property(x => x.Note).HasMaxLength(2000);
            b.Property(x => x.PaymentMethodCode).HasMaxLength(20).IsRequired();
            b.Property(x => x.TotalPriceVnd).HasPrecision(18, 2).IsRequired();
            b.Property(x => x.DiscountAmountVnd).HasPrecision(18, 2).IsRequired().HasDefaultValue(0m);
            b.Property(x => x.DepositAmountVnd).HasPrecision(18, 2);
            b.Property(x => x.StatusCode).HasMaxLength(40).IsRequired();
            b.Property(x => x.OrderType).HasMaxLength(20).IsRequired().HasDefaultValue("ONLINE");
            b.Property(x => x.PaymentCollectionStatus).HasMaxLength(20).IsRequired().HasDefaultValue("NONE");
            b.Property(x => x.LoyaltyPointsAwarded).IsRequired().HasDefaultValue(false);
            b.Property(x => x.TrackingNumber).HasMaxLength(80);
            b.Property(x => x.ShippingProvider).HasMaxLength(40);
            b.Property(x => x.TransactionId).HasMaxLength(100);
            b.Property(x => x.RefundTransactionId).HasMaxLength(100);
            b.Property(x => x.CreatedAtUtc).IsRequired();
            b.HasOne(x => x.CreatedByStaff)
                .WithMany()
                .HasForeignKey(x => x.CreatedByStaffId)
                .OnDelete(DeleteBehavior.Restrict);
            b.HasOne(x => x.AssignedStaff)
                .WithMany()
                .HasForeignKey(x => x.AssignedStaffId)
                .OnDelete(DeleteBehavior.Restrict);
            b.HasOne(x => x.PromoCode)
                .WithMany()
                .HasForeignKey(x => x.PromoCodeId)
                .OnDelete(DeleteBehavior.SetNull);
            b.HasIndex(x => x.OrderType);
            b.HasIndex(x => x.CreatedByStaffId);
            b.HasIndex(x => x.AssignedStaffId);
            b.HasIndex(x => x.PaymentCollectionStatus);
            b.HasIndex(x => x.StatusCode);
            b.HasIndex(x => x.TrackingNumber);
        });

        modelBuilder.Entity<OrderTimelineEntity>(b =>
        {
            b.ToTable("OrderTimeline", "dbo");
            b.HasKey(x => x.OrderTimelineId);
            b.Property(x => x.StatusCode).HasMaxLength(40).IsRequired();
            b.Property(x => x.Note).HasMaxLength(1000);
            b.Property(x => x.ChangedBy).HasMaxLength(100);
            b.Property(x => x.IssueReasonCode).HasMaxLength(40);
            b.Property(x => x.CreatedAtUtc).IsRequired();
            b.HasOne(x => x.Order)
                .WithMany(x => x.Timelines)
                .HasForeignKey(x => x.OrderId)
                .OnDelete(DeleteBehavior.Cascade);
            b.HasIndex(x => x.OrderId);
            b.HasIndex(x => x.CreatedAtUtc);
        });

        modelBuilder.Entity<OrderItemEntity>(b =>
        {
            // EF Core 7+: bảng có trigger SQL → không dùng OUTPUT clause mặc định
            b.ToTable("OrderItem", "dbo", tb =>
            {
                tb.HasTrigger("TR_OrderItem_DeductStock_POS");
            });
            b.HasKey(x => x.OrderItemId);
            b.Property(x => x.UnitPriceVnd).HasPrecision(18, 2).IsRequired();
            b.HasOne(x => x.Order).WithMany(x => x.Items).HasForeignKey(x => x.OrderId);
            b.HasOne(x => x.Component).WithMany().HasForeignKey(x => x.ComponentId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<ProductReviewEntity>(b =>
        {
            b.ToTable("ProductReviews", "dbo");
            b.HasKey(x => x.ReviewId);
            b.Property(x => x.CustomerName).HasMaxLength(200).IsRequired();
            b.Property(x => x.Comment).HasMaxLength(2000).IsRequired();
            b.Property(x => x.Rating).IsRequired();
            b.Property(x => x.IsApproved).IsRequired().HasDefaultValue(true);
            b.Property(x => x.CreatedAt).IsRequired();
            b.HasOne(x => x.Product)
                .WithMany()
                .HasForeignKey(x => x.ProductId)
                .OnDelete(DeleteBehavior.Cascade);
            b.HasIndex(x => x.ProductId);
        });

        modelBuilder.Entity<PromoCodeEntity>(b =>
        {
            b.ToTable("PromoCode", "dbo");
            b.HasKey(x => x.PromoCodeId);
            b.HasIndex(x => x.Code).IsUnique();
            b.Property(x => x.Code).HasMaxLength(40).IsRequired();
            b.Property(x => x.DiscountType).HasMaxLength(20).IsRequired();
            b.Property(x => x.DiscountValue).HasPrecision(18, 2).IsRequired();
            b.Property(x => x.MinOrderVnd).HasPrecision(18, 2);
            b.Property(x => x.UsedCount).IsRequired().HasDefaultValue(0);
            b.Property(x => x.ApplicableTier).HasMaxLength(20);
            b.Property(x => x.Description).HasMaxLength(500);
            b.Property(x => x.IsActive).IsRequired();
            b.Property(x => x.CreatedAtUtc).IsRequired();
        });

        modelBuilder.Entity<ProductQuestionEntity>(b =>
        {
            b.ToTable("ProductQuestion", "dbo");
            b.HasKey(x => x.ProductQuestionId);
            b.Property(x => x.AskerName).HasMaxLength(200).IsRequired();
            b.Property(x => x.AskerEmail).HasMaxLength(200);
            b.Property(x => x.Question).HasMaxLength(2000).IsRequired();
            b.Property(x => x.Answer).HasMaxLength(4000);
            b.Property(x => x.Status).HasMaxLength(20).IsRequired().HasDefaultValue("PENDING");
            b.Property(x => x.CreatedAtUtc).IsRequired();
            b.HasOne(x => x.Component)
                .WithMany()
                .HasForeignKey(x => x.ComponentId)
                .OnDelete(DeleteBehavior.Cascade);
            b.HasOne(x => x.Customer)
                .WithMany()
                .HasForeignKey(x => x.CustomerId)
                .OnDelete(DeleteBehavior.SetNull);
            b.HasIndex(x => x.ComponentId);
            b.HasIndex(x => x.Status);
        });

        modelBuilder.Entity<WarrantyClaimEntity>(b =>
        {
            b.ToTable("WarrantyClaim", "dbo");
            b.HasKey(x => x.WarrantyClaimId);
            b.Property(x => x.SerialNumber).HasMaxLength(100);
            b.Property(x => x.IssueDescription).HasMaxLength(2000).IsRequired();
            b.Property(x => x.Status).HasMaxLength(20).IsRequired().HasDefaultValue("PENDING");
            b.Property(x => x.AdminNote).HasMaxLength(2000);
            b.Property(x => x.CreatedAtUtc).IsRequired();
            b.HasOne(x => x.Order)
                .WithMany()
                .HasForeignKey(x => x.OrderId)
                .OnDelete(DeleteBehavior.Restrict);
            b.HasOne(x => x.Component)
                .WithMany()
                .HasForeignKey(x => x.ComponentId)
                .OnDelete(DeleteBehavior.Restrict);
            b.HasOne(x => x.Customer)
                .WithMany()
                .HasForeignKey(x => x.CustomerId)
                .OnDelete(DeleteBehavior.SetNull);
            b.HasIndex(x => x.OrderId);
            b.HasIndex(x => x.Status);
        });

        modelBuilder.Entity<StockWaitlistEntity>(b =>
        {
            b.ToTable("StockWaitlist", "dbo");
            b.HasKey(x => x.StockWaitlistId);
            b.HasIndex(x => new { x.ComponentId, x.CustomerId }).IsUnique();
            b.Property(x => x.CreatedAtUtc).IsRequired();
        });
    }
}

