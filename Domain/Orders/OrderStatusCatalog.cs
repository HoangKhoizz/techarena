namespace ban_link_kien_PC.Domain.Orders;

public static class OrderStatusCatalog
{
    public const string PendingConfirmation = "PENDING_CONFIRMATION";
    public const string Pending = "PENDING";
    public const string Processing = "PROCESSING";
    /// <summary>Đã duyệt — chờ phân công giao / đóng gói.</summary>
    public const string Approved = "APPROVED";
    /// <summary>Đã đóng gói — chờ tạo vận đơn (UC11).</summary>
    public const string Packed = "PACKED";
    /// <summary>Đã xuất kho linh kiện cho kỹ thuật lắp ráp.</summary>
    public const string Exported = "EXPORTED";
    /// <summary>Đang lắp ráp &amp; test hiệu năng.</summary>
    public const string Assembling = "ASSEMBLING";
    /// <summary>Sẵn sàng giao (sau lắp ráp).</summary>
    public const string ReadyToDeliver = "READY_TO_DELIVER";
    /// <summary>Tạm ngưng — linh kiện lỗi DOA.</summary>
    public const string IssueDoa = "ISSUE_DOA";
    /// <summary>Tạm ngưng — không vừa vỏ Case.</summary>
    public const string IssueCaseMismatch = "ISSUE_CASE_MISMATCH";
    public const string Shipping = "SHIPPING";
    public const string WaitingPayment = "WAITING_PAYMENT";
    public const string Paid = "PAID";
    public const string PaidCompleted = "PAID_COMPLETED";
    /// <summary>Giao thành công — đã thu tiền.</summary>
    public const string DeliveredCollected = "DELIVERED_COLLECTED";
    /// <summary>Giao thành công — chưa thu tiền (cần ký quỹ / công nợ).</summary>
    public const string DeliveredUnpaid = "DELIVERED_UNPAID";
    /// <summary>Giao không thành công.</summary>
    public const string DeliveryFailed = "DELIVERY_FAILED";
    public const string Cancelled = "CANCELLED";
    public const string CancelledNoStock = "CANCELLED_NO_STOCK";
    public const string Refunded = "REFUNDED";

    public static string Normalize(string? code)
    {
        var normalized = (code ?? string.Empty).Trim().ToUpperInvariant();
        return normalized switch
        {
            "WAITING_PARTS" => PendingConfirmation,
            "PENDING" => PendingConfirmation,
            "PROCESSING" => PendingConfirmation,
            "SHIPPED" => Shipping,
            "PAID" => Paid,
            "COMPLETED" => PaidCompleted,
            "DELIVERED" => DeliveredCollected,
            "REFUNDED" => Refunded,
            "EXPORTED" => Exported,
            "ASSEMBLING" => Assembling,
            "READYTODELIVER" or "READY_TO_DELIVER" => ReadyToDeliver,
            "ISSUE_DOA" or "DOA" => IssueDoa,
            "ISSUE_CASEMISMATCH" or "ISSUE_CASE_MISMATCH" or "CASE_MISMATCH" => IssueCaseMismatch,
            _ => normalized
        };
    }

    public static string ToDisplayName(string? code)
    {
        return Normalize(code) switch
        {
            PendingConfirmation => "Chờ xác nhận",
            Approved => "Đã duyệt",
            Packed => "Đã đóng gói",
            Exported => "Đã xuất kho",
            Assembling => "Đang lắp ráp",
            ReadyToDeliver => "Sẵn sàng giao",
            IssueDoa => "Lỗi linh kiện DOA",
            IssueCaseMismatch => "Không vừa vỏ Case",
            Shipping => "Đang giao hàng",
            WaitingPayment => "Chờ thanh toán",
            Paid => "Đã thanh toán",
            PaidCompleted => "Đã thanh toán - Hoàn thành đơn",
            DeliveredCollected => "Giao thành công — đã thu tiền",
            DeliveredUnpaid => "Giao thành công — chưa thu tiền",
            DeliveryFailed => "Giao không thành công",
            Cancelled => "Đã hủy",
            CancelledNoStock => "Hủy đơn (lỗi tồn kho)",
            Refunded => "Đã hoàn tiền",
            _ => "Không xác định"
        };
    }

    public static string ToBadgeClass(string? code)
    {
        return Normalize(code) switch
        {
            PendingConfirmation => "text-bg-warning",
            Approved => "text-bg-primary",
            Packed => "text-bg-secondary",
            Exported => "text-bg-info",
            Assembling => "text-bg-primary",
            ReadyToDeliver => "text-bg-success",
            IssueDoa => "text-bg-danger",
            IssueCaseMismatch => "text-bg-danger",
            Shipping => "text-bg-info",
            WaitingPayment => "text-bg-secondary",
            Paid => "text-bg-success",
            PaidCompleted => "text-bg-success",
            DeliveredCollected => "text-bg-success",
            DeliveredUnpaid => "text-bg-warning",
            DeliveryFailed => "text-bg-danger",
            Cancelled => "text-bg-danger",
            CancelledNoStock => "text-bg-danger",
            Refunded => "text-bg-primary",
            _ => "text-bg-light"
        };
    }

    /// <summary>
    /// Cho phép hủy khi chưa giao / chưa lắp xong giai sâu.
    /// </summary>
    public static bool CanCancel(string? code)
    {
        var n = Normalize(code);
        return n is PendingConfirmation or Approved or Packed or Exported
            or WaitingPayment or Paid
            or Pending or Processing or "WAITING_PARTS";
    }

    public static bool IsAssemblyIssue(string? code)
    {
        var n = Normalize(code);
        return n is IssueDoa or IssueCaseMismatch;
    }

    public static bool IsInAssemblyPipeline(string? code)
    {
        var n = Normalize(code);
        return n is Exported or Assembling or ReadyToDeliver or IssueDoa or IssueCaseMismatch;
    }

    public static bool IsShippedOrBeyond(string? code)
    {
        var n = Normalize(code);
        return n is Shipping or PaidCompleted
            or DeliveredCollected or DeliveredUnpaid or DeliveryFailed;
    }

    public static bool IsDeliveredSuccess(string? code)
    {
        var n = Normalize(code);
        return n is DeliveredCollected or DeliveredUnpaid or PaidCompleted;
    }

    public static bool IsCancelled(string? code)
    {
        var n = Normalize(code);
        return n is Cancelled or CancelledNoStock or Refunded;
    }

    /// <summary>Đơn công nợ: giao thành công nhưng chưa thu tiền.</summary>
    public static bool IsDebtOutstanding(string? statusCode, string? paymentCollectionStatus)
    {
        var status = Normalize(statusCode);
        var collect = PaymentCollectionCatalog.Normalize(paymentCollectionStatus);
        if (status is DeliveredUnpaid)
            return true;
        return status is DeliveredCollected or PaidCompleted
            && collect is PaymentCollectionCatalog.Uncollected;
    }

    /// <summary>Đơn đã PAID qua MoMo/VNPay → hủy phải Refund.</summary>
    public static bool NeedsOnlineRefund(string? statusCode, string? paymentMethodCode)
    {
        var status = Normalize(statusCode);
        var pm = (paymentMethodCode ?? "").Trim().ToUpperInvariant();
        if (status is not Paid)
            return false;
        return pm is "MOMO" or "VNPAY";
    }
}
