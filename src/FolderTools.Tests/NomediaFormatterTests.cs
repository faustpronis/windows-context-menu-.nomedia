using System;
using FolderTools.Core;
using Xunit;

namespace FolderTools.Tests
{
    public class NomediaFormatterTests
    {
        [Theory]
        [InlineData(2026, 11, 15, "Вс")]
        [InlineData(2026, 11, 16, "Пн")]
        [InlineData(2026, 11, 17, "Вт")]
        [InlineData(2026, 11, 18, "Ср")]
        [InlineData(2026, 11, 20, "Пт")]
        [InlineData(2026, 11, 21, "Сб")]
        [InlineData(2026, 11, 22, "Вс")]
        public void FormatTimestamp_UsesHardCodedRussianWeekdays(int year, int month, int day, string weekday)
        {
            DateTime when = new DateTime(year, month, day, 18, 53, 23);
            Assert.EndsWith(" " + weekday, NomediaFormatter.FormatTimestamp(when));
        }

        [Fact]
        public void FormatTimestamp_MatchesRequestedLayout()
        {
            DateTime when = new DateTime(2026, 11, 18, 18, 53, 23);
            Assert.Equal("18:53:23  18.11.2026 Ср", NomediaFormatter.FormatTimestamp(when));
        }

        [Fact]
        public void FormatTimestamp_IsIndependentOfCurrentCulture()
        {
            DateTime when = new DateTime(2026, 11, 18, 6, 5, 4);
            string expected = "06:05:04  18.11.2026 Ср";

            var previous = System.Globalization.CultureInfo.CurrentCulture;
            try
            {
                foreach (string name in new[] { "en-US", "ja-JP", "de-DE" })
                {
                    System.Globalization.CultureInfo culture;
                    try
                    {
                        culture = new System.Globalization.CultureInfo(name);
                    }
                    catch (System.Globalization.CultureNotFoundException)
                    {
                        // Globalization-invariant environments (e.g. a Linux build
                        // agent without ICU) only know the invariant culture.
                        continue;
                    }

                    System.Globalization.CultureInfo.CurrentCulture = culture;
                    Assert.Equal(expected, NomediaFormatter.FormatTimestamp(when));
                }
            }
            finally
            {
                System.Globalization.CultureInfo.CurrentCulture = previous;
            }
        }
    }
}
