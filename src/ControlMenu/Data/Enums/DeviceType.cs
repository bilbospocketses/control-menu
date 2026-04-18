namespace ControlMenu.Data.Enums;

// Persisted as string via EF (see AppDbContext), so adding new values is safe —
// existing rows continue to round-trip on their string name.
public enum DeviceType
{
    GoogleTV,
    AndroidPhone,
    AndroidTablet,
    AndroidWatch,
}
