using System.Security.Cryptography;
using FitnessCenter.Web.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace FitnessCenter.Web.Data;

public static class DbSeeder
{
    public static async Task InitializeAsync(IServiceProvider services, IWebHostEnvironment environment)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        await db.Database.MigrateAsync();
        await ClubTrainerRoster.ReplaceLegacyAsync(db);
        var roles = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();
        foreach (var role in new[] { Roles.Admin, Roles.Manager, Roles.User })
            if (!await roles.RoleExistsAsync(role)) Check(await roles.CreateAsync(new(role)));

        var localDemo = environment.IsDevelopment();
        var configuration = scope.ServiceProvider.GetRequiredService<IConfiguration>();
        if (!localDemo && !configuration.GetValue<bool>("Demo:Seed")) return;

        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var credentials = new List<string>();
        foreach (var (email, name, role, passwordKey) in new[]
        {
            ("admin@pulse.local", "Администратор", Roles.Admin, "Demo:AdminPassword"),
            ("manager@pulse.local", "Менеджер", Roles.Manager, "Demo:ManagerPassword"),
            ("client@pulse.local", "Александр", Roles.User, "Demo:ClientPassword")
        })
        {
            var user = await users.FindByEmailAsync(email);
            if (user is null)
            {
                var configuredPassword = configuration[passwordKey];
                if (!localDemo && role != Roles.User && string.IsNullOrWhiteSpace(configuredPassword))
                    throw new InvalidOperationException($"Set {passwordKey.Replace(":", "__")} before creating the hosted demo account.");
                var password = localDemo || string.IsNullOrWhiteSpace(configuredPassword)
                    ? "Aa1!" + Convert.ToHexString(RandomNumberGenerator.GetBytes(16))
                    : configuredPassword;
                user = new() { UserName = email, Email = email, EmailConfirmed = true, FullName = name };
                Check(await users.CreateAsync(user, password));
                if (localDemo) credentials.Add($"{name}: {email} | {password}");
            }
            if ((await users.GetRolesAsync(user)).Count == 0) Check(await users.AddToRoleAsync(user, role));
        }
        if (credentials.Count > 0)
        {
            var directory = Path.Combine(environment.ContentRootPath, "App_Data");
            Directory.CreateDirectory(directory);
            await File.AppendAllLinesAsync(Path.Combine(directory, "demo-accounts.txt"), credentials);
        }

        var clock = scope.ServiceProvider.GetRequiredService<ClubClock>();
        if (!await db.Services.AnyAsync())
            await SeedCatalogAsync(db, users, clock);
        await DemoReviews.SeedAsync(db, clock);
    }

    private static async Task SeedCatalogAsync(ApplicationDbContext db, UserManager<ApplicationUser> users, ClubClock clock)
    {
        var catalog = new List<FitnessService>
        {
            new() { Name = "Тренажёрный зал", Category = "Сила", Summary = "Свободные веса, современные тренажёры и пространство для твоего прогресса.", Description = "Силовая тренировка для всех уровней подготовки. Тренер поможет освоить технику, выбрать рабочую нагрузку и составить понятную программу. В зале есть кардиозона, силовые тренажёры и свободные веса.", DurationMinutes = 60, Price = 700, Image = "/images/club.jpg" },
            new() { Name = "Функциональный тренинг", Category = "Энергия", Summary = "Развивай силу, выносливость и координацию в небольшой группе.", Description = "Разнообразные упражнения с собственным весом и оборудованием. Нагрузка адаптируется под каждого участника. Занятие включает разминку, основной комплекс и восстановление.", DurationMinutes = 55, Price = 800, Image = "/images/service-functional.webp" },
            new() { Name = "Йога", Category = "Баланс", Summary = "Внимание к дыханию, подвижности и ощущениям в собственном теле.", Description = "Спокойная практика для улучшения гибкости и баланса. Подходит для знакомства с йогой: инструктор объясняет каждое положение и предлагает доступные варианты упражнений.", DurationMinutes = 60, Price = 700, Image = "/images/service-yoga.webp" },
            new() { Name = "Пилатес", Category = "Баланс", Summary = "Укрепляй глубокие мышцы, улучшай осанку и контроль движения.", Description = "Тренировка на коврике с акцентом на технику, дыхание и осознанную работу мышц. Комфортный темп и небольшая группа позволяют тренеру уделить внимание каждому участнику.", DurationMinutes = 50, Price = 800, Image = "/images/pulse-studio.webp" },
            new() { Name = "Плавание", Category = "Восстановление", Summary = "Больше свободы движения в светлом бассейне с панорамными окнами.", Description = "Занятие по плаванию с инструктором. Осваивайте базовые движения или совершенствуйте технику в удобном темпе. Уровень нагрузки подбирается индивидуально.", DurationMinutes = 45, Price = 900, Image = "/images/pool.jpg" },
            new() { Name = "Персональная тренировка", Category = "Сила", Summary = "Твои цели, твой темп и полное внимание тренера.", Description = "Индивидуальное занятие с тренером: обсуждение целей, подбор упражнений и работа над техникой. Можно начать с нуля или скорректировать уже привычную программу.", DurationMinutes = 60, Price = 1800, Image = "/images/service-personal.webp" }
        };
        var roster = new ClubTrainerRoster(catalog);
        db.Services.AddRange(catalog);
        db.Trainers.AddRange(roster.All);

        var today = clock.Now.Date;
        var timetable = new[]
        {
            (0, 9, roster.Vitalik), (2, 10, roster.OlgaMindBody), (1, 12, roster.Alexandra),
            (3, 14, roster.OlgaMindBody), (4, 16, roster.Yuri), (5, 17, roster.OlgaPersonal),
            (4, 18, roster.Yuri), (5, 19, roster.Valery)
        };
        for (var day = 0; day < 15; day++)
        {
            foreach (var (serviceIndex, hour, trainer) in timetable)
            {
                var start = clock.Utc(today.AddDays(day).AddHours(hour));
                if (start <= DateTime.UtcNow) continue;
                var service = catalog[serviceIndex];
                db.Sessions.Add(new() { Service = service, Trainer = trainer, StartsAtUtc = start, EndsAtUtc = start.AddMinutes(service.DurationMinutes), Capacity = serviceIndex == 5 ? 1 : 8 });
            }
        }
        var demoUser = (await users.FindByEmailAsync("client@pulse.local"))!;
        var past = new TrainingSession { Service = catalog[0], Trainer = roster.Vitalik, StartsAtUtc = clock.Utc(today.AddDays(-2).AddHours(10)), EndsAtUtc = clock.Utc(today.AddDays(-2).AddHours(11)), Capacity = 8 };
        var visited = new Booking { User = demoUser, Session = past, Status = BookingStatus.Attended, CreatedAtUtc = past.StartsAtUtc.AddDays(-1) };
        db.Reviews.Add(new() { Booking = visited, Rating = 5, Text = DemoReviews.FirstReviewText, IsPublished = true, IsFeatured = true });
        await db.SaveChangesAsync();
    }

    private static void Check(IdentityResult result)
    {
        if (!result.Succeeded) throw new InvalidOperationException(string.Join("; ", result.Errors.Select(x => x.Description)));
    }
}

