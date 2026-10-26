using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace Mane.Unity.SoundManeger
{
    /// <summary>
    /// Loads the clips <see cref="SoundManeger"/> plays by path (music, sound effects and voice).
    /// Set one with <see cref="SoundManeger.SetMusicLoader"/>.
    /// </summary>
    public interface IMusicLoader
    {
        /// <summary>
        /// Indicates whether the loader should retry loading if it fails.
        /// Only music is retried, after the manager's retry delay.
        /// </summary>
        bool ShouldRetry { get; }

        /// <summary>
        /// Loads the clip at <paramref name="path"/>. Called on the main thread.
        /// </summary>
        /// <param name="owner">Object the loaded clip belongs to, for loaders that release clips with their owner. Never null.</param>
        /// <param name="path">Path or key of the clip.</param>
        /// <param name="token">Canceled when the clip is no longer needed.</param>
        /// <returns>The clip, or null if it could not be loaded or the load was canceled.</returns>
        Task<AudioClip> GetMusicAsync(MonoBehaviour owner, string path, CancellationToken token = default);
    }
}
