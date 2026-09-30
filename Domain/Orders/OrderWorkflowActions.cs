namespace ban_link_kien_PC.Domain.Orders;

/// <summary>Hành động nghiệp vụ hợp lệ trên đơn (Sales/Kho) — không cho nhảy cóc qua select tự do.</summary>
public static class OrderWorkflowActions
{
    public const string Approve = "APPROVE";
    public const string Cancel = "CANCEL";
    public const string Pack = "PACK";
    public const string ExportToTech = "EXPORT_TO_TECH";
    public const string DeliverSuccess = "DELIVER_SUCCESS";
    public const string DeliverFailed = "DELIVER_FAILED";
    public const string DeliverUnpaid = "DELIVER_UNPAID";
    public const string CollectDebt = "COLLECT_DEBT";

    public static bool CanApply(string? currentStatus, string action, string? paymentCollectionStatus = null)
    {
        var status = OrderStatusCatalog.Normalize(currentStatus);
        var act = (action ?? "").Trim().ToUpperInvariant();

        return act switch
        {
            Approve => status is OrderStatusCatalog.PendingConfirmation or OrderStatusCatalog.WaitingPayment,
            Cancel => OrderStatusCatalog.CanCancel(status),
            Pack => status == OrderStatusCatalog.Approved,
            ExportToTech => status is OrderStatusCatalog.Approved or OrderStatusCatalog.Packed
                or OrderStatusCatalog.IssueDoa or OrderStatusCatalog.IssueCaseMismatch,
            DeliverSuccess or DeliverFailed or DeliverUnpaid => status == OrderStatusCatalog.Shipping,
            CollectDebt => OrderStatusCatalog.IsDebtOutstanding(status, paymentCollectionStatus),
            _ => false
        };
    }

    /// <summary>Các bước tiếp theo hợp lệ (hiển thị UI Sales).</summary>
    public static IReadOnlyList<string> AllowedActionsForSales(string? status, string? paymentCollectionStatus = null)
    {
        var list = new List<string>();
        void Add(string a)
        {
            if (CanApply(status, a, paymentCollectionStatus))
                list.Add(a);
        }

        Add(Approve);
        Add(Cancel);
        Add(Pack);
        Add(ExportToTech);
        Add(DeliverSuccess);
        Add(DeliverFailed);
        Add(DeliverUnpaid);
        Add(CollectDebt);
        return list;
    }
}
