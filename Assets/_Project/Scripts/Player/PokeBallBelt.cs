using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

namespace LuluDungeon
{
    /// <summary>
    /// 腰间精灵球腰带：只为已装载精灵的槽生成球(空槽不显示球)
    /// 布局:左侧 3 颗(槽 0-2) + 右侧 3 颗(槽 3-5)
    /// 位置由 BodyFollower 控制（跟随腰部）
    /// 每颗球是可抓取/可投掷的 PokeBallObject，负责抓取判定与回归
    /// </summary>
    public class PokeBallBelt : MonoBehaviour
    {
        [Header("球Prefab")]
        public GameObject pokeBallPrefab;   // 可投掷的球（含碰撞体+Rigidbody+PokeBallObject）

        [Header("球布局")]
        public float sideSpacing = 0.22f;   // 右侧横向偏移
        public float ballSpacing = 0.12f;   // 前后间距
        public float ballDiameter = 0.1f;

        [Header("材质")]
        public Material loadedBallMat;      // 装载球（有精灵）
        public Material emptyBallMat;       // 空球

        private readonly List<PokeBallObject> _balls = new List<PokeBallObject>();

        private void Start()
        {
            RefreshBalls();
            // 捕捉/战斗结束/装卸后刷新腰间球
            EventBus.Subscribe<SpriteInstance>(EventBus.ON_SPRITE_CAUGHT, OnSpriteCaught);
            EventBus.Subscribe(EventBus.ON_BATTLE_END, RefreshBalls);
        }

        private void OnDestroy()
        {
            EventBus.Unsubscribe<SpriteInstance>(EventBus.ON_SPRITE_CAUGHT, OnSpriteCaught);
            EventBus.Unsubscribe(EventBus.ON_BATTLE_END, RefreshBalls);
        }

        private void OnSpriteCaught(SpriteInstance s)
        {
            RefreshBalls();
        }

        /// <summary>
        /// 按玩家实际球槽数量刷新腰间球（保留已抓取/飞行中的球）
        /// </summary>
        public void RefreshBalls()
        {
            if (GameManager.Instance == null) return;
            var slots = GameManager.Instance.playerData.pokeBallSlots;
            if (slots == null) return;

            // 清理:销毁不再需要的球(空槽的球 / 越界槽的球 / 已被销毁的球)
            for (int i = _balls.Count - 1; i >= 0; i--)
            {
                if (_balls[i] == null) { _balls.RemoveAt(i); continue; }
                int si = _balls[i].slotIndex;
                bool keep = si >= 0 && si < slots.Count && slots[si] != null && slots[si].isLoaded;
                if (!keep)
                {
                    var go = _balls[i].gameObject;
                    // 销毁前先禁交互器，避免射线 select 中对象被销毁 → MissingReferenceException 卡死
                    var inter = go.GetComponent<XRSimpleInteractable>();
                    if (inter != null) inter.enabled = false;
                    Destroy(go);
                    _balls.RemoveAt(i);
                }
            }

            // 只为已装载精灵的槽生成球(空槽不显示球)
            for (int i = 0; i < slots.Count; i++)
            {
                if (slots[i] == null || !slots[i].isLoaded) continue;
                if (_balls.Exists(b => b.slotIndex == i)) continue;
                var ball = CreateBall(i, true);
                _balls.Add(ball);
            }
        }

        private PokeBallObject CreateBall(int index, bool loaded)
        {
            GameObject go;

            if (pokeBallPrefab != null)
            {
                go = Instantiate(pokeBallPrefab, transform);
            }
            else
            {
                // 回退：程序生成球体占位
                go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                go.transform.SetParent(transform, false);
                go.transform.localScale = Vector3.one * ballDiameter;

                var col = go.GetComponent<SphereCollider>();
                if (col != null) col.isTrigger = true;

                var rb = go.AddComponent<Rigidbody>();
                rb.mass = 0.1f;
                rb.linearDamping = 0.5f;
                rb.angularDamping = 0.5f;
                rb.isKinematic = true;
                rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            }

            go.name = "Ball_" + index + (loaded ? "_loaded" : "_empty");

            // 视觉材质（装载亮色 / 空暗色）
            var mr = go.GetComponentInChildren<MeshRenderer>();
            if (mr != null)
                mr.sharedMaterial = loaded ? loadedBallMat : emptyBallMat;

            var ballObj = go.GetComponent<PokeBallObject>();
            if (ballObj == null) ballObj = go.AddComponent<PokeBallObject>();

            // 绑定槽位:左侧 3 颗(index 0-2) + 右侧 3 颗(index 3-5),各自从前往后排列
            int side = index < 3 ? -1 : 1;
            int local = index % 3;
            Vector3 slotPos = new Vector3(side * sideSpacing, 0f, ballSpacing * -local);
            ballObj.BindToBelt(index, transform, slotPos);

            // 绑定槽位精灵（投掷命中后决定出战精灵）
            var gm = GameManager.Instance;
            if (gm != null && index < gm.playerData.pokeBallSlots.Count)
            {
                var slot = gm.playerData.pokeBallSlots[index];
                ballObj.sprite = (slot != null && slot.isLoaded) ? slot.sprite : null;
            }
            return ballObj;
        }

        /// <summary>
        /// 查找距手柄最近的、仍在腰带上的可抓取球
        /// </summary>
        public bool TryGetGrabbableBall(Vector3 handPos, float radius, out PokeBallObject result)
        {
            result = null;
            PokeBallObject nearest = null;
            float nearestSqr = radius * radius;

            foreach (var ball in _balls)
            {
                if (ball == null || ball.State != PokeBallState.OnBelt) continue;
                float d = (handPos - ball.transform.position).sqrMagnitude;
                if (d <= nearestSqr)
                {
                    nearestSqr = d;
                    nearest = ball;
                }
            }

            result = nearest;
            return result != null;
        }
    }
}
