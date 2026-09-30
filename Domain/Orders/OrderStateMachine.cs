namespace ban_link_kien_PC.Domain.Orders;

// State pattern: manage assembly order lifecycle.
public interface IOrderState
{
    string Code { get; }
    IOrderState Next();
}

public sealed class WaitingPartsState : IOrderState
{
    public string Code => OrderStatusCatalog.PendingConfirmation;
    public IOrderState Next() => new ShippingState();
}

public sealed class ShippingState : IOrderState
{
    public string Code => OrderStatusCatalog.Shipping;
    public IOrderState Next() => new WaitingPaymentState();
}

public sealed class WaitingPaymentState : IOrderState
{
    public string Code => OrderStatusCatalog.WaitingPayment;
    public IOrderState Next() => new PaidCompletedState();
}

public sealed class PaidCompletedState : IOrderState
{
    public string Code => OrderStatusCatalog.PaidCompleted;
    public IOrderState Next() => this;
}

public sealed class CancelledNoStockState : IOrderState
{
    public string Code => OrderStatusCatalog.CancelledNoStock;
    public IOrderState Next() => this;
}

public interface IOrderStateMachine
{
    string Advance(string currentStatusCode);
}

public sealed class OrderStateMachine : IOrderStateMachine
{
    public string Advance(string currentStatusCode)
    {
        var current = currentStatusCode?.Trim().ToUpperInvariant() switch
        {
            "WAITING_PARTS" => (IOrderState)new WaitingPartsState(),
            "PENDING_CONFIRMATION" => new WaitingPartsState(),
            "SHIPPING" => new ShippingState(),
            "WAITING_PAYMENT" => new WaitingPaymentState(),
            "PAID_COMPLETED" => new PaidCompletedState(),
            "CANCELLED_NO_STOCK" => new CancelledNoStockState(),
            _ => new WaitingPartsState()
        };

        return current.Next().Code;
    }
}

