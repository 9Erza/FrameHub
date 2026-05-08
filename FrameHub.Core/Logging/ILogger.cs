using System;

namespace FrameHub.Core.Logging
{
    /// <summary>
    /// Główny interfejs loggera dla aplikacji.
    /// </summary>
    public interface ILogger : IDisposable
    {
        /// <summary>
        /// Ustawia aktualny poziom logowania.
        /// </summary>
        ILogLevel LogLevel { get; set; }

        /// <summary>
        /// Loguje wiadomość na poziomie Debug.
        /// </summary>
        void Debug(string message);

        /// <summary>
        /// Loguje wiadomość na poziomie Info.
        /// </summary>
        void Info(string message);

        /// <summary>
        /// Loguje ostrzeżenie na poziomie Warn.
        /// </summary>
        void Warn(string message);

        /// <summary>
        /// Loguje błąd na poziomie Error z opcjonalnym wyjątkiem.
        /// </summary>
        void Error(string message, Exception? ex = null);

        /// <summary>
        /// Loguje krytyczny błąd na poziomie Fatal z opcjonalnym wyjątkiem.
        /// </summary>
        void Fatal(string message, Exception? ex = null);

        /// <summary>
        /// Loguje wyjątek z domyślnym komunikatem.
        /// </summary>
        void LogException(Exception ex);
    }
}
