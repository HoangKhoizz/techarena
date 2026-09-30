namespace ban_link_kien_PC.Infrastructure.Persistence.Entities;

public sealed class BrandEntity
{
    public int BrandId { get; set; }
    public string Name { get; set; } = "";
}

public sealed class ComponentCategoryEntity
{
    public int ComponentCategoryId { get; set; }
    public string Code { get; set; } = "";
    public string DisplayName { get; set; } = "";
}

public sealed class SocketEntity
{
    public int SocketId { get; set; }
    public string Code { get; set; } = "";
    public string DisplayName { get; set; } = "";
}

public sealed class RamStandardEntity
{
    public int RamStandardId { get; set; }
    public string Code { get; set; } = "";
    public string DisplayName { get; set; } = "";
}

public sealed class FormFactorEntity
{
    public int FormFactorId { get; set; }
    public string Code { get; set; } = "";
    public string DisplayName { get; set; } = "";
}

public sealed class ComponentEntity
{
    public int ComponentId { get; set; }
    public int ComponentCategoryId { get; set; }
    public int? BrandId { get; set; }
    public string Sku { get; set; } = "";
    public string Name { get; set; } = "";
    public decimal PriceVnd { get; set; }
    public int StockQty { get; set; }
    public bool IsActive { get; set; } = true;
    /// <summary>Hàng hot — Qty=0 vẫn hiện trên trang chủ với chú thích "Cháy hàng".</summary>
    public bool IsHot { get; set; }
    /// <summary>Hàng bán chạy — hiện khu vực Best Seller.</summary>
    public bool IsBestSeller { get; set; }
    /// <summary>Đường dẫn ảnh upload (vd. /images/products/xxx.jpg). Null → dùng resolver mặc định.</summary>
    public string? ImageUrl { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    public ComponentCategoryEntity? Category { get; set; }
    public BrandEntity? Brand { get; set; }

    public CpuSpecEntity? CpuSpec { get; set; }
    public MainboardSpecEntity? MainboardSpec { get; set; }
    public RamSpecEntity? RamSpec { get; set; }
    public GpuSpecEntity? GpuSpec { get; set; }
    public PsuSpecEntity? PsuSpec { get; set; }
}

public sealed class CpuSpecEntity
{
    public int ComponentId { get; set; }
    public int SocketId { get; set; }
    public string? Generation { get; set; }
    public int TdpWatt { get; set; }

    public ComponentEntity? Component { get; set; }
    public SocketEntity? Socket { get; set; }
}

public sealed class MainboardSpecEntity
{
    public int ComponentId { get; set; }
    public int SocketId { get; set; }
    public string Chipset { get; set; } = "";
    public int RamStandardId { get; set; }
    public int FormFactorId { get; set; }
    public string? PcieSlotVersion { get; set; }

    public ComponentEntity? Component { get; set; }
    public SocketEntity? Socket { get; set; }
    public RamStandardEntity? RamStandard { get; set; }
    public FormFactorEntity? FormFactor { get; set; }
}

public sealed class RamSpecEntity
{
    public int ComponentId { get; set; }
    public int RamStandardId { get; set; }
    public int CapacityGb { get; set; }
    public int? SpeedMhz { get; set; }
    /// <summary>1 = single stick; 2+ = multi-stick kit sold as one SKU.</summary>
    public int ModuleCount { get; set; } = 1;

    public ComponentEntity? Component { get; set; }
    public RamStandardEntity? RamStandard { get; set; }
}

public sealed class GpuSpecEntity
{
    public int ComponentId { get; set; }
    public int TdpWatt { get; set; }

    public ComponentEntity? Component { get; set; }
}

public sealed class PsuSpecEntity
{
    public int ComponentId { get; set; }
    public int CapacityWatt { get; set; }
    public string? Efficiency { get; set; }

    public ComponentEntity? Component { get; set; }
}

public sealed class BuildConfigurationEntity
{
    public int BuildConfigurationId { get; set; }
    public string Name { get; set; } = "";
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    public List<BuildConfigurationItemEntity> Items { get; set; } = [];
}

public sealed class BuildConfigurationItemEntity
{
    public int BuildConfigurationItemId { get; set; }
    public int BuildConfigurationId { get; set; }
    public int ComponentCategoryId { get; set; }
    public int ComponentId { get; set; }
    public int Qty { get; set; }

