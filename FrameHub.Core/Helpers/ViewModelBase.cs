using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace FrameHub.Core.Helpers
{
    /// <summary>
    /// Provides a base implementation for ViewModels to support data binding 
    /// by implementing the INotifyPropertyChanged interface.
    /// </summary>
    public abstract class ViewModelBase : INotifyPropertyChanged
    {
        #region Events
        /// <summary>
        /// Occurs when a property value changes.
        /// </summary>
        public event PropertyChangedEventHandler? PropertyChanged;
        #endregion

        #region Protected Methods
        /// <summary>
        /// Raises the PropertyChanged event to notify the UI that a property value has been updated.
        /// </summary>
        /// <param name="propertyName">The name of the property. Automatically populated via CallerMemberName.</param>
        protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        /// <summary>
        /// Sets a property value and automatically notifies the UI if the value has changed.
        /// </summary>
        /// <typeparam name="T">The type of the property.</typeparam>
        /// <param name="storage">Reference to the private backing field.</param>
        /// <param name="value">The new value to set.</param>
        /// <param name="propertyName">The name of the property (optional).</param>
        /// <returns>True if the value was changed; false if the new value was equal to the old value.</returns>
        protected bool SetProperty<T>(ref T storage, T value, [CallerMemberName] string? propertyName = null)
        {
            if (Equals(storage, value))
            {
                return false;
            }

            storage = value;
            OnPropertyChanged(propertyName);
            return true;
        }
        #endregion
    }
}