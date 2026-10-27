using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace Mane.Unity.SoundManeger
{
    /// <summary>
    /// Default <see cref="IClipLoader"/>: loads clips from <c>Resources</c> folders with <see cref="Resources.LoadAsync{T}(string)"/>.
    /// Paths are relative to a <c>Resources</c> folder, without the file extension. The owner is not used, and clips
    /// need no release: <see cref="Resources.UnloadUnusedAssets"/> frees them once nothing uses them.
    /// </summary>
    public class ResourcesClipLoader : IClipLoader
    {
        /// <summary>
        /// Always 0: a <c>Resources</c> load that fails will fail again, so it is not retried.
        /// </summary>
        public float RetryDelay => 0f;

        /// <inheritdoc />
        public async Task<AudioClip> LoadClipAsync(MonoBehaviour owner, string path, CancellationToken token = default)
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
