using System.Collections;
using UnityEngine;

namespace LuluDungeon
{
    /// <summary>
    /// 精灵球状态
    /// </summary>
    public enum PokeBallState
    {
        OnBelt,     // 在腰带上（kinematic，可被抓取）
        InHand,     // 持在手中（kinematic，跟随手柄）
        Thrown      // 飞行中/落地（物理模拟）
    }

    /// <summary>
    /// 精灵球运行时组件：负责腰带/手心/飞行三种状态切换，
    /// 出手、回归腰带动画，以及命中野生精灵的事件。
    /// </summary>
    public class PokeBallObject : MonoBehaviour
    {
        public int slotIndex = -1;
        public LuluDungeon.SpriteInstance sprite;   // 该球对应的精灵（投掷命中后决定出战精灵）
        public bool HitMonster { get; set; }
        public PokeBallState State { get; private set; } = PokeBallState.OnBelt;

        public event System.Action<PokeBallObject> OnThrown;
        public event System.Action<PokeBallObject, GameObject> OnHitMonster;
        public event System.Action<PokeBallObject> OnReturnedToBelt;

        [Header("组件引用")]
        [SerializeField] private Rigidbody _rb;
        [SerializeField] private Collider _col;

        private Transform _beltTransform;
        private Vector3 _beltLocalPos;
        private Coroutine _returnCoroutine;

        private void Awake()
        {
            if (_rb == null) _rb = GetComponent<Rigidbody>();
            if (_col == null) _col = GetComponentInChildren<Collider>();
        }

        /// <summary>
        /// 绑定到腰带槽位并吸附回腰带
        /// </summary>
        public void BindToBelt(int index, Transform belt, Vector3 localPos)
        {
            slotIndex = index;
            _beltTransform = belt;
            _beltLocalPos = localPos;
            SnapToBelt();
        }

        /// <summary>
        /// 球在腰带上的世界坐标（用于"腰带附近松手归位"判定）
        /// </summary>
        public Vector3 BeltWorldPosition =>
            _beltTransform != null ? _beltTransform.TransformPoint(_beltLocalPos) : transform.position;

        /// <summary>
        /// 立即回到腰带槽位
        /// </summary>
        public void SnapToBelt()
        {
            StopReturn();
            State = PokeBallState.OnBelt;
            HitMonster = false;
            if (_rb != null) _rb.isKinematic = true;
            SetColliderEnabled(false);
            if (_beltTransform != null)
            {
                transform.SetParent(_beltTransform, false);
                transform.localPosition = _beltLocalPos;
                transform.localRotation = Quaternion.identity;
            }
        }

        /// <summary>
        /// 抓到手上
        /// </summary>
        public void GrabToHand(Transform hand, Vector3 localPos)
        {
            if (State == PokeBallState.Thrown) return;
            StopReturn();
            State = PokeBallState.InHand;
            HitMonster = false;
            if (_rb != null) _rb.isKinematic = true;
            SetColliderEnabled(false);
            transform.SetParent(hand, false);
            transform.localPosition = localPos;
            transform.localRotation = Quaternion.identity;
        }

        /// <summary>
        /// 出手：脱离手，按给定速度飞行
        /// </summary>
        public void Throw(Vector3 velocity, Vector3 angularVelocity)
        {
            State = PokeBallState.Thrown;
            transform.SetParent(null, true);
            if (_rb != null)
            {
                _rb.isKinematic = false;
                _rb.linearVelocity = velocity;
                _rb.angularVelocity = angularVelocity;
            }
            SetColliderEnabled(true);
            OnThrown?.Invoke(this);
        }

        /// <summary>
        /// 飞行结束后飞回腰带槽位（带弧线动画）
        /// </summary>
        public void FlyBackToBelt(float duration = 0.5f)
        {
            if (_beltTransform == null) { SnapToBelt(); return; }
            StopReturn();
            _returnCoroutine = StartCoroutine(ReturnFlight(duration));
        }

        private IEnumerator ReturnFlight(float duration)
        {
            if (_rb != null) _rb.isKinematic = true;
            SetColliderEnabled(false);

            Vector3 start = transform.position;
            Quaternion startRot = transform.rotation;
            Vector3 target = _beltTransform.TransformPoint(_beltLocalPos);
            float t = 0f;

            while (t < 1f)
            {
                t += Time.deltaTime / Mathf.Max(0.05f, duration);
                Vector3 pos = Vector3.Lerp(start, target, t);
                pos.y += Mathf.Sin(t * Mathf.PI) * 0.25f;   // 小弧线
                transform.position = pos;
                transform.rotation = Quaternion.Slerp(startRot, _beltTransform.rotation, t);
                yield return null;
            }

            SnapToBelt();
            OnReturnedToBelt?.Invoke(this);
        }

        private void StopReturn()
        {
            if (_returnCoroutine != null)
            {
                StopCoroutine(_returnCoroutine);
                _returnCoroutine = null;
            }
        }

        private void OnCollisionEnter(Collision collision)
        {
            if (State != PokeBallState.Thrown) return;
            var ws = collision.collider.GetComponentInParent<WildSprite>();
            if (ws != null)
            {
                OnHitMonster?.Invoke(this, ws.gameObject);
                return;
            }
            var monster = collision.collider.GetComponentInParent<Monster>();
            if (monster != null)
            {
                OnHitMonster?.Invoke(this, monster.gameObject);
                return;
            }
            // Boss 持有者（Specter）：命中本体触发 Boss 战
            var trainer = collision.collider.GetComponentInParent<BossTrainer>();
            if (trainer != null)
            {
                OnHitMonster?.Invoke(this, trainer.gameObject);
            }
        }

        private void SetColliderEnabled(bool enabled)
        {
            if (_col != null) _col.enabled = enabled;
        }
    }
}
