using FitnessCenter.Web.Services;
using Microsoft.EntityFrameworkCore;

namespace FitnessCenter.Web.Data;

internal static class DemoReviews
{
    private const string AuthorPrefix = "pulse-demo-review-";
    internal const string FirstReviewText = "Понравилось, что тренер сначала объяснил технику и помог подобрать нагрузку. Просторный зал, удобно записываться на занятие.";

    private sealed record Sample(string Key, string Author, string Service, int Rating, string Text);

    private static readonly Sample[] Samples =
    [
        new("marina", "Марина", "Плавание", 5, "Раньше быстро сбивалась с дыхания в воде. Юрий показал упражнение у бортика, а потом мы попробовали его на дорожке. К концу занятия уже проплыла дорожку спокойнее."),
        new("ilya", "Илья", "Функциональный тренинг", 5, "Думал, что на первой круговой тренировке буду отставать. Александра предложила облегчённый вариант пары упражнений, и я прошёл весь круг в своём темпе."),
        new("daria", "Дарья", "Йога", 5, "Пришла на йогу впервые. Ольга показывала, как упростить позы, и оставляла время, чтобы в них освоиться. Понравилось, что никто никуда не торопился."),
        new("mikhail", "Михаил", "Тренажёрный зал", 4, "В зале удобно заниматься со свободными весами. Вечером пришлось немного подождать скамью, зато Виталик подсказал, каким упражнением занять паузу."),
        new("alina", "Алина", "Пилатес", 5, "Ольга объяснила, как дышать во время упражнений, и помогла поправить положение спины. На коврике нагрузка оказалась ощутимее, чем я ожидала."),
        new("nikita", "Никита", "Персональная тренировка", 5, "На занятии разобрали присед по шагам. Когда поменял постановку стоп, стало легче держать равновесие. Теперь понимаю, за чем следить, если тренируюсь сам."),
        new("ekaterina", "Екатерина", "Плавание", 4, "В бассейне светло и хорошо видно разметку дорожки. Вечером было многолюдно, но на занятии с тренером успела потренировать поворот и дыхание."),
        new("pavel", "Павел", "Тренажёрный зал", 5, "Возвращаюсь к тренировкам после перерыва. Начали с небольших весов и подобрали несколько упражнений на неделю. Ушёл с понятным планом, что делать в следующий раз."),
        new("sofia", "София", "Функциональный тренинг", 5, "Больше всего понравился блок на координацию. Задания менялись, и время пролетело незаметно. На сложных станциях тренер успевала помочь с техникой."),
        new("vera", "Вера", "Пилатес", 5, "Выбрала пилатес после рабочего дня. Понравились упражнения на подвижность и спокойный темп. Инструктор объясняла, где держать усилие и когда расслабиться."),
        new("roman", "Роман", "Персональная тренировка", 4, "Тренировка помогла разобраться с техникой тяги. Хотелось побольше времени на разминку, но основные упражнения успели спокойно разобрать и повторить.")
    ];

    public static bool IsSampleReview(Review review) =>
        review.Booking.UserId.StartsWith(AuthorPrefix, StringComparison.Ordinal) || review.Text == FirstReviewText;

    public static async Task SeedAsync(ApplicationDbContext db, ClubClock clock)
    {
        var authorIds = Samples.Select(sample => AuthorPrefix + sample.Key).ToArray();
        if (await db.Users.CountAsync(user => authorIds.Contains(user.Id)) == Samples.Length) return;

        await using var transaction = await db.Database.BeginTransactionAsync();
        await db.Database.ExecuteSqlRawAsync("""
            DECLARE @result int;
            EXEC @result = sp_getapplock @Resource = 'PulseFitness.DemoReviews',
                @LockMode = 'Exclusive', @LockOwner = 'Transaction', @LockTimeout = 10000;
            IF @result < 0 THROW 51000, 'Unable to lock demo reviews.', 1;
            """);

        var seededAuthors = await db.Users.Where(user => authorIds.Contains(user.Id))
            .Select(user => user.Id).ToListAsync();
        var serviceNames = Samples.Select(sample => sample.Service).Distinct().ToArray();
        var services = await db.Services.AsNoTracking()
            .Where(service => service.IsActive && serviceNames.Contains(service.Name))
            .Include(service => service.Trainers.Where(trainer => trainer.IsActive))
            .OrderBy(service => service.Id).ToListAsync();
        var today = clock.Now.Date;

        for (var index = 0; index < Samples.Length; index++)
        {
            var sample = Samples[index];
            var authorId = AuthorPrefix + sample.Key;
            if (seededAuthors.Contains(authorId)) continue;

            var service = services.FirstOrDefault(service => service.Name == sample.Service && service.Trainers.Count > 0);
            if (service is null) continue;
            var trainer = service.Trainers.MinBy(trainer => trainer.Id)!;
            var start = clock.Utc(today.AddDays(-index - 1).AddHours(8));
            var end = start.AddMinutes(service.DurationMinutes);
            db.Reviews.Add(new()
            {
                Rating = sample.Rating,
                Text = sample.Text,
                IsPublished = true,
                IsFeatured = true,
                CreatedAtUtc = end.AddHours(2),
                Booking = new()
                {
                    User = new() { Id = authorId, FullName = sample.Author },
                    Status = BookingStatus.Attended,
                    CreatedAtUtc = start.AddDays(-1),
                    Session = new()
                    {
                        ServiceId = service.Id,
                        TrainerId = trainer.Id,
                        StartsAtUtc = start,
                        EndsAtUtc = end,
                        Capacity = 1
                    }
                }
            });
        }

        await db.SaveChangesAsync();
        await transaction.CommitAsync();
    }
}
