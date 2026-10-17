using System.Runtime.CompilerServices;
using System.Text;
using NodaTime;
using SCalenderPlus.Application.Import;

namespace SCalenderPlus.Application.Tests.Import;

/// <summary>Committed export fixtures (CRLF) and a helper to plan a file into a calendar in Europe/Berlin.</summary>
internal static class IcsImportFixtures
{
    public static readonly Guid CalendarId = Guid.Parse("0192f2a1-0000-7000-8000-000000000001");
    public static readonly Guid CreatorId = Guid.Parse("0192f2a1-0000-7000-8000-000000000002");
    public static readonly Instant Now = Instant.FromUtc(2026, 10, 17, 12, 0);

    public static string Directory([CallerFilePath] string source = "") => Path.Combine(Path.GetDirectoryName(source)!, "Fixtures");

    public static string Read(string name) => File.ReadAllText(Path.Combine(Directory(), name), Encoding.UTF8);

    public static IcsImportPlan Plan(string text, string calendarZone = "Europe/Berlin")
    {
        var roots = IcsReader.Read(text, IcsImportService.MaxEvents, out var error, out var detail);
        Assert.True(roots is not null, $"{error}: {detail}");
        return IcsImportPlanner.Plan(roots!, DateTimeZoneProviders.Tzdb[calendarZone], CalendarId, CreatorId, Now);
    }

    /// <summary>A minimal VCALENDAR around <paramref name="body"/> (lines joined with CRLF).</summary>
    public static string Calendar(params string[] body) =>
        string.Join("\r\n", ["BEGIN:VCALENDAR", "VERSION:2.0", "PRODID:-//test//EN", .. body, "END:VCALENDAR", string.Empty]);

    public static IcsImportItem Single(IcsImportPlan plan) => Assert.Single(plan.Items);

    public static IcsImportItem Item(IcsImportPlan plan, string uid) => Assert.Single(plan.Items, i => i.Uid == uid);
}
