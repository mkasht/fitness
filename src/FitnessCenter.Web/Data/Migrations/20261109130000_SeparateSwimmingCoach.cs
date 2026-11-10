using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace FitnessCenter.Web.Data.Migrations;

[DbContext(typeof(ApplicationDbContext))]
[Migration("20261109130000_SeparateSwimmingCoach")]
public sealed class SeparateSwimmingCoach : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
        DECLARE @now datetime2 = SYSUTCDATETIME();
        DECLARE @changed TABLE (Id int PRIMARY KEY, IsCancelled bit NOT NULL);

        UPDATE session
        SET TrainerId = COALESCE(replacement.Id, session.TrainerId),
            IsCancelled = CASE WHEN replacement.Id IS NULL THEN 1 ELSE 0 END
        OUTPUT inserted.Id, inserted.IsCancelled INTO @changed
        FROM Sessions AS session
        JOIN Trainers AS original ON original.Id = session.TrainerId
        JOIN Services AS service ON service.Id = session.ServiceId
        OUTER APPLY (
            SELECT TOP (1) coach.Id
            FROM Trainers AS coach
            JOIN FitnessServiceTrainer AS assignment ON assignment.TrainersId = coach.Id
            WHERE coach.Name = N'Илья Морозов' AND coach.IsActive = 1
                AND assignment.ServicesId = session.ServiceId
                AND NOT EXISTS (
                    SELECT 1 FROM Sessions AS other
                    WHERE other.TrainerId = coach.Id AND other.IsCancelled = 0
                        AND other.StartsAtUtc < session.EndsAtUtc AND other.EndsAtUtc > session.StartsAtUtc
                )
            ORDER BY coach.Id
        ) AS replacement
        WHERE original.Name = N'Артём Соколов' AND service.Name = N'Плавание'
            AND session.StartsAtUtc > @now AND session.IsCancelled = 0;

        -- Notify clients with pending or confirmed bookings, preserving the original time.
        INSERT INTO Notifications (UserId, Text, CreatedAtUtc, IsRead)
        SELECT booking.UserId,
            CASE WHEN changed.IsCancelled = 1
                THEN N'Клуб отменил занятие по плаванию. Выберите другое время в расписании.'
                ELSE N'Занятие по плаванию проведёт Илья Морозов. Время занятия прежнее.'
            END, @now, 0
        FROM Bookings AS booking
        JOIN @changed AS changed ON changed.Id = booking.SessionId
        WHERE booking.Status IN (0, 1);

        UPDATE booking SET Status = 3
        FROM Bookings AS booking
        JOIN @changed AS changed ON changed.Id = booking.SessionId
        WHERE changed.IsCancelled = 1 AND booking.Status IN (0, 1);

        DELETE assignment
        FROM FitnessServiceTrainer AS assignment
        JOIN Trainers AS coach ON coach.Id = assignment.TrainersId
        JOIN Services AS service ON service.Id = assignment.ServicesId
        WHERE coach.Name = N'Артём Соколов' AND service.Name = N'Плавание';
        """);

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        // Later bookings and schedule edits must survive a schema rollback.
    }
}
