namespace FrameHub.Core.Logging
{
    /// <summary>
    /// Interfejs definiujący poziomy logowania.
    /// </summary>
    public interface ILogLevel
    {
        /// <summary>
        /// Nazwa poziomu logowania (np. "Debug", "Info", "Error").
        /// </summary>
        string Name { get; }

        /// <summary>
        /// Liczbowy poziom logowania (niższa wartość = wyższy priorytet).
        /// </summary>
        int Level { get; }

        /// <summary>
        /// Sprawdza, czy ten poziom logowania jest ważniejszy lub równy innemu poziomowi.
        /// </summary>
        /// <param name="other">Inny poziom logowania do porównania.</param>
        /// <returns>True jeśli ten poziom ma wyższy lub równy priorytet.</returns>
        bool IsEnabled(ILogLevel other);
    }
}
