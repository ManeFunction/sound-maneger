using System.Threading.Tasks;
using UnityEngine;

namespace Mane.SoundManeger
{
    public interface IMusicLoader
    {
        Task<AudioClip> GetMusicAsync(MonoBehaviour owner, string path);
    }
}