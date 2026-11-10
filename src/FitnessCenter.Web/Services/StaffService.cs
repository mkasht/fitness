using System.Data;
using System.Security.Claims;
using FitnessCenter.Web.Data;
using Microsoft.EntityFrameworkCore;

namespace FitnessCenter.Web.Services;

public sealed class StaffService(IDbContextFactory<ApplicationDbContext> factory, TimeProvider time)
{
    public static async Task RequireAsync(ClaimsPrincipal actor, ApplicationDbContext db, bool admin = false)
    {
        var id = BookingService.UserId(actor);
        var allowed = await (from ur in db.UserRoles join r in db.Roles on ur.RoleId equals r.Id
            where ur.UserId == id && (r.Name == Roles.Admin || (!admin && r.Name == Roles.Manager)) select ur).AnyAsync();
        if (!allowed) throw new UnauthorizedAccessException("Недостаточно прав.");
    }
    public async Task<List<Booking>> BookingsAsync(ClaimsPrincipal actor, BookingStatus? status = null, string? search = null)
    {
        await using var db = await factory.CreateDbContextAsync(); await RequireAsync(actor, db);
        var query = db.Bookings.AsNoTracking().Where(b => status == null || b.Status == status);
        if (search?.Trim() is { Length: > 0 } term)
            query = query.Where(b => b.User.FullName.Contains(term) || (b.User.Email ?? "").Contains(term)
                || b.Session.Service.Name.Contains(term) || b.Session.Trainer.Name.Contains(term));
        return await query.Include(x => x.User).Include(x => x.Session).ThenInclude(x => x.Service)
            .Include(x => x.Session).ThenInclude(x => x.Trainer).OrderByDescending(x => x.CreatedAtUtc).ToListAsync();
    }
    public async Task<OperationResult> SetStatusAsync(ClaimsPrincipal actor, int id, BookingStatus status)
    {
        await using var db = await factory.CreateDbContextAsync(); await RequireAsync(actor, db);
        var b = await db.Bookings.Include(x => x.Session).SingleOrDefaultAsync(x => x.Id == id);
        if (b is null) return new(false, "Запись не найдена.");
        var now = time.GetUtcNow().UtcDateTime;
        var valid = (b.Status == BookingStatus.Pending && (status is BookingStatus.Confirmed or BookingStatus.Rejected) && b.Session.StartsAtUtc > now)
            || (b.Status.IsActive() && status == BookingStatus.Cancelled && b.Session.StartsAtUtc > now)
            || (b.Status == BookingStatus.Confirmed && (status is BookingStatus.Attended or BookingStatus.NoShow) && b.Session.EndsAtUtc <= now);
        if (!valid || b.Session.IsCancelled) return new(false, "Этот переход статуса недоступен. Обновите список.");
        b.Status = status;
        db.Notifications.Add(new() { UserId = b.UserId, Text = $"Статус записи: {status.Label().ToLowerInvariant()}." });
        try { await db.SaveChangesAsync(); return new(true, "Статус обновлён."); }
        catch (DbUpdateConcurrencyException) { return new(false, "Запись уже изменилась. Обновите список."); }
    }
    public async Task<List<Review>> ReviewsAsync(ClaimsPrincipal actor)
    {
        await using var db = await factory.CreateDbContextAsync(); await RequireAsync(actor, db);
        return await db.Reviews.AsNoTracking().Include(x => x.Booking).ThenInclude(x => x.User).OrderByDescending(x => x.CreatedAtUtc).ToListAsync();
    }
    public async Task<OperationResult> SetReviewPublicationAsync(ClaimsPrincipal actor, int id, bool published)
    {
        await using var db = await factory.CreateDbContextAsync(); await RequireAsync(actor, db, true);
        var affected = await db.Reviews.Where(r => r.Id == id)
            .ExecuteUpdateAsync(query => query.SetProperty(r => r.IsPublished, published)
                .SetProperty(r => r.IsFeatured, r => published && r.IsFeatured));
        return affected == 0 ? new(false, "Отзыв не найден.") :
            new(true, published ? "Отзыв опубликован." : "Отзыв снят с публикации.");
    }

    public async Task<OperationResult> SetReviewFeaturedAsync(ClaimsPrincipal actor, int id, bool featured)
    {
        await using var db = await factory.CreateDbContextAsync();
        await RequireAsync(actor, db, true);
        var affected = await db.Reviews.Where(review => review.Id == id && review.IsPublished)
            .ExecuteUpdateAsync(query => query.SetProperty(review => review.IsFeatured, featured));
        return affected == 0 ? new(false, "Отзыв должен быть опубликован.") :
            new(true, featured ? "Отзыв добавлен в подборку главной." : "Отзыв убран из подборки главной.");
    }
    public async Task<List<FeedbackMessage>> FeedbackAsync(ClaimsPrincipal actor)
    {
        await using var db = await factory.CreateDbContextAsync(); await RequireAsync(actor, db, true);
        return await db.Feedback.AsNoTracking().OrderByDescending(x => x.CreatedAtUtc).Take(200).ToListAsync();
    }
    public async Task ProcessFeedbackAsync(ClaimsPrincipal actor, int id)
    {
        await using var db = await factory.CreateDbContextAsync(); await RequireAsync(actor, db, true);
        var message = await db.Feedback.FindAsync(id);
        if (message is not null) { message.IsProcessed = true; await db.SaveChangesAsync(); }
    }
    public Task<OperationResult> AddSessionAsync(ClaimsPrincipal actor, int serviceId, int trainerId, DateTime start, int capacity) =>
        SaveSessionAsync(actor, null, serviceId, trainerId, start, capacity);

