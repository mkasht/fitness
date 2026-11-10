namespace FitnessCenter.Web.Services;

public sealed class ClubClock(IConfiguration configuration)
{
    public TimeZoneInfo Zone { get; } = TimeZoneInfo.FindSystemTimeZoneById(configuration["Club:TimeZone"] ?? "Europe/Samara");
    public DateTime Now => Local(DateTime.UtcNow);
    public DateTime Local(DateTime utc) => TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utc, DateTimeKind.Utc), Zone);
    public DateTime Utc(DateTime local) => TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(local, DateTimeKind.Unspecified), Zone);
}
