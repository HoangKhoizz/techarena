using ban_link_kien_PC.Domain.Cskh;
using ban_link_kien_PC.Infrastructure.Persistence;
using ban_link_kien_PC.Infrastructure.Persistence.Entities;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

namespace ban_link_kien_PC.Controllers.Api;

[ApiController]
[Route("api/reviews")]
public sealed class ReviewsController : ControllerBase
{
    private readonly PcStoreDbContext _db;
    private readonly ReviewEligibilityService _eligibility;

    public ReviewsController(PcStoreDbContext db, ReviewEligibilityService eligibility)
    {
        _db = db;
        _eligibility = eligibility;
    }

    [HttpGet]
    public async Task<IActionResult> List([FromQuery] int productId, CancellationToken ct)
    {
        var items = await _db.ProductReviews.AsNoTracking()
            .Where(x => x.ProductId == productId && x.IsApproved)
            .OrderByDescending(x => x.CreatedAt)
            .Select(x => new
            {
                x.ReviewId,
                x.ProductId,
                x.CustomerName,
                x.Rating,
                x.Comment,
                x.CreatedAt
            })
            .ToListAsync(ct);

        var avg = items.Count == 0 ? 0 : items.Average(x => x.Rating);
        var elig = await _eligibility.CheckAsync(productId, TryGetCustomerId(), ct);

        return Ok(new
        {
            average = Math.Round(avg, 1),
            count = items.Count,
            items,
            canReview = elig.CanReview,
            reviewBlockedReason = elig.Reason,
            isAuthenticated = elig.IsAuthenticated
        });
    }

    [HttpGet("eligibility")]
    public async Task<IActionResult> Eligibility([FromQuery] int productId, CancellationToken ct)
    {
        if (productId <= 0)
            return BadRequest(new { error = "ProductId không hợp lệ." });

        var elig = await _eligibility.CheckAsync(productId, TryGetCustomerId(), ct);
        return Ok(new
        {
            canReview = elig.CanReview,
            reason = elig.Reason,
            isAuthenticated = elig.IsAuthenticated,
            hasPurchasedAndReceived = elig.HasPurchasedAndReceived,
            alreadyReviewed = elig.AlreadyReviewed
        });
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateReviewRequest req, CancellationToken ct)
    {
        if (req.ProductId <= 0)
            return BadRequest(new { error = "ProductId không hợp lệ." });
        if (req.Rating < 1 || req.Rating > 5)
            return BadRequest(new { error = "Đánh giá phải từ 1 đến 5 sao." });
        if (string.IsNullOrWhiteSpace(req.Comment) || req.Comment.Trim().Length < 3)
            return BadRequest(new { error = "Vui lòng nhập nhận xét (tối thiểu 3 ký tự)." });

        var customerId = TryGetCustomerId();
        var elig = await _eligibility.CheckAsync(req.ProductId, customerId, ct);
        if (!elig.CanReview)
            return StatusCode(StatusCodes.Status403Forbidden, new { error = elig.Reason ?? "Không được phép đánh giá." });

        var productExists = await _db.Components.AnyAsync(x => x.ComponentId == req.ProductId, ct);
        if (!productExists)
            return NotFound(new { error = "Không tìm thấy sản phẩm." });

        var name = string.IsNullOrWhiteSpace(req.CustomerName) ? "Khách hàng" : req.CustomerName.Trim();
        var customer = await _db.Customers.AsNoTracking()
            .Where(x => x.CustomerId == customerId!.Value)
            .Select(x => new { x.FullName, x.Username })
            .FirstOrDefaultAsync(ct);
        if (customer is not null && string.IsNullOrWhiteSpace(req.CustomerName))
            name = string.IsNullOrWhiteSpace(customer.FullName) ? customer.Username : customer.FullName!;

        var entity = new ProductReviewEntity
        {
            ProductId = req.ProductId,
            CustomerName = name,
            CustomerId = customerId,
            Rating = req.Rating,
            Comment = req.Comment.Trim(),
            IsApproved = false,
            CreatedAt = DateTime.UtcNow
        };

        _db.ProductReviews.Add(entity);
        await _db.SaveChangesAsync(ct);

        return Ok(new
        {
            entity.ReviewId,
            entity.ProductId,
            entity.CustomerName,
            entity.Rating,
            entity.Comment,
            entity.CreatedAt,
            pendingApproval = true,
            message = "Đã gửi đánh giá. Sẽ hiển thị sau khi admin duyệt."
        });
    }

    private int? TryGetCustomerId()
    {
        if (User.Identity?.IsAuthenticated != true) return null;
        return int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var cid) ? cid : null;
    }
}

public sealed record CreateReviewRequest(
    int ProductId,
    string? CustomerName,
    int Rating,
    string Comment);
