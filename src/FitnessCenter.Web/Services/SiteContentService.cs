using System.Data;
using System.Net.Mail;
using System.Security.Claims;
using FitnessCenter.Web.Data;
using Microsoft.EntityFrameworkCore;

namespace FitnessCenter.Web.Services;

public enum SiteFieldKind { Text, Paragraph, Email, HttpsLink, ImagePath, ActionLink }
public record SiteField(string Key, string Label, string Default = "", SiteFieldKind Kind = SiteFieldKind.Text, bool Required = false);
public record SiteSection(string Label, IReadOnlyList<SiteField> Fields);
public record ClubBanner(string Label, string Title, string Subtitle, string Description, string Action, string Href,
    string Image, string? Style = null, string? Underlay = null);

public sealed class SiteContent(IReadOnlyDictionary<string, string> values)
{
    public string this[string key] => values.GetValueOrDefault(key) ?? "";
    public string? HttpsLink(string key) =>
        Uri.TryCreate(this[key], UriKind.Absolute, out var url) && url.Scheme == Uri.UriSchemeHttps ? url.AbsoluteUri : null;
}

public sealed class SiteContentService(IDbContextFactory<ApplicationDbContext> factory, IConfiguration configuration, PublicDataCache? cache = null)
{
    private const string Images = "/images/figma/";
    private const string Intro = "Тренажёрный зал, бассейн и студийные занятия. Выбери тренировку по своему темпу.";
    public static IReadOnlyList<ClubBanner> Banners { get; } =
    [
        new("", "Твой ритм.", "Твоя сила.", Intro, "Выбрать тренировку", "#quick-booking", Images + "slide-gym.webp"),
        new("Демо-акция для новых клиентов", "Скидка 45%", "На тестовую неделю", Intro,
            "Узнать условия", "/contacts", Images + "slide-intro.webp", "figma-slide-offer"),
        new("", "Вайбы", "для олдов", "Вайбы «подвальной качалки» в специальном зале.",
            "Посмотреть зал", "/services", Images + "slide-weights.webp", Underlay: Images + "slide-weights-base.webp"),
        new("Демо-событие Пульс.Club", "Потогонка!", "Новый сезон!", "Ежегодное соревнование клуба на выносливость.",
            "Узнать о событии", "/contacts", Images + "slide-challenge.webp", "figma-slide-challenge"),
        new("Предупреждение!", "Внимание!", "Блины не есть!", "Без сгущёнки…",
            "Я понял, хорошо", "", Images + "slide-plates.webp", "figma-slide-plates")
    ];

