using UnityEngine;

namespace NOReplay
{
    /// <summary>
    /// Simple OnGUI progress bar shown automatically when ReplayStaticHandler.StartFor is called.
    /// </summary>
    internal class ReplayLoadProgress : MonoBehaviour
    {
        private static ReplayLoadProgress _inst;
        private static float _progress;
        private static string _label = "Lade Replay…";
        private static bool _active;

        public static void Begin(string label = "Lade Replay…")
        {
            _label = label;
            _progress = 0f;
            _active = true;
            if (_inst == null)
            {
                var go = new GameObject("NOReplay_LoadProgress");
                UnityEngine.Object.DontDestroyOnLoad(go);
                go.hideFlags = HideFlags.HideAndDontSave;
                _inst = go.AddComponent<ReplayLoadProgress>();
            }
        }

        public static void Set(float t, string extra = null)
        {
            _progress = Mathf.Clamp01(t);
            if (!string.IsNullOrEmpty(extra))
                _label = extra;
        }

        public static void End()
        {
            _active = false;
            if (_inst != null)
            {
                UnityEngine.Object.Destroy(_inst.gameObject);
                _inst = null;
            }
        }

        void OnGUI()
        {
            if (!_active) return;
            const float w = 440f;
            const float h = 32f;
            var rect = new Rect(16, 16, w, h);
            GUI.Box(rect, "");
            var fill = new Rect(rect.x + 3, rect.y + 3, (w - 6) * _progress, h - 6);
            var old = GUI.color;
            GUI.color = new Color(0.15f, 0.65f, 0.25f, 0.85f);
            GUI.DrawTexture(fill, Texture2D.whiteTexture);
            GUI.color = Color.white;
            GUI.Label(new Rect(rect.x + 8, rect.y + 6, w - 16, 22),
                $"{_label}  {(_progress * 100f):0}%");
            GUI.color = old;
        }
    }
}