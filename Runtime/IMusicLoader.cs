using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace Mane.SoundManeger
{
    public interface IMusicLoader
    {
        /// <summary>
        /// Asynchronously loads an audio clip from the specified path.
        /// </summary>
        /// <param name="owner">The MonoBehaviour that owns this load request.</param>
        /// <param name="path">The path to the audio clip.</param>
        /// <param name="token">Cancellation token to cancel the operation.</param>
        /// <returns>The loaded AudioClip, or null if loading failed.</returns>
        Task<AudioClip> GetMusicAsync(MonoBehaviour owner, string path, CancellationToken token = default);

        /// <summary>
        /// Indicates whether failed load operations should be retried.
        /// </summary>
        /// <remarks>
        /// Resources loads generally fail permanently, while Addressables loads may fail transiently.
        /// </remarks>
        bool ShouldRetry { get; }
    }
}