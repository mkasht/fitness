using FitnessCenter.Web.Services;
using Microsoft.EntityFrameworkCore;

namespace FitnessCenter.Web.Data;

internal sealed class ClubTrainerRoster(IEnumerable<FitnessService> services)
{
    private const string PortraitPrefix = "/images/figma/team-";

    public Trainer Vitalik { get; } = CreateTrainer(services, "Виталик", "Персональный тренер", "vitalik",
        "Помогает освоить упражнения в тренажёрном зале и подобрать нагрузку под цель занятия.",
        "Тренажёрный зал", "Персональная тренировка");

    public Trainer OlgaPersonal { get; } = CreateTrainer(services, "Ольга", "Персональный тренер", "olga-personal",
        "Ведёт индивидуальные занятия с акцентом на технику упражнений и постепенное увеличение нагрузки.",
        "Персональная тренировка");

    public Trainer Valery { get; } = CreateTrainer(services, "Валерий", "Персональный тренер", "valery",
        "На персональных тренировках помогает отработать силовые упражнения и подобрать рабочий вес.",
        "Персональная тренировка");

    public Trainer Alexandra { get; } = CreateTrainer(services, "Александра", "Функциональный тренер", "alexandra",
        "Ведёт функциональные тренировки на силу, выносливость и координацию.",
        "Функциональный тренинг");

    public Trainer Yuri { get; } = CreateTrainer(services, "Юрий", "Тренер по плаванию", "yuri",
        "Помогает освоиться в воде и отработать дыхание, положение тела и технику гребка.",
        "Плавание");

    public Trainer OlgaMindBody { get; } = CreateTrainer(services, "Ольга", "Тренер йоги и пилатеса", "olga-mind-body",
        "Ведёт занятия по йоге и пилатесу с вниманием к дыханию, подвижности и контролю движений.",
        "Йога", "Пилатес");

    public Trainer[] All => [Vitalik, OlgaPersonal, Valery, Alexandra, Yuri, OlgaMindBody];

    public static async Task ReplaceLegacyAsync(ApplicationDbContext db)
    {
        var legacyQuery = db.Trainers.Where(t => t.IsActive &&
            ((t.Name == "Артём Соколов" && t.Image == "/images/trainer-male.jpg") ||
             (t.Name == "Анна Волкова" && t.Image == "/images/trainer-female.jpg") ||
             (t.Name == "Илья Морозов" && t.Image == "/images/trainer-swimming.webp")));

        if (!await legacyQuery.AnyAsync()) return;

        await using var transaction = await db.Database.BeginTransactionAsync();
        // Both local versions of the site can start against the same database.
        await db.Database.ExecuteSqlRawAsync("""
            DECLARE @result int;
            EXEC @result = sp_getapplock @Resource = 'PulseFitness.ClubTrainerRoster',
                @LockMode = 'Exclusive', @LockOwner = 'Transaction', @LockTimeout = 10000;
            IF @result < 0 THROW 51000, 'Unable to lock the club trainer roster.', 1;
            """);

        var legacy = await legacyQuery.ToListAsync();
        if (legacy.Count == 0 || await db.Trainers.AnyAsync(t => t.Image.StartsWith(PortraitPrefix))) return;

        var roster = new ClubTrainerRoster(await db.Services.ToListAsync()).All;
        db.Trainers.AddRange(roster);

        var legacyIds = legacy.Select(t => t.Id).ToArray();
        var now = DateTime.UtcNow;
        var sessions = await db.Sessions
            .Include(s => s.Bookings.Where(b => b.Status == BookingStatus.Pending || b.Status == BookingStatus.Confirmed))
            .Where(s => legacyIds.Contains(s.TrainerId) && s.StartsAtUtc > now && !s.IsCancelled)
            .OrderBy(s => s.StartsAtUtc).ThenBy(s => s.Id)
            .ToListAsync();
        var assigned = roster.ToDictionary(trainer => trainer, _ => new List<TrainingSession>());

        foreach (var session in sessions)
        {
            var replacement = roster
                .Where(t => t.Services.Any(s => s.Id == session.ServiceId) &&
                    !assigned[t].Any(s => s.EndsAtUtc > session.StartsAtUtc && s.StartsAtUtc < session.EndsAtUtc))
                .MinBy(t => assigned[t].Count);

            if (replacement is null)
                session.IsCancelled = true;
            else
            {
                session.Trainer = replacement;
                assigned[replacement].Add(session);
            }

            foreach (var booking in session.Bookings)
            {
                if (session.IsCancelled) booking.Status = BookingStatus.Cancelled;
                db.Notifications.Add(new()
                {
                    UserId = booking.UserId,
                    CreatedAtUtc = now,
                    Text = replacement is null
                        ? "Клуб отменил занятие после смены тренера. Выберите другое время в расписании."
                        : $"Занятие проведёт {replacement.Name} ({replacement.Specialty.ToLowerInvariant()}). Время занятия прежнее."
                });
            }
        }

        foreach (var trainer in legacy) trainer.IsActive = false;
        await db.SaveChangesAsync();
        await transaction.CommitAsync();
    }

    private static Trainer CreateTrainer(IEnumerable<FitnessService> services, string name, string specialty,
        string portrait, string biography, params string[] directions) => new()
    {
        Name = name,
        Specialty = specialty,
        Image = $"{PortraitPrefix}{portrait}.webp",
        Biography = biography,
        Services = services.Where(s => directions.Contains(s.Name)).ToList()
    };
}
