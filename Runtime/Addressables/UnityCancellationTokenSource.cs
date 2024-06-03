using System.Threading;

namespace Mane.SoundManeger
{
    internal class UnityCancellationTokenSource : CancellationTokenSource
    {
        private static UnityCancellationTokenSource _global;

        public static CancellationToken GlobalToken
        {
            get
            {
                _global ??= new UnityCancellationTokenSource();
                
                return _global.Token;
            }
        }

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