#if UNITY_ADDRESSABLES
using System;
using System.Collections;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;

namespace Mane.SoundManeger
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
            
            AudioClip result = null;
            Coroutine coroutine = owner.StartCoroutine(GetMusicCoroutine(owner, path, OnClipLoaded, token));
            while (coroutine != null)
            {
                if (token.IsCancellationRequested)
                {
                    owner.StopCoroutine(coroutine);
                    coroutine = null;
                }
                
                await Task.Yield();
            }

            return result;

            
            void OnClipLoaded(AudioClip clip)
            {
                result = clip;
                coroutine = null;
            }
        }
        
        private IEnumerator GetMusicCoroutine(MonoBehaviour owner, string path, Action<AudioClip> callback,
            CancellationToken token)
        {
            AsyncOperationHandle<AudioClip> handle = default;
            if (!handle.IsValid())
            {
                handle = Addressables.LoadAssetAsync<AudioClip>(path);
                yield return handle;
                
                if (token.IsCancellationRequested)
                {
                    callback?.Invoke(null);
                    yield break;
                }
                
                if (handle.Status != AsyncOperationStatus.Succeeded)
                {
                    Addressables.Release(handle);
                    callback?.Invoke(null);
                    yield break;
                }
            }

            if (!owner)
            {
                callback?.Invoke(null);
                yield break;
            }
            
            var links = owner.GetComponents<AddressablesMusicLink>();
            AddressablesMusicLink link = links.FirstOrDefault(l => l.Clip == handle.Result);
            if (!link)
            {
                link = owner.gameObject.AddComponent<AddressablesMusicLink>();
                link.ShouldReleaseHandler += OnShouldReleaseHandler;
            }

            link.Bind(handle);
            callback?.Invoke(handle.Result);
        }

        private void OnShouldReleaseHandler(AsyncOperationHandle<AudioClip> handler)
        {
            if (handler.IsValid())
                Addressables.Release(handler);
        }
    }
}
#endif