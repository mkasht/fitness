using System.Data;
using System.Security.Claims;
using FitnessCenter.Web.Data;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace FitnessCenter.Web.Services;

public record ManagedUser(string Id, string Name, string Email, string Phone, string Role);

public sealed class UserAdminService(IDbContextFactory<ApplicationDbContext> factory)
{
    public static string RoleLabel(string role) => role switch
    {
        Roles.Admin => "Администратор",
        Roles.Manager => "Менеджер",
        Roles.User => "Клиент",
        _ => "Без роли"
    };

    public async Task<List<ManagedUser>> LoadAsync(ClaimsPrincipal actor)
    {
        await using var db = await factory.CreateDbContextAsync();
        await StaffService.RequireAsync(actor, db, true);
        var users = await db.Users.AsNoTracking().Where(u => u.UserName != null)
            .OrderBy(u => u.FullName).ThenBy(u => u.Email).ToListAsync();
        var assignments = await (from assignment in db.UserRoles
            join role in db.Roles on assignment.RoleId equals role.Id
            select new { assignment.UserId, role.Name }).ToListAsync();
        var roles = assignments.ToLookup(a => a.UserId, a => a.Name);
        return users.Select(u => new ManagedUser(u.Id, u.FullName, u.Email ?? "", u.PhoneNumber ?? "",
            new[] { Roles.Admin, Roles.Manager, Roles.User }.FirstOrDefault(role => roles[u.Id].Contains(role)) ?? "")).ToList();
    }

    public async Task<OperationResult> SaveAsync(ClaimsPrincipal actor, ManagedUser input)
    {
        if (string.IsNullOrWhiteSpace(input.Name) || input.Name.Length > 100 || input.Phone.Length > 30 ||
            input.Role is not (Roles.Admin or Roles.Manager or Roles.User))
            return new(false, "Проверьте имя, телефон и роль.");
        await using var db = await factory.CreateDbContextAsync();
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        await StaffService.RequireAsync(actor, db, true);
        try
        {
            var user = await db.Users.SingleOrDefaultAsync(u => u.Id == input.Id && u.UserName != null);
            if (user is null) return new(false, "Учётная запись не найдена.");
            var available = await db.Roles.Where(r => r.Name == Roles.Admin || r.Name == Roles.Manager || r.Name == Roles.User)
                .ToDictionaryAsync(r => r.Name!, r => r.Id);
            var assignments = await db.UserRoles.Where(r => r.UserId == user.Id).ToListAsync();
            var targetRoleId = available[input.Role];
            var managed = assignments.Where(r => available.ContainsValue(r.RoleId)).ToList();
            var roleChanged = managed.Count != 1 || managed[0].RoleId != targetRoleId;
            if (roleChanged)
            {
                if (user.Id == BookingService.UserId(actor)) return new(false, "Изменить собственную роль нельзя.");
                var adminId = available[Roles.Admin];
                if (managed.Any(r => r.RoleId == adminId) && input.Role != Roles.Admin &&
                    !await db.UserRoles.AnyAsync(r => r.RoleId == adminId && r.UserId != user.Id))
                    return new(false, "В системе должен остаться хотя бы один администратор.");
                db.UserRoles.RemoveRange(managed.Where(r => r.RoleId != targetRoleId));
                if (!managed.Any(r => r.RoleId == targetRoleId))
                    db.UserRoles.Add(new IdentityUserRole<string> { UserId = user.Id, RoleId = targetRoleId });
                user.SecurityStamp = Guid.NewGuid().ToString("N");
            }
            user.FullName = input.Name.Trim();
            var phone = input.Phone.Trim();
            if (user.PhoneNumber != phone)
            {
                user.PhoneNumber = phone;
                user.PhoneNumberConfirmed = false;
            }
            user.ConcurrencyStamp = Guid.NewGuid().ToString("N");
            await db.SaveChangesAsync();
            await tx.CommitAsync();
            return new(true, roleChanged ? "Данные и роль сохранены. Для новой роли потребуется повторный вход." : "Данные пользователя сохранены.");
        }
        catch (Exception ex) when (BookingService.IsConflict(ex)) { return new(false, "Данные изменились. Повторите действие."); }
    }
}