    public static IReadOnlyList<SiteSection> Sections { get; } =
    [
        new("Контакты и настройки", [
            new("Club:City", "Город", "Ижевск", Required: true),
            new("Club:Address", "Адрес", Required: true),
            new("Club:TransitNote", "Как добраться"),
            new("Club:Phone", "Телефон для звонков"),
            new("Club:DemoPhone", "Телефон в макете"),
            new("Club:Email", "Email для связи", Kind: SiteFieldKind.Email),
            new("Club:ContactLabel", "Подпись кнопки связи"),
            new("Club:Hours", "Режим работы", Required: true),
            new("Club:MapUrl", "Ссылка на виджет карты", Kind: SiteFieldKind.HttpsLink),
            new("Club:DirectionsUrl", "Ссылка на маршрут", Kind: SiteFieldKind.HttpsLink),
            new("Club:VkUrl", "ВКонтакте", Kind: SiteFieldKind.HttpsLink),
            new("Club:TelegramUrl", "Telegram", Kind: SiteFieldKind.HttpsLink),
            new("Club:WhatsAppUrl", "WhatsApp", Kind: SiteFieldKind.HttpsLink),
            new("Club:InstagramUrl", "Instagram", Kind: SiteFieldKind.HttpsLink)
        ]),
        new("О фитнес-центре", [
            new("About:Title", "Заголовок страницы", "Место для твоего движения.", Required: true),
            new("About:Lead", "Вступление", "Пульс объединяет зал, бассейн и студийные занятия в одном пространстве. Здесь можно начать с нуля и найти свой ритм.", SiteFieldKind.Paragraph),
            new("About:HistoryTitle", "Заголовок истории", "Как появился «Пульс»"),
            new("About:History", "История", "В основе концепции клуба была идея собрать разные виды тренировок под одной крышей. Так появились силовая зона, студия групповых занятий и бассейн.", SiteFieldKind.Paragraph),
            new("About:MissionTitle", "Заголовок миссии", "Сделать движение привычкой"),
            new("About:Mission", "Миссия", "Мы помогаем выбрать нагрузку, которая подходит твоей цели и уровню подготовки. На первом занятии можно познакомиться с тренером и освоить базовую технику.", SiteFieldKind.Paragraph),
            new("About:SpaceTitle", "Заголовок инфраструктуры", "Зал, студия и бассейн"),
            new("About:Space", "Инфраструктура", "В зале есть свободные веса и зона функционального тренинга. В студии проходят занятия йогой и пилатесом, а в бассейне можно заниматься плаванием с инструктором.", SiteFieldKind.Paragraph),
            new("About:TeamTitle", "Заголовок команды", "Люди, которые рядом"),
            new("About:Team", "Команда", "У каждого тренера своя специализация. В профилях можно узнать об опыте, выбрать направление и посмотреть расписание.", SiteFieldKind.Paragraph),
            new("About:HomeLead", "Первый абзац на главной", "Мы хотим, чтобы движение стало привычной частью жизни. В «Пульсе» можно заниматься в тренажёрном зале, плавать или ходить на занятия в студии.", SiteFieldKind.Paragraph),
            new("About:HomeTeam", "Второй абзац на главной", "Тренеры специализируются на силовой подготовке, йоге, пилатесе и плавании. Они помогут освоить технику и подобрать нагрузку под твой уровень.", SiteFieldKind.Paragraph)
        ]),
        new("Блоки главной и подвал", [
            new("Home:ServicesHeading", "Услуги · начало заголовка", "Найди своё"),
            new("Home:ServicesAccent", "Услуги · выделенное слово", "движение"),
            new("Home:AboutHeading", "О клубе · первая строка заголовка", "Место для"),
            new("Home:AboutLine2", "О клубе · начало второй строки", "твоего"),
            new("Home:AboutAccent", "О клубе · выделенное слово", "движения"),
            new("Home:TrainersHeading", "Заголовок блока тренеров", "Наша команда", Required: true),
            new("Home:ReviewsHeading", "Заголовок блока отзывов", "Отзывы клиентов", Required: true),
            new("Home:BookingHeading", "Заголовок блока записи", "Запись на тренировку", Required: true),
            new("Home:BookingLead", "Подпись к форме записи", "Выбери день, тренера и свободное время."),
            new("Home:AccountTitle", "Личный кабинет · заголовок", "С личным кабинетом удобнее", Required: true),
            new("Home:AccountLead", "Личный кабинет · описание", "Записывайся на тренировки и следи за своими занятиями в одном месте.", SiteFieldKind.Paragraph),
            new("Home:AccountAction", "Личный кабинет · текст кнопки", "Открыть личный кабинет", Required: true),
            new("Home:AccountScheduleTitle", "Расписание · заголовок", "Быстрый доступ к расписанию и записи"),
            new("Home:AccountScheduleText", "Расписание · описание", "Выбирай тренировку, тренера и свободное время.", SiteFieldKind.Paragraph),
            new("Home:AccountHistoryTitle", "История занятий · заголовок", "История занятий и уведомления"),
            new("Home:AccountHistoryText", "История занятий · описание", "Смотри статус своих записей в личном кабинете.", SiteFieldKind.Paragraph),
            new("Footer:Note", "Примечание в подвале", "Учебный проект. Данные клуба и отзывы демонстрационные.")
        ]),
        new("Команда проекта", [
            new("Team:Title", "Заголовок", "Кто делает Пульс", Required: true),
            new("Team:Lead", "Вступление", "Сайт фитнес-центра по техническому заданию, вариант 9.", SiteFieldKind.Paragraph),
            new("Team:Author", "Участник 1", "Автор проекта"),
            new("Team:AuthorWork", "Задачи участника 1", "Требования, выбор референса, обратная связь и приёмка результата.", SiteFieldKind.Paragraph),
            new("Team:Developer", "Участник 2", "Codex"),
            new("Team:DeveloperWork", "Задачи участника 2", "Реализация приложения на C#, база данных, интерфейс и проверка сценариев.", SiteFieldKind.Paragraph),
            new("Team:Design", "Участник 3", "GPT Image"),
            new("Team:DesignWork", "Задачи участника 3", "Изображения зала, бассейна и вымышленных тренеров.", SiteFieldKind.Paragraph),
            new("Team:Tools", "Технологии", "C#, ASP.NET Core, Blazor, Entity Framework Core, SQL Server и ASP.NET Core Identity.", SiteFieldKind.Paragraph),
            new("Team:Note", "Примечание", "Клуб, услуги, тренеры и опубликованные примеры отзывов созданы для демонстрации.", SiteFieldKind.Paragraph)
        ]),
        .. Banners.Select((banner, i) => new SiteSection($"Баннер {i + 1}", [
            new($"Banner:{i}:Label", "Подпись", banner.Label),
            new($"Banner:{i}:Title", "Заголовок", banner.Title, Required: true),
            new($"Banner:{i}:Subtitle", "Подзаголовок", banner.Subtitle),
            new($"Banner:{i}:Description", "Текст", banner.Description, SiteFieldKind.Paragraph),
            new($"Banner:{i}:Action", "Подпись кнопки", banner.Action, Required: true),
            new($"Banner:{i}:Href", "Адрес кнопки", banner.Href, SiteFieldKind.ActionLink),
            new($"Banner:{i}:Image", "Путь к изображению", banner.Image, SiteFieldKind.ImagePath, Required: true)
        ]))
    ];

