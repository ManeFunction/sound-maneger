using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.ResourceManagement.AsyncOperations;
// Inside this namespace, a bare Addressables means the namespace, not the class
using AddressablesApi = UnityEngine.AddressableAssets.Addressables;

namespace Mane.Unity.SoundManeger.Addressables
{
    /// <summary>
    /// <see cref="IClipLoader"/> that loads clips by Addressables address. Available when the Addressables package is installed.
    /// A clip loaded for an owner is held by an <see cref="AddressablesMusicLink"/> on the owner's GameObject and released
    /// when the owner is destroyed, so every clip loaded for an owner stays in memory until then.
    /// A clip loaded without an owner (music) is held until <see cref="Release"/>.
    /// </summary>
    public class AddressablesClipLoader : IClipLoader
    {
        // Handles of the clips loaded without an owner, one per load
        private readonly List<AsyncOperationHandle<AudioClip>> _ownerlessHandles = new();

        /// <summary>
        /// Creates the loader.
        /// </summary>
        /// <param name="retryDelay">Delay in seconds before a failed music load is retried, see <see cref="RetryDelay"/>.</param>
        public AddressablesClipLoader(float retryDelay = 5f) => RetryDelay = retryDelay;

        /// <summary>
        /// Delay in seconds before the manager retries a music load that failed, because an Addressables load
        /// can fail temporarily (for example, a remote bundle download). Zero or less turns retries off.
        /// </summary>
        public float RetryDelay { get; }

        /// <inheritdoc />
        public async Task<AudioClip> LoadClipAsync(MonoBehaviour owner, string path, CancellationToken token = default)
        {
            if (string.IsNullOrEmpty(path)) return null;

            // Null, not a destroyed owner: the caller releases the clip with Release()
            bool hasOwner = owner is not null;
            if (hasOwner && !owner) return null;

            // Polled directly: a coroutine on the owner would stop with it, and this load would never finish
            AsyncOperationHandle<AudioClip> handle = AddressablesApi.LoadAssetAsync<AudioClip>(path);
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
            if (token.IsCancellationRequested || handle.Status != AsyncOperationStatus.Succeeded || hasOwner && !owner)
            {
                OnShouldReleaseHandler(handle);
                return null;
            }

            if (!hasOwner)
            {
                _ownerlessHandles.Add(handle);
                return handle.Result;
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

        /// <inheritdoc />
        public void Release(AudioClip clip)
        {
            int index = _ownerlessHandles.FindIndex(handle => handle.IsValid() && ReferenceEquals(handle.Result, clip));
            if (index < 0) return;

            OnShouldReleaseHandler(_ownerlessHandles[index]);
            _ownerlessHandles.RemoveAt(index);
        }

        private void OnShouldReleaseHandler(AsyncOperationHandle<AudioClip> handler)
        {
            if (handler.IsValid())
                AddressablesApi.Release(handler);
        }
    }
}
