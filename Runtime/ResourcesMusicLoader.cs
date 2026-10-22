using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace Mane.Unity.SoundManeger
{
    public class ResourcesMusicLoader : IMusicLoader
    {
        /// <summary>
        /// Resources loader doesn't need to retry as failures are permanent
        /// </summary>
        public bool ShouldRetry => false;

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
