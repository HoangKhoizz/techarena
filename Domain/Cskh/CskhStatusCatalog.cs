namespace ban_link_kien_PC.Domain.Cskh;

public static class ProductQuestionStatus
{
    public const string Pending = "PENDING";
    public const string Answered = "ANSWERED";
    public const string Hidden = "HIDDEN";

    public static string ToDisplayName(string? status) => (status ?? "").Trim().ToUpperInvariant() switch
    {
        Answered => "Đã trả lời",
        Hidden => "Ẩn",
        _ => "Chờ trả lời"
    };
}

public static class WarrantyClaimStatus
{
    public const string Pending = "PENDING";
    public const string Approved = "APPROVED";
    public const string Rejected = "REJECTED";
    public const string Completed = "COMPLETED";

    public static string ToDisplayName(string? status) => (status ?? "").Trim().ToUpperInvariant() switch
    {
        Approved => "Đã duyệt",
        Rejected => "Từ chối",
        Completed => "Hoàn tất",
        _ => "Chờ xử lý"
    };

    public static string ToBadgeClass(string? status) => (status ?? "").Trim().ToUpperInvariant() switch
    {
        Approved => "text-bg-primary",
        Rejected => "text-bg-danger",
        Completed => "text-bg-success",
        _ => "text-bg-warning"
    };
}
