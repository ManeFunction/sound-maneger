#if UNITY_ADDRESSABLES
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;

namespace Mane.Unity.SoundManeger
{
    public class AddressablesMusicLoader : IMusicLoader
    {
        /// <summary>
        /// Addressables loader should retry as asset loading can fail temporarily
        /// </summary>
        public bool ShouldRetry => true;

        public async Task<AudioClip> GetMusicAsync(MonoBehaviour owner, string path, CancellationToken token = default)
        {
            if (string.IsNullOrEmpty(path)) return null;

            if (!owner) owner = SoundManeger.Instance;

            // Polled directly: a coroutine on the owner would stop with it, and this load would never finish
            AsyncOperationHandle<AudioClip> handle = Addressables.LoadAssetAsync<AudioClip>(path);
            while (!handle.IsDone)
            {
                if (token.IsCancellationRequested)
                {
                    // Nobody takes the clip, so it is released as soon as it loads
                    handle.Completed += OnShouldReleaseHandler;
                    return null;
                }

                await Task.Yield();
            }

            // Canceled, failed, or there is no owner left to hold the clip
            if (token.IsCancellationRequested || handle.Status != AsyncOperationStatus.Succeeded || !owner)
            {
                OnShouldReleaseHandler(handle);
                return null;
            }

            var links = owner.GetComponents<AddressablesMusicLink>();
            AddressablesMusicLink link = links.FirstOrDefault(l => l.Clip == handle.Result);
            if (!link)
            {
                link = owner.gameObject.AddComponent<AddressablesMusicLink>();
                link.ShouldReleaseHandler += OnShouldReleaseHandler;
            }

            link.Bind(handle);
            return handle.Result;
        }

        private void OnShouldReleaseHandler(AsyncOperationHandle<AudioClip> handler)
        {
            if (handler.IsValid())
                Addressables.Release(handler);
        }
    }
}
#endif
