using ban_link_kien_PC.Domain.Cskh;
using ban_link_kien_PC.Infrastructure.Persistence;
using ban_link_kien_PC.Infrastructure.Persistence.Entities;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

namespace ban_link_kien_PC.Controllers.Api;

[ApiController]
[Route("api/questions")]
public sealed class ProductQuestionsController : ControllerBase
{
    private readonly PcStoreDbContext _db;
    private readonly QuestionRateLimiter _rateLimiter;

    public ProductQuestionsController(PcStoreDbContext db, QuestionRateLimiter rateLimiter)
    {
        _db = db;
        _rateLimiter = rateLimiter;
    }

    [HttpGet]
    public async Task<IActionResult> List([FromQuery] int productId, CancellationToken ct)
    {
        if (productId <= 0)
            return BadRequest(new { error = "ProductId không hợp lệ." });

        int? customerId = null;
        if (User.Identity?.IsAuthenticated == true &&
            int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var cid))
            customerId = cid;

        var items = await _db.ProductQuestions.AsNoTracking()
            .Where(x => x.ComponentId == productId &&
                        (x.Status == ProductQuestionStatus.Answered ||
                         (customerId != null && x.CustomerId == customerId && x.Status == ProductQuestionStatus.Pending)))
            .OrderByDescending(x => x.CreatedAtUtc)
            .Select(x => new
            {
                x.ProductQuestionId,
                x.AskerName,
                x.Question,
                x.Answer,
                x.Status,
                x.CreatedAtUtc,
                x.AnsweredAtUtc
            })
            .ToListAsync(ct);

        return Ok(new { count = items.Count, items });
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateQuestionRequest req, CancellationToken ct)
    {
        if (req.ProductId <= 0)
            return BadRequest(new { error = "ProductId không hợp lệ." });
        if (string.IsNullOrWhiteSpace(req.Question) || req.Question.Trim().Length < 5)
            return BadRequest(new { error = "Câu hỏi tối thiểu 5 ký tự." });

        var rateKey = BuildRateKey();
        if (!_rateLimiter.TryAcquire(rateKey, out var retryAfter))
        {
            return StatusCode(StatusCodes.Status429TooManyRequests, new
            {
                error = $"Bạn gửi câu hỏi quá nhanh. Vui lòng đợi khoảng {retryAfter} giây rồi thử lại.",
                retryAfterSeconds = retryAfter
            });
        }

        var exists = await _db.Components.AnyAsync(x => x.ComponentId == req.ProductId && x.IsActive, ct);
        if (!exists)
            return NotFound(new { error = "Không tìm thấy sản phẩm." });

        int? customerId = null;
        var name = string.IsNullOrWhiteSpace(req.AskerName) ? "Khách hàng" : req.AskerName.Trim();
        string? email = string.IsNullOrWhiteSpace(req.AskerEmail) ? null : req.AskerEmail.Trim();

        if (User.Identity?.IsAuthenticated == true &&
            int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var cid))
        {
            customerId = cid;
            var customer = await _db.Customers.AsNoTracking()
                .Where(x => x.CustomerId == cid)
                .Select(x => new { x.FullName, x.Username, x.Email })
                .FirstOrDefaultAsync(ct);
            if (customer is not null)
            {
                if (string.IsNullOrWhiteSpace(req.AskerName))
                    name = string.IsNullOrWhiteSpace(customer.FullName) ? customer.Username : customer.FullName!;
                email ??= customer.Email;
            }
        }

        var entity = new ProductQuestionEntity
        {
            ComponentId = req.ProductId,
            CustomerId = customerId,
            AskerName = name,
            AskerEmail = email,
            Question = req.Question.Trim(),
            Status = ProductQuestionStatus.Pending,
            CreatedAtUtc = DateTime.UtcNow
        };

        _db.ProductQuestions.Add(entity);
        await _db.SaveChangesAsync(ct);

        return Ok(new
        {
            entity.ProductQuestionId,
            entity.AskerName,
            entity.Question,
            entity.Status,
            entity.CreatedAtUtc,
            message = "Đã gửi câu hỏi. Admin sẽ trả lời sớm."
        });
    }

    private string BuildRateKey()
    {
        if (User.Identity?.IsAuthenticated == true &&
            int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var cid))
            return $"user:{cid}";

        var ip = HttpContext.Connection.RemoteIpAddress?.ToString();
        if (string.IsNullOrWhiteSpace(ip))
            ip = "unknown";
        return $"ip:{ip}";
    }
}

public sealed record CreateQuestionRequest(
    int ProductId,
    string? AskerName,
    string? AskerEmail,
    string Question);
