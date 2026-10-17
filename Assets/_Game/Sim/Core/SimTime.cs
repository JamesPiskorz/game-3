namespace Game.Sim
{
    /// <summary>Game time: minutes since 00:00 on the first Monday. Days of the week run 0 (Monday) to 6 (Sunday).</summary>
    public readonly struct SimTime
    {
        public const int MinutesPerDay = 24 * 60;
        static readonly string[] DayNames = { "Monday", "Tuesday", "Wednesday", "Thursday", "Friday", "Saturday", "Sunday" };

        public readonly long Minutes;

        public SimTime(long minutes) => Minutes = minutes;

        public static SimTime At(int day, int hour, int minute = 0) => new SimTime((long)day * MinutesPerDay + hour * 60 + minute);

        public int Day => (int)(Minutes / MinutesPerDay);
        public int DayOfWeek => Day % 7;
        public int Hour => (int)(Minutes % MinutesPerDay / 60);
        public int Minute => (int)(Minutes % 60);
        public bool IsWeekend => DayOfWeek >= 5;

        public SimTime Plus(int minutes) => new SimTime(Minutes + minutes);

        public override string ToString() => $"{DayNames[DayOfWeek]} (day {Day + 1}) {Hour:00}:{Minute:00}";
    }
}
