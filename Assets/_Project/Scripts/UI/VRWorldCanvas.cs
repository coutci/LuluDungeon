using UnityEngine;

namespace LuluDungeon
{
    /// <summary>
    /// VR World Space Canvas 基类
    /// 提供射线交互面板的通用功能
    /// </summary>
    [RequireComponent(typeof(Canvas))]
    public class VRWorldCanvas : MonoBehaviour
    {
        [Header("面板设置")]
        [Tooltip("面板与玩家的距离")]
        public float distanceFromPlayer = 1.5f;
        [Tooltip("面板高度偏移")]
        public float heightOffset = 0f;

        protected Canvas _canvas;
        protected Transform _cameraTransform;

        public bool IsVisible
        {
            get => _canvas.enabled;
            set => _canvas.enabled = value;
        }

        protected virtual void Awake()
        {
            _canvas = GetComponent<Canvas>();
            _canvas.renderMode = RenderMode.WorldSpace;
            _canvas.worldCamera = Camera.main;
        }

        protected virtual void Start()
        {
            _cameraTransform = Camera.main?.transform;
        }

        /// <summary>
        /// 将面板定位到玩家前方
        /// </summary>
        public virtual void PositionInFrontOfPlayer()
        {
            if (_cameraTransform == null)
                _cameraTransform = Camera.main?.transform;
            if (_cameraTransform == null) return;

            Vector3 forward = _cameraTransform.forward;
            forward.y = 0;
            forward.Normalize();

            transform.position = _cameraTransform.position + forward * distanceFromPlayer
                                 + Vector3.up * heightOffset;
            transform.LookAt(_cameraTransform);
            transform.Rotate(0, 180, 0); // 翻转使面板正面朝向玩家
        }

        public virtual void Show()
        {
            IsVisible = true;
            PositionInFrontOfPlayer();
        }

        public virtual void Hide()
        {
            IsVisible = false;
        }
    }
}
