using EvCharging.Domain.Charging;
using EvCharging.Domain.Reservations;
using EvCharging.Persistence.Entities;
using Npgsql;
using Npgsql.EntityFrameworkCore.PostgreSQL.Infrastructure;
using Npgsql.NameTranslation;

namespace EvCharging.Persistence.Extensions;

public static class PostgresEnumExtensions
{
    private static readonly NpgsqlSnakeCaseNameTranslator Names = new();
    private static readonly Type[] EnumTypes =
    [
        typeof(ReservationState),
        typeof(SessionLifecycle),
        typeof(CommandOutcome),
        typeof(ChargingCommandKind),
        typeof(FileKind),
        typeof(FileState),
        typeof(ReservationNotificationKind),
        typeof(NotificationDeliveryOutcome)
    ];

    public static NpgsqlDbContextOptionsBuilder MapPlatformEnums(this NpgsqlDbContextOptionsBuilder options)
    {
        foreach (var type in EnumTypes)
        {
            options.MapEnum(type, Names.TranslateTypeName(type.Name), PlatformDbContext.Schema, Names);
        }

        return options;
    }

    public static NpgsqlDataSourceBuilder MapPlatformEnums(this NpgsqlDataSourceBuilder builder)
    {
        foreach (var type in EnumTypes)
        {
            builder.MapEnum(type, $"{PlatformDbContext.Schema}.{Names.TranslateTypeName(type.Name)}", Names);
        }

        return builder;
    }
}