    public BuildConfigurationEntity? BuildConfiguration { get; set; }
    public ComponentCategoryEntity? Category { get; set; }
    public ComponentEntity? Component { get; set; }
}

public sealed class CustomerEntity
{
    public int CustomerId { get; set; }
    public string Username { get; set; } = "";
    public string Email { get; set; } = "";
    public byte[] PasswordHash { get; set; } = Array.Empty<byte>();
    public byte[] PasswordSalt { get; set; } = Array.Empty<byte>();
    public string? PasswordResetToken { get; set; }
    public DateTime? PasswordResetExpiresAtUtc { get; set; }
    public string? FullName { get; set; }
    public string? Phone { get; set; }
    public string? AvatarUrl { get; set; }
    public DateTime? DateOfBirth { get; set; }
    public string? ShippingAddress1 { get; set; }
    public string? ShippingAddress2 { get; set; }
    public string? BankAccountInfo { get; set; }
    /// <summary>Điểm tích lũy — dùng phân hạng Bạc/Vàng/Bạch kim.</summary>
    public int LoyaltyPoints { get; set; }
    /// <summary>NONE | SILVER | GOLD | PLATINUM</summary>
    public string MembershipTier { get; set; } = "NONE";
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}

public sealed class ProductReviewEntity
{
    public int ReviewId { get; set; }
    public int ProductId { get; set; }
    public string CustomerName { get; set; } = "";
    public int? CustomerId { get; set; }
    public int Rating { get; set; }
    public string Comment { get; set; } = "";
    /// <summary>Admin duyệt trước khi hiển thị công khai (Phase 4).</summary>
    public bool IsApproved { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public ComponentEntity? Product { get; set; }
}

/// <summary>Mã khuyến mãi (Promo Code).</summary>
public sealed class PromoCodeEntity
{
    public int PromoCodeId { get; set; }
    public string Code { get; set; } = "";
    /// <summary>PERCENT | FIXED</summary>
    public string DiscountType { get; set; } = "PERCENT";
    public decimal DiscountValue { get; set; }
    public decimal? MinOrderVnd { get; set; }
    public int? MaxUses { get; set; }
    public int UsedCount { get; set; }
    public DateTime? StartsAtUtc { get; set; }
    public DateTime? ExpiresAtUtc { get; set; }
    /// <summary>NULL = mọi hạng; hoặc SILVER/GOLD/PLATINUM.</summary>
    public string? ApplicableTier { get; set; }
    public bool IsActive { get; set; } = true;
    public string? Description { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}

/// <summary>Hỏi đáp sản phẩm (CSKH).</summary>
public sealed class ProductQuestionEntity
{
    public int ProductQuestionId { get; set; }
    public int ComponentId { get; set; }
    public int? CustomerId { get; set; }
    public string AskerName { get; set; } = "";
    public string? AskerEmail { get; set; }
    public string Question { get; set; } = "";
    public string? Answer { get; set; }
    /// <summary>PENDING | ANSWERED | HIDDEN</summary>
    public string Status { get; set; } = "PENDING";
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime? AnsweredAtUtc { get; set; }

    public ComponentEntity? Component { get; set; }
    public CustomerEntity? Customer { get; set; }
}

/// <summary>Yêu cầu bảo hành sản phẩm.</summary>
public sealed class WarrantyClaimEntity
{
    public int WarrantyClaimId { get; set; }
    public int OrderId { get; set; }
    public int ComponentId { get; set; }
    public int? CustomerId { get; set; }
    public string? SerialNumber { get; set; }
    public string IssueDescription { get; set; } = "";
    /// <summary>PENDING | APPROVED | REJECTED | COMPLETED</summary>
    public string Status { get; set; } = "PENDING";
    public string? AdminNote { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime? ResolvedAtUtc { get; set; }

    public OrderEntity? Order { get; set; }
    public ComponentEntity? Component { get; set; }
    public CustomerEntity? Customer { get; set; }
}

public sealed class CartEntity
{
    public int CartId { get; set; }
    public int? CustomerId { get; set; }
    public string? SessionKey { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    public List<CartItemEntity> Items { get; set; } = [];
}

public sealed class CartItemEntity
{
    public int CartItemId { get; set; }
    public int CartId { get; set; }
    public int ComponentId { get; set; }
    public int Qty { get; set; }

