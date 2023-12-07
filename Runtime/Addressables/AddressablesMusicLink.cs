#if UNITY_ADDRESSABLES
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;

namespace Mane.SoundManeger
{
    public class AddressablesMusicLink : MonoBehaviour
    {
        private AsyncOperationHandle<AudioClip> _handler;
        
        /// <summary>
        /// Music clip from the handler or null if the handler is invalid or empty.
        /// </summary>
        public AudioClip Clip => _handler.IsValid() ? _handler.Result : null;
        
        /// <summary>
        /// Releasing the previous handler and binding a new one.
        /// </summary>
        /// <param name="musicHandler">New music handler.</param>
        public void Bind(AsyncOperationHandle<AudioClip> musicHandler)
        {
            ReleaseInternal();
            _handler = musicHandler;
        }
        
        /// <summary>
        /// Release music handler and destroys the link itself.
        /// </summary>
        [ContextMenu("Release")]
        public void Release()
        {
            ReleaseInternal();
            Destroy(this);
        }
        
        private void ReleaseInternal()
        {
            if (_handler.IsValid())
                Addressables.Release(_handler);
        }

        private void OnDestroy() => Release();
    }
}
#endif