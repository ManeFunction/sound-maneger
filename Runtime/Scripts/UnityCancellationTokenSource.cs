using System.Threading;

namespace Mane.Unity.SoundManeger
{
    // Cancels itself when the editor leaves Play mode, so loads do not outlive the session.
    internal class UnityCancellationTokenSource : CancellationTokenSource
    {
#if UNITY_EDITOR
        public UnityCancellationTokenSource() => 
            UnityEditor.EditorApplication.playModeStateChanged += OnPlayModeChanged;


        protected override void Dispose(bool disposing)
        {
            if (disposing) 
                UnityEditor.EditorApplication.playModeStateChanged -= OnPlayModeChanged;

            base.Dispose(disposing);
        }


        private void OnPlayModeChanged(UnityEditor.PlayModeStateChange state)
        {
            if (state == UnityEditor.PlayModeStateChange.ExitingPlayMode)
                Cancel();
        }
#endif
    }
}