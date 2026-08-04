using System.Collections;
using UnityEngine;
using TMPro;

namespace LuluDungeon
{
    /// <summary>
    /// 战斗提示：在世界空间指定锚点上方弹出悬浮文字（如"进入战斗！"），
    /// 始终面朝玩家（Billboard），播放一段时间后淡出销毁。
    /// </summary>
    public class BattlePrompt : MonoBehaviour
    {
        /// <summary>
        /// 在锚点上方显示提示文字
        /// </summary>
        public static void Show(Transform anchor, string text, float duration = 2.5f)
        {
            if (anchor == null) return;

            var go = new GameObject("BattlePrompt");
            go.transform.SetParent(anchor, false);
            go.transform.localPosition = Vector3.up * 2.2f;

            var prompt = go.AddComponent<BattlePrompt>();
            prompt.Init(text, duration);
        }

        private TextMeshPro _tmp;

        private void Init(string text, float duration)
        {
            _tmp = gameObject.AddComponent<TextMeshPro>();
            _tmp.text = text;
            _tmp.fontSize = 6;
            _tmp.alignment = TextAlignmentOptions.Center;
            _tmp.color = Color.yellow;
            _tmp.outlineWidth = 0.3f;
            _tmp.outlineColor = Color.black;

            gameObject.AddComponent<Billboard>();

            StartCoroutine(FadeAndDestroy(duration));
        }

        private IEnumerator FadeAndDestroy(float duration)
        {
            float fade = 0.4f;
            yield return new WaitForSeconds(Mathf.Max(0.1f, duration - fade));

            float t = 0f;
            while (t < fade)
            {
                t += Time.deltaTime;
                if (_tmp != null)
                {
                    var c = _tmp.color;
                    c.a = Mathf.Clamp01(1f - t / fade);
                    _tmp.color = c;
                }
                yield return null;
            }

            Destroy(gameObject);
        }
    }
}
