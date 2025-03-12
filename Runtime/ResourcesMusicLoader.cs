using System;
using System.Collections;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace Mane.SoundManeger
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
            
            owner ??= SoundManeger.Instance;
            
            AudioClip result = null;
            Coroutine coroutine = owner.StartCoroutine(GetMusicCoroutine(path, OnClipLoaded, token));
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
        
        private IEnumerator GetMusicCoroutine(string path, Action<AudioClip> callback, CancellationToken token)
        {
            var request = Resources.LoadAsync<AudioClip>(path);
            yield return request;

            if (token.IsCancellationRequested)
            {
                callback?.Invoke(null);
                yield break;
            }
            
            callback?.Invoke((AudioClip)request.asset);
        }
    }
}