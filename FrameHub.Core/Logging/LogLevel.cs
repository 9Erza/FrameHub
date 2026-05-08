namespace FrameHub.Core.Logging
{
    /// <summary>
    /// Log levels ordered from most verbose to most severe.
    /// </summary>
    public class LogLevel : ILogLevel
    {
        private static readonly string[] EnglishNames = { "Debug", "Info", "Warn", "Error", "Fatal" };

        public static readonly ILogLevel Debug = new LogLevel(0, EnglishNames[0]);
        public static readonly ILogLevel Info = new LogLevel(1, EnglishNames[1]);
        public static readonly ILogLevel Warn = new LogLevel(2, EnglishNames[2]);
        public static readonly ILogLevel Error = new LogLevel(3, EnglishNames[3]);
        public static readonly ILogLevel Fatal = new LogLevel(4, EnglishNames[4]);

        private LogLevel(int level, string name)
        {
            Level = level;
            Name = name;
        }

        public string Name { get; }
        public int Level { get; }

        /// <summary>
        /// Returns true when an incoming log message should be written for the current minimum level.
        /// Example: current Info allows Info/Warn/Error/Fatal but blocks Debug.
        /// </summary>
        public bool IsEnabled(ILogLevel other) => other.Level >= Level;

        public static ILogLevel FromValue(int value)
        {
            return value switch
            {
                0 => Debug,
                1 => Info,
                2 => Warn,
                3 => Error,
                4 => Fatal,
                _ => Info
            };
        }

        public static string GetEnglishName(int level)
        {
            return level >= 0 && level < EnglishNames.Length ? EnglishNames[level] : EnglishNames[1];
        }

        public static string GetPolishName(int level) => GetEnglishName(level);
    }
}
