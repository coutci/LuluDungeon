using UnityEngine;

namespace LuluDungeon
{
    /// <summary>
    /// NPC 行为：强制只播待机动画（锁定 IdleNormal，防控制器自动过渡到攻击/移动）
    /// + 头顶朝玩家的文字标签
    /// </summary>
    public class NPCIdle : MonoBehaviour
    {
        [Header("标签")]
        public string label = "";
        public float labelHeight = 2.5f;
        public float labelScale = 0.04f;

        private Animator _anim;
        private string _idleClip;

        private void Start()
        {
            // 支持挂载在根节点（Animator 在子物体上）
            _anim = GetComponent<Animator>();
            if (_anim == null) _anim = GetComponentInChildren<Animator>();
            if (_anim != null && _anim.runtimeAnimatorController != null)
            {
                // 检测 IdleNormal（或任意 idle 片段）
                foreach (var c in _anim.runtimeAnimatorController.animationClips)
                {
                    string n = c.name.ToLower();
                    if (n.Contains("idle") && n.Contains("normal")) { _idleClip = c.name; break; }
                    if (n.Contains("idle")) _idleClip = c.name;
                }
                if (_idleClip != null) _anim.Play(_idleClip);
            }
            CreateLabel();
        }

        private void Update()
        {
            // 每帧检查：一旦离开待机状态，立刻强制回到待机
            if (_anim == null || string.IsNullOrEmpty(_idleClip)) return;
            var info = _anim.GetCurrentAnimatorStateInfo(0);
            if (!info.IsName(_idleClip))
                _anim.Play(_idleClip);
        }

        private void CreateLabel()
        {
            if (string.IsNullOrEmpty(label)) return;

            var go = new GameObject("Label_" + label);
            go.transform.SetParent(transform, false);
            go.transform.localPosition = new Vector3(0, labelHeight, 0);

            var tm = go.AddComponent<TextMesh>();
            tm.text = label;
            tm.fontSize = 80;
            tm.characterSize = labelScale;
            tm.color = Color.white;
            tm.anchor = TextAnchor.MiddleCenter;
            tm.alignment = TextAlignment.Center;

            // flipDirection=true：TextMesh 从 -Z 面可读，翻转后正对玩家
            var b = go.AddComponent<Billboard>();
            b.flipDirection = true;
            b.lockYAxis = true;
        }
    }
}
