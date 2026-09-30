using ban_link_kien_PC.Domain.Orders;
using ban_link_kien_PC.Infrastructure;
using Microsoft.AspNetCore.Mvc;

namespace ban_link_kien_PC.ViewComponents;

public sealed class CartSummaryViewComponent : ViewComponent
{
    private readonly CartManager _cart;

    public CartSummaryViewComponent(CartManager cart) => _cart = cart;

    public IViewComponentResult Invoke()
    {
        var cartKey = CartKeyResolver.GetExistingCartKey(HttpContext);
        var count = 0;
        if (!string.IsNullOrEmpty(cartKey))
        {
            var cart = _cart.GetCart(cartKey);
            count = cart.Values.Sum();
        }

        return View(count);
    }
}
