using UnityEngine;

namespace LuluDungeon
{
    /// <summary>
    /// 使对象始终面朝主摄像机（VR头显）
    /// </summary>
    public class Billboard : MonoBehaviour
    {
        [Header("设置")]
        [Tooltip("是否只在Y轴旋转（通常UI面板选择true）")]
        public bool lockYAxis = true;
        [Tooltip("是否同时翻转朝向（从背面看时）")]
        public bool flipDirection = false;

        private Transform _cameraTransform;

        private void Start()
        {
            _cameraTransform = Camera.main?.transform;
        }

        private void LateUpdate()
        {
            if (_cameraTransform == null)
            {
                _cameraTransform = Camera.main?.transform;
                return;
            }

            Vector3 direction = _cameraTransform.position - transform.position;

            if (flipDirection)
                direction = -direction;

            if (lockYAxis)
                direction.y = 0;

            if (direction != Vector3.zero)
                transform.rotation = Quaternion.LookRotation(direction);
        }
    }
}
