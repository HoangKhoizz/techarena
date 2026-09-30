using ban_link_kien_PC.Domain.Auth;
using ban_link_kien_PC.Domain.Cskh;
using ban_link_kien_PC.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ban_link_kien_PC.Controllers;

/// <summary>Admin CSKH: hỏi đáp · đánh giá · bảo hành.</summary>
[Authorize]
public sealed class CskhController : Controller
{
    private readonly PcStoreDbContext _db;

    public CskhController(PcStoreDbContext db) => _db = db;

    // ─── Questions ─────────────────────────────────────────

    [HttpGet]
    public async Task<IActionResult> Questions(string? status, CancellationToken ct)
    {
        var guard = EnsureAdmin();
        if (guard is not null) return guard;

        ViewData["Title"] = "Hỏi đáp sản phẩm";
        var filter = (status ?? "PENDING").Trim().ToUpperInvariant();
        if (filter is not ("PENDING" or "ANSWERED" or "HIDDEN" or "ALL"))
            filter = "PENDING";

        var q = _db.ProductQuestions.AsNoTracking().AsQueryable();
        if (filter != "ALL")
            q = q.Where(x => x.Status == filter);

        var rows = await (
            from pq in q
            join c in _db.Components.AsNoTracking() on pq.ComponentId equals c.ComponentId
            orderby pq.CreatedAtUtc descending
            select new CskhQuestionRow
            {
                ProductQuestionId = pq.ProductQuestionId,
                ComponentId = pq.ComponentId,
                ProductName = c.Name,
                Sku = c.Sku,
                AskerName = pq.AskerName,
                AskerEmail = pq.AskerEmail,
                Question = pq.Question,
                Answer = pq.Answer,
                Status = pq.Status,
                CreatedAtUtc = pq.CreatedAtUtc,
                AnsweredAtUtc = pq.AnsweredAtUtc
            }
        ).Take(200).ToListAsync(ct);

        foreach (var r in rows)
            r.StatusDisplay = ProductQuestionStatus.ToDisplayName(r.Status);

        return View(new CskhQuestionsPageVm { StatusFilter = filter, Rows = rows });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> AnswerQuestion(int id, string answer, CancellationToken ct)
    {
        var guard = EnsureAdmin();
        if (guard is not null) return guard;

        var row = await _db.ProductQuestions.SingleOrDefaultAsync(x => x.ProductQuestionId == id, ct);
        if (row is null) return NotFound();

        if (string.IsNullOrWhiteSpace(answer) || answer.Trim().Length < 2)
        {
            TempData["AdminInfo"] = "Nội dung trả lời quá ngắn.";
            return RedirectToAction(nameof(Questions), new { status = row.Status });
        }

        row.Answer = answer.Trim();
        row.Status = ProductQuestionStatus.Answered;
        row.AnsweredAtUtc = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);
        TempData["AdminInfo"] = "Đã trả lời câu hỏi.";
        return RedirectToAction(nameof(Questions), new { status = "ANSWERED" });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> HideQuestion(int id, CancellationToken ct)
    {
        var guard = EnsureAdmin();
        if (guard is not null) return guard;

        var row = await _db.ProductQuestions.SingleOrDefaultAsync(x => x.ProductQuestionId == id, ct);
        if (row is null) return NotFound();
        row.Status = ProductQuestionStatus.Hidden;
        await _db.SaveChangesAsync(ct);
        TempData["AdminInfo"] = "Đã ẩn câu hỏi.";
        return RedirectToAction(nameof(Questions), new { status = "HIDDEN" });
    }

    // ─── Reviews ───────────────────────────────────────────

    [HttpGet]
    public async Task<IActionResult> Reviews(string? status, CancellationToken ct)
    {
        var guard = EnsureAdmin();
        if (guard is not null) return guard;

        ViewData["Title"] = "Đánh giá sản phẩm";
        var filter = (status ?? "PENDING").Trim().ToUpperInvariant();
        // PENDING = chưa duyệt, APPROVED = đã duyệt, ALL
        if (filter is not ("PENDING" or "APPROVED" or "ALL"))
            filter = "PENDING";

        var q = _db.ProductReviews.AsNoTracking().AsQueryable();
        if (filter == "PENDING")
            q = q.Where(x => !x.IsApproved);
        else if (filter == "APPROVED")
            q = q.Where(x => x.IsApproved);

        var rows = await (
            from r in q
            join c in _db.Components.AsNoTracking() on r.ProductId equals c.ComponentId
            orderby r.CreatedAt descending
            select new CskhReviewRow
            {
                ReviewId = r.ReviewId,
                ProductId = r.ProductId,
                ProductName = c.Name,
                Sku = c.Sku,
                CustomerName = r.CustomerName,
                Rating = r.Rating,
                Comment = r.Comment,
                IsApproved = r.IsApproved,
                CreatedAt = r.CreatedAt
            }
        ).Take(200).ToListAsync(ct);

        return View(new CskhReviewsPageVm { StatusFilter = filter, Rows = rows });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ApproveReview(int id, CancellationToken ct)
    {
        var guard = EnsureAdmin();
        if (guard is not null) return guard;

        var row = await _db.ProductReviews.SingleOrDefaultAsync(x => x.ReviewId == id, ct);
        if (row is null) return NotFound();
        row.IsApproved = true;
        await _db.SaveChangesAsync(ct);
        TempData["AdminInfo"] = "Đã duyệt đánh giá.";
        return RedirectToAction(nameof(Reviews), new { status = "APPROVED" });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> RejectReview(int id, CancellationToken ct)
    {
        var guard = EnsureAdmin();
        if (guard is not null) return guard;

        var row = await _db.ProductReviews.SingleOrDefaultAsync(x => x.ReviewId == id, ct);
        if (row is null) return NotFound();
        row.IsApproved = false;
        await _db.SaveChangesAsync(ct);
        TempData["AdminInfo"] = "Đã ẩn / bỏ duyệt đánh giá.";
        return RedirectToAction(nameof(Reviews), new { status = "PENDING" });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteReview(int id, CancellationToken ct)
    {
        var guard = EnsureAdmin();
        if (guard is not null) return guard;

        var row = await _db.ProductReviews.SingleOrDefaultAsync(x => x.ReviewId == id, ct);
        if (row is not null)
        {
            _db.ProductReviews.Remove(row);
            await _db.SaveChangesAsync(ct);
            TempData["AdminInfo"] = "Đã xóa đánh giá.";
        }
        return RedirectToAction(nameof(Reviews));
    }

    // ─── Warranty ──────────────────────────────────────────

    [HttpGet]
    public async Task<IActionResult> Warranties(string? status, CancellationToken ct)
    {
        var guard = EnsureAdmin();
        if (guard is not null) return guard;

        ViewData["Title"] = "Bảo hành";
        var filter = (status ?? "PENDING").Trim().ToUpperInvariant();
        if (filter is not ("PENDING" or "APPROVED" or "REJECTED" or "COMPLETED" or "ALL"))
            filter = "PENDING";

        var q = _db.WarrantyClaims.AsNoTracking().AsQueryable();
        if (filter != "ALL")
            q = q.Where(x => x.Status == filter);

        var rows = await (
            from w in q
            join c in _db.Components.AsNoTracking() on w.ComponentId equals c.ComponentId
            orderby w.CreatedAtUtc descending
            select new CskhWarrantyRow
            {
                WarrantyClaimId = w.WarrantyClaimId,
                OrderId = w.OrderId,
                ComponentId = w.ComponentId,
                ProductName = c.Name,
                Sku = c.Sku,
                CustomerId = w.CustomerId,
                SerialNumber = w.SerialNumber,
                IssueDescription = w.IssueDescription,
                Status = w.Status,
                AdminNote = w.AdminNote,
                CreatedAtUtc = w.CreatedAtUtc,
                ResolvedAtUtc = w.ResolvedAtUtc
            }
        ).Take(200).ToListAsync(ct);

        foreach (var r in rows)
        {
            r.StatusDisplay = WarrantyClaimStatus.ToDisplayName(r.Status);
            r.StatusBadgeClass = WarrantyClaimStatus.ToBadgeClass(r.Status);
        }

        return View(new CskhWarrantiesPageVm { StatusFilter = filter, Rows = rows });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> UpdateWarranty(int id, string status, string? adminNote, CancellationToken ct)
    {
        var guard = EnsureAdmin();
        if (guard is not null) return guard;

        var normalized = (status ?? "").Trim().ToUpperInvariant();
        if (normalized is not (
            WarrantyClaimStatus.Pending or WarrantyClaimStatus.Approved
            or WarrantyClaimStatus.Rejected or WarrantyClaimStatus.Completed))
        {
            TempData["AdminInfo"] = "Trạng thái không hợp lệ.";
            return RedirectToAction(nameof(Warranties));
        }

        var row = await _db.WarrantyClaims.SingleOrDefaultAsync(x => x.WarrantyClaimId == id, ct);
        if (row is null) return NotFound();

        row.Status = normalized;
        row.AdminNote = string.IsNullOrWhiteSpace(adminNote) ? row.AdminNote : adminNote.Trim();
        if (normalized is WarrantyClaimStatus.Rejected or WarrantyClaimStatus.Completed)
            row.ResolvedAtUtc = DateTime.UtcNow;

        await _db.SaveChangesAsync(ct);
        TempData["AdminInfo"] = "Đã cập nhật yêu cầu bảo hành.";
        return RedirectToAction(nameof(Warranties), new { status = normalized });
    }

    private IActionResult? EnsureAdmin() =>
        AdminPortalAccess.EnsureCanAccess(this, User, nameof(Questions), "Cskh");
}

public sealed class CskhQuestionsPageVm
{
    public string StatusFilter { get; set; } = "PENDING";
    public List<CskhQuestionRow> Rows { get; set; } = [];
}

public sealed class CskhQuestionRow
{
    public int ProductQuestionId { get; set; }
    public int ComponentId { get; set; }
    public string ProductName { get; set; } = "";
    public string Sku { get; set; } = "";
    public string AskerName { get; set; } = "";
    public string? AskerEmail { get; set; }
    public string Question { get; set; } = "";
    public string? Answer { get; set; }
    public string Status { get; set; } = "";
    public string StatusDisplay { get; set; } = "";
    public DateTime CreatedAtUtc { get; set; }
    public DateTime? AnsweredAtUtc { get; set; }
}

public sealed class CskhReviewsPageVm
{
    public string StatusFilter { get; set; } = "PENDING";
    public List<CskhReviewRow> Rows { get; set; } = [];
}

public sealed class CskhReviewRow
{
    public int ReviewId { get; set; }
    public int ProductId { get; set; }
    public string ProductName { get; set; } = "";
    public string Sku { get; set; } = "";
    public string CustomerName { get; set; } = "";
    public int Rating { get; set; }
    public string Comment { get; set; } = "";
    public bool IsApproved { get; set; }
    public DateTime CreatedAt { get; set; }
}

public sealed class CskhWarrantiesPageVm
{
    public string StatusFilter { get; set; } = "PENDING";
    public List<CskhWarrantyRow> Rows { get; set; } = [];
}

public sealed class CskhWarrantyRow
{
    public int WarrantyClaimId { get; set; }
    public int OrderId { get; set; }
    public int ComponentId { get; set; }
    public string ProductName { get; set; } = "";
    public string Sku { get; set; } = "";
    public int? CustomerId { get; set; }
    public string? SerialNumber { get; set; }
    public string IssueDescription { get; set; } = "";
    public string Status { get; set; } = "";
    public string StatusDisplay { get; set; } = "";
    public string StatusBadgeClass { get; set; } = "";
    public string? AdminNote { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime? ResolvedAtUtc { get; set; }
}
