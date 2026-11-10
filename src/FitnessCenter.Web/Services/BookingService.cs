using System.Data;
using System.Security.Claims;
using FitnessCenter.Web.Data;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace FitnessCenter.Web.Services;

public record OperationResult(bool Success, string Message);

public sealed class BookingService(IDbContextFactory<ApplicationDbContext> factory, TimeProvider time)
{
    public static string UserId(ClaimsPrincipal user) => user.Identity?.IsAuthenticated == true
        ? user.FindFirstValue(ClaimTypes.NameIdentifier) ?? throw new UnauthorizedAccessException()
        : throw new UnauthorizedAccessException();

    public async Task<OperationResult> BookAsync(ClaimsPrincipal user, int sessionId, string? comment)
    {
        var userId = UserId(user);
        if (comment?.Length > 1000) return new(false, "Комментарий не должен превышать 1000 символов.");
        await using var db = await factory.CreateDbContextAsync();
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        try
        {
            var session = await db.Sessions.Include(x => x.Service).Include(x => x.Trainer).SingleOrDefaultAsync(x => x.Id == sessionId);
            var now = time.GetUtcNow().UtcDateTime;
            if (session is null || session.IsCancelled || !session.Service.IsActive || !session.Trainer.IsActive || session.StartsAtUtc <= now)
                return new(false, "Занятие уже началось или недоступно. Выберите другое время.");
            var old = await db.Bookings.SingleOrDefaultAsync(x => x.UserId == userId && x.SessionId == sessionId);
            if (old is not null && old.Status != BookingStatus.Cancelled && old.Status != BookingStatus.Rejected)
                return new(false, "Вы уже записаны на это занятие.");
            var occupied = await db.Bookings.CountAsync(x => x.SessionId == sessionId && (x.Status == BookingStatus.Pending || x.Status == BookingStatus.Confirmed));
            if (occupied >= session.Capacity) return new(false, "Все места уже заняты. Выберите другое занятие.");
            var overlap = await db.Bookings.AnyAsync(x => x.UserId == userId && (x.Status == BookingStatus.Pending || x.Status == BookingStatus.Confirmed)
                && !x.Session.IsCancelled && x.Session.StartsAtUtc < session.EndsAtUtc && x.Session.EndsAtUtc > session.StartsAtUtc);
            if (overlap) return new(false, "На это время у вас уже есть запись.");
            if (old is null) db.Bookings.Add(new() { UserId = userId, SessionId = sessionId, Comment = comment?.Trim() ?? "", CreatedAtUtc = now });
            else { old.Status = BookingStatus.Pending; old.Comment = comment?.Trim() ?? ""; old.CreatedAtUtc = now; }
            db.Notifications.Add(new() { UserId = userId, Text = $"Заявка на занятие «{session.Service.Name}» отправлена. Менеджер подтвердит запись." });
            await db.SaveChangesAsync();
            await tx.CommitAsync();
            return new(true, "Вы записаны! Заявка ожидает подтверждения. Следите за статусом в личном кабинете.");
        }
        catch (Exception ex) when (IsConflict(ex)) { return new(false, "Расписание только что изменилось. Обновите список и попробуйте ещё раз."); }
    }

    public async Task<List<Booking>> MyBookingsAsync(ClaimsPrincipal user)
    {
        var id = UserId(user);
        await using var db = await factory.CreateDbContextAsync();
        return await db.Bookings.AsNoTracking().Include(x => x.Session).ThenInclude(x => x.Service)
            .Include(x => x.Session).ThenInclude(x => x.Trainer).Where(x => x.UserId == id).OrderByDescending(x => x.Session.StartsAtUtc).ToListAsync();
    }

    public async Task<OperationResult> CancelAsync(ClaimsPrincipal user, int id)
    {
        var uid = UserId(user);
        await using var db = await factory.CreateDbContextAsync();
        var booking = await db.Bookings.Include(x => x.Session).SingleOrDefaultAsync(x => x.Id == id && x.UserId == uid);
        if (booking is null || !booking.Status.IsActive() || booking.Session.StartsAtUtc <= time.GetUtcNow().UtcDateTime)
            return new(false, "Эту запись уже нельзя отменить.");
        booking.Status = BookingStatus.Cancelled;
        db.Notifications.Add(new() { UserId = uid, Text = "Ваша запись отменена. Можно выбрать новое занятие." });
        try { await db.SaveChangesAsync(); return new(true, "Запись отменена, место освобождено."); }
        catch (DbUpdateConcurrencyException) { return new(false, "Статус изменился. Обновите страницу."); }
    }

    public async Task<OperationResult> AddReviewAsync(ClaimsPrincipal user, int bookingId, int rating, string text)
    {
        var uid = UserId(user);
        if (rating < 1 || rating > 5 || string.IsNullOrWhiteSpace(text) || text.Length > 1500)
            return new(false, "Укажите оценку от 1 до 5 и отзыв до 1500 символов.");
        await using var db = await factory.CreateDbContextAsync();
        if (!await db.Bookings.AnyAsync(x => x.Id == bookingId && x.UserId == uid && x.Status == BookingStatus.Attended))
            return new(false, "Отзыв можно оставить после подтверждённого посещения.");
        if (await db.Reviews.AnyAsync(x => x.BookingId == bookingId)) return new(false, "Вы уже оставили отзыв об этом занятии.");
        db.Reviews.Add(new() { BookingId = bookingId, Rating = rating, Text = text.Trim() });
        try { await db.SaveChangesAsync(); return new(true, "Спасибо! Отзыв отправлен на модерацию."); }
        catch (DbUpdateException ex) when (IsConflict(ex)) { return new(false, "Отзыв уже отправлен."); }
    }

    internal static bool IsConflict(Exception ex)
    {
        for (Exception? cause = ex; cause is not null; cause = cause.InnerException)
        {
            if (cause is DbUpdateConcurrencyException or SqlException { Number: 1205 or 2601 or 2627 })
                return true;
        }

        return false;
    }
}
