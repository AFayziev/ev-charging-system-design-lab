using Microsoft.EntityFrameworkCore;

namespace EvCharging.Persistence;

public sealed class PlatformReadDbContext(DbContextOptions<PlatformReadDbContext> options) : PlatformDbContext(options);
