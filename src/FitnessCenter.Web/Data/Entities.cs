using System.ComponentModel.DataAnnotations;

namespace FitnessCenter.Web.Data;

public class FitnessService
{
    public int Id { get; set; }
    [Required, MaxLength(100)] public string Name { get; set; } = "";
    [Required, MaxLength(100)] public string Category { get; set; } = "";
    [Required, MaxLength(300)] public string Summary { get; set; } = "";
    [Required, MaxLength(4000)] public string Description { get; set; } = "";
    [MaxLength(200)] public string Image { get; set; } = "/images/club.jpg";
    [Range(15, 180)] public int DurationMinutes { get; set; } = 60;
    [Range(0, 100000)] public decimal Price { get; set; }
    public bool IsActive { get; set; } = true;
    public List<Trainer> Trainers { get; set; } = [];
}

public class Trainer
{
    public int Id { get; set; }
    [Required, MaxLength(100)] public string Name { get; set; } = "";
    [Required, MaxLength(150)] public string Specialty { get; set; } = "";
    [Required, MaxLength(4000)] public string Biography { get; set; } = "";
    [MaxLength(200)] public string Image { get; set; } = "/images/trainer-male.jpg";
    [Range(0, 60)] public int ExperienceYears { get; set; }
    public bool IsActive { get; set; } = true;
    public List<FitnessService> Services { get; set; } = [];
}

public class TrainingSession
{
    public int Id { get; set; }
    public int ServiceId { get; set; }
    public FitnessService Service { get; set; } = null!;
    public int TrainerId { get; set; }
    public Trainer Trainer { get; set; } = null!;
    public DateTime StartsAtUtc { get; set; }
    public DateTime EndsAtUtc { get; set; }
    public int Capacity { get; set; } = 1;
    public bool IsCancelled { get; set; }
    [Timestamp] public byte[] RowVersion { get; set; } = [];
    public List<Booking> Bookings { get; set; } = [];
}

public enum BookingStatus { Pending, Confirmed, Rejected, Cancelled, Attended, NoShow }

public class Booking
{
    public int Id { get; set; }
    public string UserId { get; set; } = "";
    public ApplicationUser User { get; set; } = null!;
    public int SessionId { get; set; }
    public TrainingSession Session { get; set; } = null!;
    [MaxLength(1000)] public string Comment { get; set; } = "";
    public BookingStatus Status { get; set; } = BookingStatus.Pending;
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    [Timestamp] public byte[] RowVersion { get; set; } = [];
}

public class Review
{
    public int Id { get; set; }
    public int BookingId { get; set; }
    public Booking Booking { get; set; } = null!;
    [Range(1, 5)] public int Rating { get; set; } = 5;
    [Required, MaxLength(1500)] public string Text { get; set; } = "";
    public bool IsPublished { get; set; }
    public bool IsFeatured { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}

public class Notification
{
    public int Id { get; set; }
    public string UserId { get; set; } = "";
    [MaxLength(500)] public string Text { get; set; } = "";
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public bool IsRead { get; set; }
}

public class FeedbackMessage
{
    public int Id { get; set; }
    [Required(ErrorMessage = "Укажите имя"), MaxLength(100)] public string Name { get; set; } = "";
    [Required(ErrorMessage = "Укажите email"), EmailAddress(ErrorMessage = "Проверьте email"), MaxLength(200)] public string Email { get; set; } = "";
    [Required(ErrorMessage = "Напишите сообщение"), StringLength(2000, MinimumLength = 10, ErrorMessage = "От 10 до 2000 символов")] public string Text { get; set; } = "";
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public bool IsProcessed { get; set; }
}

public static class Roles
{
    public const string Admin = "Administrator";
    public const string Manager = "Manager";
    public const string User = "User";
    public const string Staff = Admin + "," + Manager;
}

public static class BookingLabels
{
    public static string Label(this BookingStatus status) => status switch
    {
        BookingStatus.Pending => "Ожидает подтверждения",
        BookingStatus.Confirmed => "Подтверждена",
        BookingStatus.Rejected => "Отклонена",
        BookingStatus.Cancelled => "Отменена",
        BookingStatus.Attended => "Посещено",
        BookingStatus.NoShow => "Неявка",
        _ => "Неизвестный статус"
    };
    public static bool IsActive(this BookingStatus status) => status is BookingStatus.Pending or BookingStatus.Confirmed;
}

