#if UNITY_ADDRESSABLES
using System;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;

namespace Mane.SoundManeger
{
    internal static class TaskExtensions
    {
        public static async Task WithCancellation(this Task task, CancellationToken cancellationToken)
        {
            var tcs = new TaskCompletionSource<bool>();
            using (cancellationToken.Register(s => ((TaskCompletionSource<bool>)s).TrySetResult(true), tcs))
            {
                if (task.IsCompleted || await Task.WhenAny(task, tcs.Task) != tcs.Task)
                    await task;
                else
                    throw new OperationCanceledException(cancellationToken);
            }
        }
    }

    public class AddressablesMusicLoader : IMusicLoader
    {
        // Track loaded assets to prevent them from being unloaded while in use
        private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, AsyncOperationHandle<AudioClip>> _loadedAssets = 
            new System.Collections.Concurrent.ConcurrentDictionary<string, AsyncOperationHandle<AudioClip>>();

        /// <summary>
        /// Creates a new AddressablesMusicLoader instance.
        /// </summary>
        public AddressablesMusicLoader() { }

        /// <summary>
        /// Addressable loads should be retried as failures might be due to transient network issues.
        /// </summary>
        public bool ShouldRetry => true;

        public async Task<AudioClip> GetMusicAsync(MonoBehaviour owner, string path, CancellationToken token = default)
        {
            if (string.IsNullOrEmpty(path))
                return null;
            
            if (!owner)
                owner = SoundManeger.Instance;
            
            // Check if we already have the asset loaded
            if (_loadedAssets.TryGetValue(path, out var existingHandle) && existingHandle.IsValid())
            {
                // If handle is valid but still in progress, await it
                if (existingHandle.Status == AsyncOperationStatus.None)
                {
                    try
                    {
                        // Create a cancellation task
                        var tcs = new TaskCompletionSource<bool>();
                        using (token.Register(s => ((TaskCompletionSource<bool>)s).TrySetResult(true), tcs))
                        {
                            if (await Task.WhenAny(existingHandle.Task, tcs.Task) == tcs.Task)
                                throw new OperationCanceledException(token);
                            
                            await existingHandle.Task;
                        }
                        
                        return existingHandle.Status == AsyncOperationStatus.Succeeded ? existingHandle.Result : null;
                    }
                    catch (OperationCanceledException)
                    {
                        return null;
                    }
                }
                
                // Handle is valid and completed
                return existingHandle.Status == AsyncOperationStatus.Succeeded ? existingHandle.Result : null;
            }
            
            // Start new loading operation
            try
            {
                AsyncOperationHandle<AudioClip> handle = Addressables.LoadAssetAsync<AudioClip>(path);
                
                // Register the handle for tracking before awaiting
                _loadedAssets[path] = handle;
                
                // Wait for completion with cancellation support
                var tcs = new TaskCompletionSource<bool>();
                using (token.Register(s => ((TaskCompletionSource<bool>)s).TrySetResult(true), tcs))
                {
                    if (await Task.WhenAny(handle.Task, tcs.Task) == tcs.Task)
                        throw new OperationCanceledException(token);
                    
                    await handle.Task;
                }
                
                // If loading failed, remove the handle from tracked assets
                if (handle.Status != AsyncOperationStatus.Succeeded)
                {
                    _loadedAssets.TryRemove(path, out _);
                    Addressables.Release(handle);
                    return null;
                }
                
                return handle.Result;
            }
            catch (OperationCanceledException)
            {
                // Cleanup on cancellation
                if (_loadedAssets.TryRemove(path, out var handle) && handle.IsValid())
                {
                    Addressables.Release(handle);
                }
                return null;
            }
            catch (Exception ex)
            {
                Debug.LogError($"[AddressablesMusicLoader] Error loading {path}: {ex.Message}");
                
                // Cleanup on error
                if (_loadedAssets.TryRemove(path, out var handle) && handle.IsValid())
                {
                    Addressables.Release(handle);
                }
                return null;
            }
        }
    }
}
#endif