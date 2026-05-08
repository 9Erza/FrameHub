using System;

namespace FrameHub.Core.Models
{
    #region Optimization Modes

    /// <summary>
    /// Defines the supported CPU core optimization method.
    /// </summary>
    public enum OptimizationMode
    {
        /// <summary>
        /// Hard binding to specific CPU cores using classic process affinity.
        /// </summary>
        Affinity,

        /// <summary>
        /// Soft binding via Windows CPU Sets. The scheduler prefers selected cores,
        /// but may still make scheduling decisions under system load.
        /// </summary>
        CpuSets,

        /// <summary>
        /// Legacy value kept only so old profiles containing "Exclusive" can be loaded
        /// and migrated safely. It is no longer exposed in the UI.
        /// </summary>
        [Obsolete("Exclusive mode is no longer supported. Existing profiles are migrated to Affinity.")]
        Exclusive
    }

    #endregion
}