    public SiteContent Defaults => new(DefaultValues());

    public Task<SiteContent> ReadAsync() => cache?.SiteAsync(ReadFromDatabaseAsync) ?? ReadFromDatabaseAsync();

    private async Task<SiteContent> ReadFromDatabaseAsync()
    {
        await using var db = await factory.CreateDbContextAsync();
        var values = DefaultValues();
        foreach (var row in await db.SiteTexts.AsNoTracking().ToListAsync())
            if (values.ContainsKey(row.Key)) values[row.Key] = row.Value;
        return new(values);
    }

    public async Task<OperationResult> SaveAsync(ClaimsPrincipal actor, int sectionIndex, IReadOnlyDictionary<string, string> input, byte[]? imageContent = null)
    {
        await using var db = await factory.CreateDbContextAsync();
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        await StaffService.RequireAsync(actor, db, true);
        if (sectionIndex < 0 || sectionIndex >= Sections.Count) return new(false, "Раздел не найден.");
        var fields = Sections[sectionIndex].Fields;
        var keys = fields.Select(f => f.Key).ToArray();
        var rows = await db.SiteTexts.Where(t => keys.Contains(t.Key)).ToDictionaryAsync(t => t.Key);
        CatalogImage? image = null;
        if (imageContent is not null)
        {
            var contentType = CatalogImage.DetectContentType(imageContent);
            if (contentType is null || !fields.Any(f => f.Kind == SiteFieldKind.ImagePath))
                return new(false, "Выберите изображение JPG, PNG или WebP до 5 МБ.");
            image = new() { Content = imageContent, ContentType = contentType };
            db.CatalogImages.Add(image);
        }
        foreach (var field in fields)
        {
            var value = field.Kind == SiteFieldKind.ImagePath && image is not null
                ? $"/images/catalog/{image.Id:D}"
                : input.GetValueOrDefault(field.Key)?.Trim() ?? "";
            if (!IsValid(field, value)) return new(false, $"Проверьте поле «{field.Label}».");
            if (rows.TryGetValue(field.Key, out var row)) row.Value = value;
            else db.SiteTexts.Add(new() { Key = field.Key, Value = value });
        }
        try
        {
            await db.SaveChangesAsync();
            await tx.CommitAsync();
            cache?.InvalidateSite();
            return new(true, "Изменения опубликованы.");
        }
        catch (Exception ex) when (BookingService.IsConflict(ex)) { return new(false, "Настройки изменились. Повторите сохранение."); }
    }

    private Dictionary<string, string> DefaultValues() => Sections.SelectMany(s => s.Fields)
        .ToDictionary(f => f.Key, f => configuration[f.Key] ?? f.Default, StringComparer.Ordinal);

    private static bool IsValid(SiteField field, string value)
    {
        if (value.Length == 0) return !field.Required;
        if (value.Length > (field.Kind == SiteFieldKind.Text ? 200 : 2000) || value.Any(char.IsControl) && field.Kind != SiteFieldKind.Paragraph) return false;
        return field.Kind switch
        {
            SiteFieldKind.HttpsLink => Uri.TryCreate(value, UriKind.Absolute, out var url) && url.Scheme == Uri.UriSchemeHttps,
            SiteFieldKind.Email => value.Length <= 254 && MailAddress.TryCreate(value, out var address) && address.Address == value,
            SiteFieldKind.ImagePath => value.StartsWith("/images/", StringComparison.Ordinal) && !value.Contains("..")
                && value.All(c => char.IsAsciiLetterOrDigit(c) || c is '/' or '-' or '_' or '.'),
            SiteFieldKind.ActionLink => value.StartsWith('#') || value.StartsWith('/') && !value.StartsWith("//", StringComparison.Ordinal) && !value.Contains('\\'),
            _ => true
        };
    }
}
