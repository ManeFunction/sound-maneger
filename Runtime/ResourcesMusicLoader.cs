using System;
using System.Collections;
using System.Threading.Tasks;
using UnityEngine;

namespace Mane.SoundManeger
{
    public class ResourcesMusicLoader : IMusicLoader
    {
        public async Task<AudioClip> GetMusicAsync(MonoBehaviour owner, string path)
        {
            owner ??= SoundManeger.Instance;
            
            AudioClip result = null;
            Coroutine coroutine = owner.StartCoroutine(GetMusicCoroutine(path, OnClipLoaded));
            while (coroutine != null)
                await Task.Yield();
            return result;

            
            void OnClipLoaded(AudioClip clip)
            {
                result = clip;
                coroutine = null;
            }
        }
        
        private IEnumerator GetMusicCoroutine(string path, Action<AudioClip> callback)
        {
            var request = Resources.LoadAsync<AudioClip>(path);
            yield return request;
            callback?.Invoke((AudioClip)request.asset);
        }
    }
}