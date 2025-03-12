using System;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace Mane.SoundManeger
{
    public class ResourcesMusicLoader : IMusicLoader
    {
        /// <summary>
        /// Resources loads should not be retried as failures are generally permanent.
        /// </summary>
        public bool ShouldRetry => false;

        public async Task<AudioClip> GetMusicAsync(MonoBehaviour owner, string path, CancellationToken token = default)
        {
            if (string.IsNullOrEmpty(path)) 
                return null;
            
            if (owner == null)
                owner = SoundManeger.Instance;
            
            try 
            {
                // Create a TaskCompletionSource to convert Unity's async operation to Task
                var tcs = new TaskCompletionSource<AudioClip>();
                
                var request = Resources.LoadAsync<AudioClip>(path);
                
                request.completed += operation => 
                {
                    if (token.IsCancellationRequested)
                    {
                        tcs.TrySetCanceled(token);
                        return;
                    }
                    
                    var resourceRequest = operation as ResourceRequest;
                    var clip = resourceRequest?.asset as AudioClip;
                    if (clip != null)
                        tcs.TrySetResult(clip);
                    else
                        tcs.TrySetResult(null);
                };
                
                // Setup cancellation token to cancel the operation if needed
                using (token.Register(() => tcs.TrySetCanceled()))
                {
                    return await tcs.Task;
                }
            }
            catch (OperationCanceledException)
            {
                return null;
            }
            catch (Exception ex)
            {
                Debug.LogError($"[ResourcesMusicLoader] Error loading {path}: {ex.Message}");
                return null;
            }
        }
    }
}