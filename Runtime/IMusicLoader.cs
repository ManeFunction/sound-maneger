using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace Mane.SoundManeger
{
    public interface IMusicLoader
    {
        /// <summary>
        /// Indicates whether the loader should retry loading if it fails
        /// </summary>
        bool ShouldRetry { get; }
        
        Task<AudioClip> GetMusicAsync(MonoBehaviour owner, string path, CancellationToken token = default);
    }
}