using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

namespace LuluDungeon
{
    /// <summary>
    /// 按钮悬停高亮：手柄射线悬停在按钮上时，填充色块向亮金色插值（类似鼠标 hover 变色），移开恢复原色。
    /// 纯视觉层——只监听 XRSimpleInteractable 的 hoverEntered/hoverExited 事件改颜色，
    /// 不修改交互器、collider、事件回调等任何影响点击的逻辑。
    /// 组件自动绑定自身 XRSimpleInteractable；禁用的按钮不会收到 hover，天然与面板互斥兼容。
    /// </summary>
    public class HoverHighlight : MonoBehaviour
    {
        [Tooltip("高亮目标色（亮金）")]
        public Color highlightColor = new Color(1f, 0.85f, 0.35f);
        [Range(0f, 1f)]
        [Tooltip("原色 → 高亮色插值比例")]
        public float blend = 0.55f;

        private XRSimpleInteractable _interactable;
        private UnityEngine.UI.Image _image;      // uGUI 填充色块
        private Renderer _renderer;                // 3D 填充色块
        private Color _originalColor;
        private bool _hasOriginal;

        private void Start()
        {
            _interactable = GetComponent<XRSimpleInteractable>();
            if (_interactable == null)
            {
                enabled = false;
                return;
            }
            _interactable.hoverEntered.AddListener(OnHoverEnter);
            _interactable.hoverExited.AddListener(OnHoverExit);
            CacheTargets();
        }

        private void OnDestroy()
        {
            if (_interactable != null)
            {
                _interactable.hoverEntered.RemoveListener(OnHoverEnter);
                _interactable.hoverExited.RemoveListener(OnHoverExit);
            }
        }

        private void OnHoverEnter(HoverEnterEventArgs args)
        {
            ApplyHighlight(true);
        }

        private void OnHoverExit(HoverExitEventArgs args)
        {
            ApplyHighlight(false);
        }

        /// <summary>定位填充色块：优先 uGUI Button.targetGraphic → 第二层 Image（Fill）→ 第二层 Renderer</summary>
        private void CacheTargets()
        {
            var btn = GetComponent<UnityEngine.UI.Button>();
            if (btn != null && btn.targetGraphic != null)
            {
                _image = btn.targetGraphic as UnityEngine.UI.Image;
                if (_image != null) return;
            }

            var images = GetComponentsInChildren<UnityEngine.UI.Image>(true);
            if (images.Length > 1) _image = images[1];          // Fill（Border 之下）
            else if (images.Length == 1) _image = images[0];

            if (_image == null)
            {
                var renderers = GetComponentsInChildren<Renderer>(true);
                if (renderers.Length > 1) _renderer = renderers[1];  // Fill（3D 色块）
                else if (renderers.Length == 1) _renderer = renderers[0];
            }
        }

        private void ApplyHighlight(bool on)
        {
            if (_image != null)
            {
                if (!_hasOriginal) { _originalColor = _image.color; _hasOriginal = true; }
                _image.color = on ? Color.Lerp(_originalColor, highlightColor, blend) : _originalColor;
                return;
            }

            if (_renderer != null)
            {
                // material 属性自动实例化，不会污染共享材质
                if (!_hasOriginal) { _originalColor = _renderer.material.color; _hasOriginal = true; }
                _renderer.material.color = on ? Color.Lerp(_originalColor, highlightColor, blend) : _originalColor;
            }
        }
    }
}