    public CartEntity? Cart { get; set; }
    public ComponentEntity? Component { get; set; }
}

public sealed class RoleEntity
{
    public int RoleId { get; set; }
    public string RoleName { get; set; } = "";

    public List<StaffEntity> Staffs { get; set; } = [];
}

public sealed class StaffEntity
{
    public int StaffId { get; set; }
    public int RoleId { get; set; }
    public string Username { get; set; } = "";
    public string Email { get; set; } = "";
    public byte[] PasswordHash { get; set; } = Array.Empty<byte>();
    public byte[] PasswordSalt { get; set; } = Array.Empty<byte>();
    public string FullName { get; set; } = "";
    public string? Phone { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    public RoleEntity? Role { get; set; }
}

public sealed class OrderEntity
{
    public int OrderId { get; set; }
    public int? CustomerId { get; set; }
    public int? CartId { get; set; }
    public string ReceiverName { get; set; } = "";
    public string ReceiverPhone { get; set; } = "";
    public string? ReceiverEmail { get; set; }
    public string ShippingAddress { get; set; } = "";
    public string? Note { get; set; }
    public string PaymentMethodCode { get; set; } = "COD";
    public decimal TotalPriceVnd { get; set; }
    public string StatusCode { get; set; } = "WAITING_PARTS";
    /// <summary>ONLINE | POS</summary>
    public string OrderType { get; set; } = "ONLINE";
    public int? CreatedByStaffId { get; set; }
    /// <summary>Nhân viên được phân công giao hàng.</summary>
    public int? AssignedStaffId { get; set; }
    /// <summary>Mã vận đơn (GHTK / Viettel / Express…).</summary>
    public string? TrackingNumber { get; set; }
    /// <summary>GHTK | VIETTEL_POST | EXPRESS_STORE</summary>
    public string? ShippingProvider { get; set; }
    public DateTime? ShippedAtUtc { get; set; }
    public int? PromoCodeId { get; set; }
    public decimal DiscountAmountVnd { get; set; }
    /// <summary>NONE | COLLECTED | UNCOLLECTED | DEPOSIT — thu tiền khi giao.</summary>
    public string PaymentCollectionStatus { get; set; } = "NONE";
    public decimal? DepositAmountVnd { get; set; }
    public DateTime? DeliveredAtUtc { get; set; }
    public DateTime? DebtCollectedAtUtc { get; set; }
    /// <summary>Đã cộng điểm loyalty cho đơn này chưa (tránh cộng trùng).</summary>
    public bool LoyaltyPointsAwarded { get; set; }
    public string? TransactionId { get; set; }
    public string? RefundTransactionId { get; set; }
    public DateTime? RefundedAtUtc { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    public StaffEntity? CreatedByStaff { get; set; }
    public StaffEntity? AssignedStaff { get; set; }
    public PromoCodeEntity? PromoCode { get; set; }
    public List<OrderItemEntity> Items { get; set; } = [];
    public List<OrderTimelineEntity> Timelines { get; set; } = [];
}

/// <summary>Lịch sử thay đổi trạng thái đơn (tiến độ lắp ráp / giao hàng).</summary>
public sealed class OrderTimelineEntity
{
    public int OrderTimelineId { get; set; }
    public int OrderId { get; set; }
    public string StatusCode { get; set; } = "";
    public string? Note { get; set; }
    public string? ChangedBy { get; set; }
    /// <summary>DOA | CASE_MISMATCH (khi status là ISSUE_*).</summary>
    public string? IssueReasonCode { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    public OrderEntity? Order { get; set; }
}

public sealed class OrderItemEntity
{
    public int OrderItemId { get; set; }
    public int OrderId { get; set; }
    public int ComponentId { get; set; }
    public decimal UnitPriceVnd { get; set; }
    public int Qty { get; set; }

    public OrderEntity? Order { get; set; }
    public ComponentEntity? Component { get; set; }
}

public sealed class StockWaitlistEntity
{
    public int StockWaitlistId { get; set; }
    public int ComponentId { get; set; }
    public int CustomerId { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}