    public async Task<List<TrainingSession>> UpcomingSessionsAsync(ClaimsPrincipal actor)
    {
        await using var db = await factory.CreateDbContextAsync();
        await RequireAsync(actor, db, true);
        var now = time.GetUtcNow().UtcDateTime;
        return await db.Sessions.AsNoTracking().Include(s => s.Service).Include(s => s.Trainer)
            .Include(s => s.Bookings.Where(b => b.Status == BookingStatus.Pending || b.Status == BookingStatus.Confirmed))
            .Where(s => s.StartsAtUtc > now && !s.IsCancelled).OrderBy(s => s.StartsAtUtc).ToListAsync();
    }

    public async Task<OperationResult> SaveSessionAsync(ClaimsPrincipal actor, int? id, int serviceId, int trainerId, DateTime start, int capacity)
    {
        await using var db = await factory.CreateDbContextAsync(); await RequireAsync(actor, db, true);
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        try
        {
            var service = await db.Services.SingleOrDefaultAsync(x => x.Id == serviceId && x.IsActive);
            var trainer = await db.Trainers.Include(x => x.Services).SingleOrDefaultAsync(x => x.Id == trainerId && x.IsActive);
            if (service is null || trainer is null || !trainer.Services.Any(x => x.Id == serviceId)) return new(false, "Выберите тренера, который проводит эту услугу.");
            var now = time.GetUtcNow().UtcDateTime;
            if (start <= now || capacity < 1 || capacity > 100) return new(false, "Выберите будущее время и вместимость от 1 до 100.");
            var session = id is null ? new TrainingSession() : await db.Sessions.Include(s => s.Bookings).SingleOrDefaultAsync(s => s.Id == id);
            if (session is null || id is not null && (session.IsCancelled || session.StartsAtUtc <= now))
                return new(false, "Это занятие уже нельзя изменить.");
            var hasMoved = session.ServiceId != serviceId || session.TrainerId != trainerId || session.StartsAtUtc != start;
            if (session.Bookings.Count > 0 && hasMoved)
                return new(false, "У занятия есть записи. Можно изменить число мест или отменить занятие и создать новое.");
            if (capacity < session.Bookings.Count(b => b.Status.IsActive()))
                return new(false, "Число мест меньше количества активных записей.");
            var end = hasMoved ? start.AddMinutes(service.DurationMinutes) : session.EndsAtUtc;
            if (await db.Sessions.AnyAsync(x => x.Id != session.Id && !x.IsCancelled && x.TrainerId == trainerId && x.StartsAtUtc < end && x.EndsAtUtc > start))
                return new(false, "У тренера уже есть занятие в это время.");
            session.ServiceId = serviceId;
            session.TrainerId = trainerId;
            session.StartsAtUtc = start;
            session.EndsAtUtc = end;
            session.Capacity = capacity;
            if (id is null) db.Sessions.Add(session);
            await db.SaveChangesAsync();
            await tx.CommitAsync();
            return new(true, id is null ? "Занятие добавлено в расписание." : "Занятие обновлено.");
        }
        catch (Exception ex) when (BookingService.IsConflict(ex)) { return new(false, "Расписание изменилось. Повторите сохранение."); }
    }
    public async Task<OperationResult> CancelSessionAsync(ClaimsPrincipal actor, int id)
    {
        await using var db = await factory.CreateDbContextAsync(); await RequireAsync(actor, db, true);
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        try
        {
            var session = await db.Sessions.Include(x => x.Bookings).SingleOrDefaultAsync(x => x.Id == id);
            if (session is null || session.IsCancelled || session.StartsAtUtc <= time.GetUtcNow().UtcDateTime) return new(false, "Это занятие нельзя отменить.");
            MarkSessionCancelled(db, session);
            await db.SaveChangesAsync(); await tx.CommitAsync(); return new(true, "Занятие отменено, участники получили уведомления.");
        }
        catch (Exception ex) when (BookingService.IsConflict(ex)) { return new(false, "Данные изменились. Повторите действие."); }
    }

    internal static void MarkSessionCancelled(ApplicationDbContext db, TrainingSession session)
    {
        session.IsCancelled = true;
        foreach (var booking in session.Bookings.Where(b => b.Status.IsActive()))
        {
            booking.Status = BookingStatus.Cancelled;
            db.Notifications.Add(new() { UserId = booking.UserId, Text = "Клуб отменил занятие. Выберите другое удобное время в расписании." });
        }
    }
}
