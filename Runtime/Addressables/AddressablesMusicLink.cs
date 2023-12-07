#if UNITY_ADDRESSABLES
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;

namespace Mane.SoundManeger
{
    public class AddressablesMusicLink : MonoBehaviour
    {
        private AsyncOperationHandle<AudioClip> _handler;
        
        public AudioClip Clip => _handler.IsValid() ? _handler.Result : null;
        
        public void Bind(AsyncOperationHandle<AudioClip> musicHandler)
        {
            Release();
            _handler = musicHandler;
        }
        
        public void Release()
        {
            if (_handler.IsValid())
                Addressables.Release(_handler);
        }
        
        private void OnDestroy() => Release();
    }
}
#endif