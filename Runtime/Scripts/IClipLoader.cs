using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace Mane.Unity.SoundManeger
{
    /// <summary>
    /// Loads the clips <see cref="SoundManeger"/> plays by path (music, sound effects and voice).
    /// Set one with <see cref="SoundManeger.SetClipLoader"/>.
    /// </summary>
    public interface IClipLoader
    {
        /// <summary>
        /// Delay in seconds before the manager retries a music load that failed. Zero or less (the default) means
        /// a failed load is not retried. Sound effects and voice are never retried.
        /// </summary>
        float RetryDelay => 0f;

        /// <summary>
        /// Loads the clip at <paramref name="path"/>. Called on the main thread.
        /// </summary>
        /// <param name="owner">Object the loaded clip belongs to, for loaders that release clips with their owner.
        /// Null when the caller releases the clip with <see cref="Release"/>, as the manager does for music.
        /// The manager always gives an owner for sound effects and voice.</param>
        /// <param name="path">Path or key of the clip.</param>
        /// <param name="token">Canceled when the clip is no longer needed.</param>
        /// <returns>The clip, or null if it could not be loaded or the load was canceled.</returns>
        Task<AudioClip> LoadClipAsync(MonoBehaviour owner, string path, CancellationToken token = default);

        /// <summary>
        /// Releases a clip that <see cref="LoadClipAsync"/> loaded without an owner, once per load. Called on the main thread.
        /// Does nothing by default, for loaders whose clips need no release, like <see cref="ResourcesClipLoader"/>.
        /// </summary>
        /// <param name="clip">Clip to release.</param>
        void Release(AudioClip clip) { }
    }
}
