using System.ComponentModel.DataAnnotations;
using System.Data;
using System.Security.Claims;
using FitnessCenter.Web.Data;
using Microsoft.EntityFrameworkCore;
namespace FitnessCenter.Web.Services;
public sealed class CatalogAdminService(IDbContextFactory<ApplicationDbContext> factory, TimeProvider? clock = null, PublicDataCache? cache = null)
{
    public Task<OperationResult> DeleteServiceAsync(ClaimsPrincipal actor, int id) => DeleteAsync(actor, id, false);
    public Task<OperationResult> DeleteTrainerAsync(ClaimsPrincipal actor, int id) => DeleteAsync(actor, id, true);

    private async Task<OperationResult> DeleteAsync(ClaimsPrincipal actor, int id, bool trainer)
    {
        await using var db = await factory.CreateDbContextAsync();
        await StaffService.RequireAsync(actor, db, true);
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        try
        {
            var service = trainer ? null : await db.Services.FindAsync(id);
            var coach = trainer ? await db.Trainers.FindAsync(id) : null;
            if (service is null && coach is null) return new(false, "Запись каталога не найдена.");

            var sessions = db.Sessions.Where(s => trainer ? s.TrainerId == id : s.ServiceId == id);
            var hasHistory = await sessions.AnyAsync();
            if (hasHistory)
            {
                if (service is not null) service.IsActive = false;
                if (coach is not null) coach.IsActive = false;
                var now = (clock ?? TimeProvider.System).GetUtcNow().UtcDateTime;
                var upcoming = await sessions.Where(s => !s.IsCancelled && s.StartsAtUtc > now)
                    .Include(s => s.Bookings).ToListAsync();
                foreach (var session in upcoming) StaffService.MarkSessionCancelled(db, session);
            }
            else if (service is not null) db.Services.Remove(service);
            else db.Trainers.Remove(coach!);

            await db.SaveChangesAsync();
            await transaction.CommitAsync();
            cache?.InvalidateCatalog();
            return new(true, hasHistory
                ? "Запись убрана с сайта. Будущие занятия отменены, история сохранена."
                : "Запись удалена из каталога.");
        }
        catch (Exception ex) when (BookingService.IsConflict(ex)) { return new(false, "Данные изменились. Повторите действие."); }
    }

    public async Task<(List<FitnessService>, List<Trainer>)> LoadAsync(ClaimsPrincipal actor)
    {
        await using var db = await factory.CreateDbContextAsync(); await StaffService.RequireAsync(actor, db, true);
        return (await db.Services.AsNoTracking().OrderBy(x => x.Id).ToListAsync(), await db.Trainers.AsNoTracking().Include(x => x.Services).OrderBy(x => x.Id).ToListAsync());
    }
    public async Task<OperationResult> SaveServiceAsync(ClaimsPrincipal actor, FitnessService input, byte[]? imageContent = null)
    {
        await using var db = await factory.CreateDbContextAsync(); await StaffService.RequireAsync(actor, db, true);
        if (!Valid(input) || !SafeImage(input.Image)) return new(false, "Проверьте поля услуги и изображение.");
        var entity = input.Id == 0 ? new FitnessService() : await db.Services.FindAsync(input.Id);
        if (entity is null) return new(false, "Услуга не найдена.");
        var imagePath = input.Image;
        if (imageContent is not null)
        {
            var contentType = CatalogImage.DetectContentType(imageContent);
            if (contentType is null) return new(false, "Выберите изображение JPG, PNG или WebP до 5 МБ.");
            var image = new CatalogImage { Content = imageContent, ContentType = contentType };
            db.CatalogImages.Add(image);
            imagePath = $"/images/catalog/{image.Id:D}";
        }
        entity.Name = input.Name.Trim(); entity.Summary = input.Summary.Trim(); entity.Description = input.Description.Trim();
        entity.Category = input.Category.Trim(); entity.DurationMinutes = input.DurationMinutes; entity.Price = input.Price;
        entity.Image = imagePath; entity.IsActive = input.IsActive;
        if (input.Id == 0) db.Services.Add(entity);
        await db.SaveChangesAsync();
        cache?.InvalidateCatalog();
        return new(true, "Услуга сохранена.");
    }
    public async Task<OperationResult> SaveTrainerAsync(ClaimsPrincipal actor, Trainer input, int[] serviceIds)
    {
        await using var db = await factory.CreateDbContextAsync(); await StaffService.RequireAsync(actor, db, true);
        if (!Valid(input) || !SafeImage(input.Image)) return new(false, "Проверьте поля профиля и изображение.");
        var entity = input.Id == 0 ? new Trainer() : await db.Trainers.Include(x => x.Services).SingleOrDefaultAsync(x => x.Id == input.Id);
        if (entity is null) return new(false, "Тренер не найден.");
        entity.Name = input.Name.Trim(); entity.Specialty = input.Specialty.Trim(); entity.Biography = input.Biography.Trim();
        entity.ExperienceYears = input.ExperienceYears; entity.Image = input.Image; entity.IsActive = input.IsActive;
        entity.Services = await db.Services.Where(x => serviceIds.Contains(x.Id)).ToListAsync();
        if (input.Id == 0) db.Trainers.Add(entity);
        await db.SaveChangesAsync();
        cache?.InvalidateCatalog();
        return new(true, "Профиль тренера сохранён.");
    }
    private static bool Valid(object input) => Validator.TryValidateObject(input, new(input), [], true);
    private static bool SafeImage(string path) => path.StartsWith("/images/", StringComparison.Ordinal) && !path.Contains("..") && !path.Contains('?');
}
