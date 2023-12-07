#if UNITY_ADDRESSABLES
using System;
using System.Collections;
using System.Linq;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;

namespace Mane.SoundManeger
{
    public class AddressablesMusicLoader : IMusicLoader
    {
        private readonly float _repeatDelay;

        /// <summary>
        /// Creates AddressablesMusicLoader
        /// </summary>
        /// <param name="repeatDelay">Delay before repeat loading if loading failed</param>
        public AddressablesMusicLoader(float repeatDelay = 5f) => _repeatDelay = repeatDelay;

        public async Task<AudioClip> GetMusicAsync(MonoBehaviour owner, string path)
        {
            owner ??= SoundManeger.Instance;
            
            AudioClip result = null;
            Coroutine coroutine = owner.StartCoroutine(GetMusicCoroutine(owner, path, OnClipLoaded));
            while (coroutine != null)
                await Task.Yield();
            return result;

            
            void OnClipLoaded(AudioClip clip)
            {
                result = clip;
                coroutine = null;
            }
        }
        
        private IEnumerator GetMusicCoroutine(MonoBehaviour owner, string path, Action<AudioClip> callback)
        {
            AsyncOperationHandle<AudioClip> handle = default;
            if (!handle.IsValid())
            {
                while (true)
                {
                    handle = Addressables.LoadAssetAsync<AudioClip>(path);
                    yield return handle;
                    
                    if (handle.Status == AsyncOperationStatus.Succeeded)
                        break;
                    Addressables.Release(handle);
                    
                    yield return new WaitForSecondsRealtime(_repeatDelay);
                }
            }

            if (owner == null)
            {
                callback?.Invoke(null);
                yield break;
            }
            
            var links = owner.GetComponents<AddressablesMusicLink>();
            AddressablesMusicLink link = links.FirstOrDefault(l => l.Clip == handle.Result);
            link ??= owner.gameObject.AddComponent<AddressablesMusicLink>();
            link.Bind(handle);
            callback?.Invoke(handle.Result);
        }
    }
}
#endif