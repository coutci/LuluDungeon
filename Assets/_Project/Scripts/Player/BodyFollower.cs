using UnityEngine;

namespace LuluDungeon
{
    /// <summary>
    /// 身体跟随：位置跟随相机 XZ + 固定腰高，旋转只跟相机 Y 轴（不随低头/歪头）
    /// 挂在腰带/身体对象上
    /// </summary>
    public class BodyFollower : MonoBehaviour
    {
        [Header("跟随目标（默认主相机）")]
        public Transform head;

        [Header("偏移")]
        public float heightOffset = -0.7f;  // 眼睛下移多少到腰（负数）
        public float forwardOffset = 0.4f;  // 身体前方偏移

        private void LateUpdate()
        {
            if (head == null)
            {
                head = Camera.main != null ? Camera.main.transform : null;
                if (head == null) return;
            }

            // 身体朝向：相机前向投影到水平面
            Vector3 fwd = head.forward;
            fwd.y = 0;
            fwd.Normalize();
            if (fwd.sqrMagnitude < 0.0001f) fwd = Vector3.forward;

            // 位置：相机 XZ + 腰高 + 前向偏移
            Vector3 pos = new Vector3(head.position.x, head.position.y + heightOffset, head.position.z);
            pos += fwd * forwardOffset;
            transform.position = pos;

            // 旋转：只跟 Y 轴
            transform.rotation = Quaternion.LookRotation(fwd);
        }
    }
}
