using UnityEngine;
using UnityEngine.ResourceManagement.AsyncOperations;

namespace Mane.Unity.SoundManeger.Addressables
{
    // Holds the Addressables handle of one clip loaded for an owner, and releases it when another handle is bound
    // or the owner's GameObject is destroyed. AddressablesClipLoader adds it to the owner.
    // Hidden from the Add Component menu: a link added by hand holds nothing.
    [AddComponentMenu("")]
    internal class AddressablesClipLink : MonoBehaviour
    {
        private AsyncOperationHandle<AudioClip> _handle;

        // Clip of the bound handle, or null when the handle is invalid
        public AudioClip Clip => _handle.IsValid() ? _handle.Result : null;

        // Releases the previous handle and binds a new one
        public void Bind(AsyncOperationHandle<AudioClip> handle)
        {
            AddressablesClipLoader.ReleaseHandle(_handle);
            _handle = handle;
        }

        private void OnDestroy() => AddressablesClipLoader.ReleaseHandle(_handle);
    }
}
