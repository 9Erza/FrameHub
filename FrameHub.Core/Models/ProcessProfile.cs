using FrameHub.Core.Helpers;
using System;
using System.Text.Json.Serialization;

namespace FrameHub.Core.Models
{
    /// <summary>
    /// Represents a saved optimization profile for a specific process.
    /// The persisted values are kept canonical and language-independent.
    /// UI-only display values are marked with JsonIgnore.
    /// </summary>
    public class ProcessProfile : ViewModelBase
    {
        #region Private Backing Fields

        private string _id = Guid.NewGuid().ToString("N");
        private int _schemaVersion = 2;
        private OptimizationMode _optimizationMode = OptimizationMode.Affinity;
        private string _processName = string.Empty;
        private string _displayName = string.Empty;
        private string? _executablePath;
        private string? _libraryItemId;
        private long _affinityMask;
        private string _priority = "Normal";
        private bool _applyPriority = true;
        private bool _applyCoreOptimization = true;
        private bool _isEnabled = true;
        private string _notes = string.Empty;
        private DateTime _createdAt = DateTime.UtcNow;
        private DateTime _updatedAt = DateTime.UtcNow;
        private string _displayPriority = "Normal";
        private string _displayOptimizationMode = string.Empty;

        #endregion

        #region Identity & Migration

        public string Id
        {
            get => _id;
            set => SetProperty(ref _id, string.IsNullOrWhiteSpace(value) ? Guid.NewGuid().ToString("N") : value.Trim());
        }

        public int SchemaVersion
        {
            get => _schemaVersion;
            set => SetProperty(ref _schemaVersion, value <= 0 ? 2 : value);
        }

        #endregion

        #region Process Identification

        /// <summary>
        /// Gets or sets the target process name without the .exe extension.
        /// </summary>
        public string ProcessName
        {
            get => _processName;
            set => SetProperty(ref _processName, value?.Trim() ?? string.Empty);
        }

        /// <summary>
        /// User-friendly name reserved for future game/application profiles.
        /// </summary>
        public string DisplayName
        {
            get => string.IsNullOrWhiteSpace(_displayName) ? ProcessName : _displayName;
            set => SetProperty(ref _displayName, value?.Trim() ?? string.Empty);
        }

        /// <summary>
        /// Optional executable path reserved for future path-bound profiles.
        /// </summary>
        public string? ExecutablePath
        {
            get => _executablePath;
            set => SetProperty(ref _executablePath, string.IsNullOrWhiteSpace(value) ? null : value.Trim());
        }

        /// <summary>
        /// Optional link to a specific library game/app item. This keeps library profiles separate even
        /// when two entries resolve to a similar executable/process name.
        /// </summary>
        public string? LibraryItemId
        {
            get => _libraryItemId;
            set => SetProperty(ref _libraryItemId, string.IsNullOrWhiteSpace(value) ? null : value.Trim());
        }

        #endregion

        #region Optimization Settings

        public OptimizationMode OptimizationMode
        {
            get => _optimizationMode;
            set => SetProperty(ref _optimizationMode, value);
        }

        public long AffinityMask
        {
            get => _affinityMask;
            set => SetProperty(ref _affinityMask, value);
        }

        public string Priority
        {
            get => _priority;
            set => SetProperty(ref _priority, value?.Trim() ?? "Normal");
        }

        public bool ApplyPriority
        {
            get => _applyPriority;
            set => SetProperty(ref _applyPriority, value);
        }

        public bool ApplyCoreOptimization
        {
            get => _applyCoreOptimization;
            set => SetProperty(ref _applyCoreOptimization, value);
        }

        public bool IsEnabled
        {
            get => _isEnabled;
            set => SetProperty(ref _isEnabled, value);
        }

        public string Notes
        {
            get => _notes;
            set => SetProperty(ref _notes, value?.Trim() ?? string.Empty);
        }

        public DateTime CreatedAt
        {
            get => _createdAt;
            set => SetProperty(ref _createdAt, value == default ? DateTime.UtcNow : value);
        }

        public DateTime UpdatedAt
        {
            get => _updatedAt;
            set => SetProperty(ref _updatedAt, value == default ? DateTime.UtcNow : value);
        }

        #endregion

        #region UI-only values

        [JsonIgnore]
        public string DisplayPriority
        {
            get => _displayPriority;
            set => SetProperty(ref _displayPriority, value);
        }

        [JsonIgnore]
        public string DisplayOptimizationMode
        {
            get => _displayOptimizationMode;
            set => SetProperty(ref _displayOptimizationMode, value);
        }

        #endregion
    }
}
