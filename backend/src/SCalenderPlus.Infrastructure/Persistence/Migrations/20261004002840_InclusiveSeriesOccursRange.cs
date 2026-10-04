using Microsoft.EntityFrameworkCore.Migrations;
using NodaTime;

#nullable disable

namespace SCalenderPlus.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InclusiveSeriesOccursRange : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<Interval>(
                name: "occurs_range",
                table: "events",
                type: "tstzrange",
                nullable: false,
                computedColumnSql: "tstzrange(CASE WHEN rrule IS NULL THEN start_utc ELSE least(start_utc, series_start_utc) END, CASE WHEN rrule IS NULL THEN end_utc ELSE coalesce(series_until_utc, 'infinity'::timestamptz) END, CASE WHEN rrule IS NOT NULL OR end_utc = start_utc THEN '[]' ELSE '[)' END)",
                stored: true,
                oldClrType: typeof(Interval),
                oldType: "tstzrange",
                oldComputedColumnSql: "tstzrange(CASE WHEN rrule IS NULL THEN start_utc ELSE least(start_utc, series_start_utc) END, CASE WHEN rrule IS NULL THEN end_utc ELSE coalesce(series_until_utc, 'infinity'::timestamptz) END, CASE WHEN rrule IS NULL AND end_utc = start_utc THEN '[]' ELSE '[)' END)",
                oldStored: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<Interval>(
                name: "occurs_range",
                table: "events",
                type: "tstzrange",
                nullable: false,
                computedColumnSql: "tstzrange(CASE WHEN rrule IS NULL THEN start_utc ELSE least(start_utc, series_start_utc) END, CASE WHEN rrule IS NULL THEN end_utc ELSE coalesce(series_until_utc, 'infinity'::timestamptz) END, CASE WHEN rrule IS NULL AND end_utc = start_utc THEN '[]' ELSE '[)' END)",
                stored: true,
                oldClrType: typeof(Interval),
                oldType: "tstzrange",
                oldComputedColumnSql: "tstzrange(CASE WHEN rrule IS NULL THEN start_utc ELSE least(start_utc, series_start_utc) END, CASE WHEN rrule IS NULL THEN end_utc ELSE coalesce(series_until_utc, 'infinity'::timestamptz) END, CASE WHEN rrule IS NOT NULL OR end_utc = start_utc THEN '[]' ELSE '[)' END)",
                oldStored: true);
        }
    }
}
