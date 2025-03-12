#if UNITY_ADDRESSABLES
using UnityEngine;
using UnityEngine.ResourceManagement.AsyncOperations;

namespace Mane.SoundManeger
{
    public delegate void MusicLinkHandler(AsyncOperationHandle<AudioClip> musicHandler);

    public class AddressablesMusicLink : MonoBehaviour
    {
        private AsyncOperationHandle<AudioClip> _handler;

        public event MusicLinkHandler ShouldReleaseHandler;
        
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
            ShouldReleaseHandler?.Invoke(_handler);
            _handler = musicHandler;
        }

        private void OnDestroy() => ShouldReleaseHandler?.Invoke(_handler);
    }
}
#endif