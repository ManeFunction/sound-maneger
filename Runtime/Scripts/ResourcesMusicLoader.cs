using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace Mane.Unity.SoundManeger
{
    /// <summary>
    /// Default <see cref="IMusicLoader"/>: loads clips from <c>Resources</c> folders with <see cref="Resources.LoadAsync{T}(string)"/>.
    /// Paths are relative to a <c>Resources</c> folder, without the file extension. The owner is not used.
    /// </summary>
    public class ResourcesMusicLoader : IMusicLoader
    {
        /// <summary>
        /// Resources loader doesn't need to retry as failures are permanent
        /// </summary>
        public bool ShouldRetry => false;

        /// <inheritdoc />
        public async Task<AudioClip> GetMusicAsync(MonoBehaviour owner, string path, CancellationToken token = default)
        {
            if (string.IsNullOrEmpty(path)) return null;

            // Polled directly: a coroutine on the owner would stop with it, and this load would never finish
            ResourceRequest request = Resources.LoadAsync<AudioClip>(path);
            while (!request.isDone)
            {
                if (token.IsCancellationRequested)
                    return null;

                await Task.Yield();
            }

            return token.IsCancellationRequested ? null : (AudioClip)request.asset;
        }
    }
}
