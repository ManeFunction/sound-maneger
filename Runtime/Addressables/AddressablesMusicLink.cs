#if UNITY_ADDRESSABLES
using UnityEngine;
using UnityEngine.ResourceManagement.AsyncOperations;

namespace Mane.Unity.SoundManeger
{
    /// <summary>
    /// Handles <see cref="AddressablesMusicLink.ShouldReleaseHandler"/>.
    /// </summary>
    /// <param name="musicHandler">Handle to release. Can be invalid (nothing to release).</param>
    public delegate void MusicLinkHandler(AsyncOperationHandle<AudioClip> musicHandler);

    /// <summary>
    /// Holds the Addressables handle of one loaded clip for its GameObject, and asks for its release
    /// when another handle is bound or the GameObject is destroyed. <see cref="AddressablesMusicLoader"/> adds it to the owner.
    /// </summary>
    public class AddressablesMusicLink : MonoBehaviour
    {
        private AsyncOperationHandle<AudioClip> _handler;

        /// <summary>
        /// Raised with the handle that should be released: the previous one on <see cref="Bind"/>, the current one on destroy.
        /// </summary>
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