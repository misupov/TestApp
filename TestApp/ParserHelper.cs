using System;
using System.Linq;
using Pidgin;
using static Pidgin.Parser;

namespace TestApp
{
    /// Формат строки:
    ///     yyyy.MM.dd w HH:mm:ss.fff
    ///     yyyy.MM.dd HH:mm:ss.fff
    ///     HH:mm:ss.fff
    ///     yyyy.MM.dd w HH:mm:ss
    ///     yyyy.MM.dd HH:mm:ss
    ///     HH:mm:ss
    /// Где yyyy - год (2000-2100)
    ///     MM - месяц (1-12)
    ///     dd - число месяца (1-31 или 32). 32 означает последнее число месяца
    ///     w - день недели (0-6). 0 - воскресенье, 6 - суббота
    ///     HH - часы (0-23)
    ///     mm - минуты (0-59)
    ///     ss - секунды (0-59)
    ///     fff - миллисекунды (0-999). Если не указаны, то 0
    public static class ParserHelper
    {
        public static Parser<char, Unit> Asterisk { get; } = Char('*').Map(_ => Unit.Value);
        public static Parser<char, int> NumberParser { get; } = Digit.AtLeastOnce().Map(s => int.Parse(new string(s.ToArray())));

        public static Parser<char, (int begin, int? end)> IntervalParser { get; } =
            from begin in NumberParser
            from end in Try(Char('-').Then(NumberParser).Optional())
                .Map(MapMaybeStruct)
            select (begin, end);

        public static Parser<char, ScheduleFormatEntry> WholeIntervalParser { get; } =
            from interval in
                Try(Asterisk.Map(_ => (begin: default(int?), end: default(int?))))
                    .Or(IntervalParser.Map(x => ((int?) x.begin, x.end)))
            from step in Try(Char('/').Then(NumberParser).Optional())
                .Map(MapMaybeStruct)
            select new ScheduleFormatEntry(interval.begin, interval.end, step);

        public static Parser<char, ScheduleFormatEntry[]> IntervalsSequenceParser { get; } =
            WholeIntervalParser.SeparatedAndOptionallyTerminatedAtLeastOnce(Char(',')).Map(x => x.ToArray())
                .SelectMany(ValidateWildcards(), ((entries, _) => entries));

        public static Parser<char, ScheduleDate> DateParser { get; } =
            from years in Validate(IntervalsSequenceParser, ValidateBoundsParser("Year", 2000, 2100))
            from _ in Char('.')
            from months in Validate(IntervalsSequenceParser, ValidateBoundsParser("Month", 1, 12))
            from __ in Char('.')
            from days in Validate(IntervalsSequenceParser, ValidateBoundsParser("Day", 1, 32))
            select new ScheduleDate(years, months, days);

        public static Parser<char, ScheduleFormatEntry[]> DayOfWeekParser { get; } =
            Validate(IntervalsSequenceParser, ValidateBoundsParser("Day of week", 0, 6));

        public static Parser<char, ScheduleTime> TimeParser { get; } =
            from hours in Validate(IntervalsSequenceParser, ValidateBoundsParser("Jour", 0, 23))
            from _ in Char(':')
            from min in Validate(IntervalsSequenceParser, ValidateBoundsParser("Min", 0, 59))
            from __ in Char(':')
            from sec in Validate(IntervalsSequenceParser, ValidateBoundsParser("Sec", 0, 59))
            from millis in Try(Char('.').Then(Validate(IntervalsSequenceParser, ValidateBoundsParser("Millis", 0, 999))).Optional())
                .Map(MapMaybe)
            select new ScheduleTime(hours, min, sec, millis ?? new[] {new ScheduleFormatEntry(0, null, null)});

        public static Parser<char, ScheduleFormat> FullFormatParser { get; } =
            from date in Try(DateParser).Before(Char(' ')).Optional().Map(MapMaybe)
            from dayOfWeek in Try(DayOfWeekParser.Before(Char(' '))).Optional().Map(MapMaybe)
            from time in TimeParser
            select new ScheduleFormat(date, dayOfWeek, time);

        private static Parser<char, ScheduleFormatEntry[]> Validate(Parser<char, ScheduleFormatEntry[]> parser,
            Func<ScheduleFormatEntry[], Parser<char, Unit>> check) =>
            parser.SelectMany(check, (entries, _) => entries);

        private static Func<ScheduleFormatEntry[], Parser<char, Unit>> ValidateWildcards() =>
            entries =>
            {
                if (entries.Length > 1 && entries.Any(x => x == new ScheduleFormatEntry(null, null, null)))
                {
                    return Parser<char>.Fail<Unit>(
                        $"Cannot have more than one wildcard entry in schedule");
                }
                
                return Parser<char>.Return(Unit.Value);
            };
        
        private static Func<ScheduleFormatEntry[], Parser<char, Unit>> ValidateBoundsParser(string formatPart, int min, int max) =>
            entries =>
            {
                foreach (var x in entries)
                {
                    if (x.Begin < min || x.Begin > max || x.End < x.Begin || x.End > max)
                    {
                        return Parser<char>.Fail<Unit>(
                            $"{formatPart} component ({x.Begin}, {x.End}) is out of bounds ({min}, {max})");
                    }
                }

                return Parser<char>.Return(Unit.Value);
            };

        private static T? MapMaybe<T>(Maybe<T> maybe) where T : class =>
            maybe.HasValue ? maybe.GetValueOrDefault() : null;
        
        private static T? MapMaybeStruct<T>(Maybe<T> maybe) where T : struct =>
            maybe.HasValue ? maybe.GetValueOrDefault() : null;
    }
}