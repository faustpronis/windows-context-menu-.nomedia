using System;
using System.Globalization;

namespace FolderTools.Core
{
    /// <summary>
    /// Formats the header line written into a .nomedia file.
    /// Weekday names are hard-coded so the output does not depend on the
    /// regional settings of the machine that runs the tool.
    /// </summary>
    public static class NomediaFormatter
    {
        // Indexed by DateTime.DayOfWeek: Sunday = 0.
        private static readonly string[] RussianWeekdays =
            { "Вс", "Пн", "Вт", "Ср", "Чт", "Пт", "Сб" };

        /// <summary>Example: <c>18:53:23  18.11.2026 Ср</c> (two spaces between time and date).</summary>
        public static string FormatTimestamp(DateTime when)
        {
            string weekday = RussianWeekdays[(int)when.DayOfWeek];
            return string.Format(
                CultureInfo.InvariantCulture,
                "{0:HH:mm:ss}  {0:dd.MM.yyyy} {1}",
                when,
                weekday);
        }
    }
}
