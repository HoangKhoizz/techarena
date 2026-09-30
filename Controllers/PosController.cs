using Microsoft.AspNetCore.Mvc;

namespace ban_link_kien_PC.Controllers;

public sealed class PosController : Controller
{
    /// <summary>Màn POS — client redirect về Login nếu chưa mở ca.</summary>
    [HttpGet]
    public IActionResult Index()
    {
        ViewData["Title"] = "POS Quầy bán hàng";
        return View();
    }

    /// <summary>Đăng nhập ca (Shift Login) — trang riêng, không popup.</summary>
    [HttpGet]
    public IActionResult Login()
    {
        ViewData["Title"] = "Đăng nhập ca POS";
        return View();
    }
}
