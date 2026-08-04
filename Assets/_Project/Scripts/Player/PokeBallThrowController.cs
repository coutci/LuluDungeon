using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

namespace LuluDungeon
{
    /// <summary>
    /// 精灵球投掷控制器（右手）
    /// 流程：右手靠近腰带球 → 按住侧键(Grip)吸附 → 手柄延伸抛物线 → 按扳机(Trigger)掷出
    /// 命中怪物弹"进入战斗"提示；落空则落地停稳/超时后自动回腰带
    /// </summary>
    public class PokeBallThrowController : MonoBehaviour
    {
        [Header("抓取")]
        public float grabRadius = 0.15f;
        public Vector3 holdOffset = new Vector3(0f, 0f, 0.08f);   // 手心前方
        [Header("抛物线瞄准")]
        public float throwSpeed = 9f;              // 出手初速度大小（抛物线强度）
        public float aimAngleUp = 10f;             // 瞄准线上仰角（度），让弧线更明显
        public int parabolaPoints = 30;            // 抛物线预览采样点数
        public float parabolaStep = 0.05f;         // 相邻采样点时间间隔（秒）
        public float parabolaMaxTime = 1.5f;       // 抛物线预览最长模拟时间（秒）
        public float parabolaMinHeight = 0.1f;     // 预览低于起点该高度即截断
        public Material lineMaterial;              // 可选：瞄准线材质（留空用内置 Sprites/Default）

        [Header("投掷后")]
        public float settleSpeed = 0.15f;            // 视为停稳的速度阈值
        public float settleTime = 0.2f;              // 停稳持续时长
        public float throwTimeout = 6f;              // 未停稳超时回归
        public float hitReturnDelay = 1.8f;          // 命中怪物后延迟回归

        private PokeBallBelt _belt;
        private PokeBallObject _heldBall;
        private Transform _rightHand;
        private GameManager _gm;
        private InputDevice _rightDevice;
        private LineRenderer _aimLine;
        private static bool _grabDiagLogged;

        private void Start()
        {
            _gm = GameManager.Instance;
            _belt = GetComponent<PokeBallBelt>();
            if (_belt == null) _belt = FindFirstObjectByType<PokeBallBelt>();
            _rightDevice = InputDevices.GetDeviceAtXRNode(XRNode.RightHand);
            FindRightHand();
        }

        private void FindRightHand()
        {
            // 优先：交互器或其祖先路径含 "Right"（如 Right Controller/Teleport Interactor）
            var rayInteractors = FindObjectsByType<XRRayInteractor>(FindObjectsSortMode.None);
            foreach (var ri in rayInteractors)
            {
                if (IsUnderRightSide(ri.transform)) { _rightHand = ri.transform; return; }
            }

            // 其次：真实右手控制器（TrackedPoseDriver 由手柄追踪驱动，旋转与手柄一致）
            var drivers = FindObjectsByType<UnityEngine.InputSystem.XR.TrackedPoseDriver>(FindObjectsSortMode.None);
            foreach (var d in drivers)
            {
                if (IsUnderRightSide(d.transform)) { _rightHand = d.transform; return; }
            }

            // 再次：名字精确匹配 "Right Controller"（PICO 场景无 XRRayInteractor，手柄根即此名）
            var allTransforms = FindObjectsByType<Transform>(FindObjectsSortMode.None);
            foreach (var t in allTransforms)
            {
                if (t.name == "Right Controller") { _rightHand = t; return; }
            }

            // 兜底：任意名字含 "Right" 且含 "Controller" 的对象（含稳定化辅助对象，保证无头显环境可用）
            var transforms = FindObjectsByType<Transform>(FindObjectsSortMode.None);
            foreach (var t in transforms)
            {
                if (t.name.IndexOf("Right", System.StringComparison.OrdinalIgnoreCase) >= 0 &&
                    t.name.IndexOf("Controller", System.StringComparison.OrdinalIgnoreCase) >= 0)
                { _rightHand = t; return; }
            }

            // 最后兜底：第一个交互器
            if (rayInteractors.Length > 0) _rightHand = rayInteractors[0].transform;
        }

        private static bool IsUnderRightSide(Transform t)
        {
            var cur = t;
            while (cur != null)
            {
                if (cur.name.IndexOf("Right", System.StringComparison.OrdinalIgnoreCase) >= 0)
                    return true;
                cur = cur.parent;
            }
            return false;
        }

        private void Update()
        {
            if (_gm == null) _gm = GameManager.Instance;
            if (_gm != null && (_gm.IsInBattle || _gm.IsPaused)) return;
            if (_rightHand == null)
            {
                FindRightHand();
                if (_rightHand == null) return;
            }

            bool grip = InputDevicesHelper.IsRightGripPressed();

            if (_heldBall == null)
            {
                if (grip) TryGrab();
                else HideAimLine();
            }
            else
            {
                // 持球状态：实时更新抛物线预览
                UpdateAimPreview();

                // 扳机键 → 沿抛物线掷出；松开 Grip 未投 → 放回腰带
                if (InputDevicesHelper.IsRightTriggerDown())
                    ReleaseThrow();
                else if (!grip)
                    CancelToBelt();
            }
        }

        private void TryGrab()
        {
            if (_belt == null)
            {
                LogGrabDiagnostic("belt=null");
                return;
            }
            if (_belt.TryGetGrabbableBall(_rightHand.position, grabRadius, out var ball))
            {
                _heldBall = ball;
                ball.OnHitMonster -= OnBallHitMonster;
                ball.OnHitMonster += OnBallHitMonster;
                ball.GrabToHand(_rightHand, holdOffset);
                Haptic(0.15f, 0.3f);
            }
            else
            {
                LogGrabDiagnostic("no ball in range");
            }
        }

        /// <summary>
        /// 抓取失败时输出一次诊断信息，帮助定位 Play 模式输入问题
        /// </summary>
        private void LogGrabDiagnostic(string reason)
        {
            if (_grabDiagLogged) return;
            _grabDiagLogged = true;

            var balls = FindObjectsByType<PokeBallObject>(FindObjectsSortMode.None);
            float nearest = float.MaxValue;
            foreach (var b in balls)
                nearest = Mathf.Min(nearest, Vector3.Distance(_rightHand.position, b.transform.position));

            Debug.Log($"[ThrowDiag] reason={reason} rightHand={_rightHand?.name} " +
                      $"{InputDevicesHelper.GetRightHandDiagnostic()} " +
                      $"balls={balls.Length} nearestDist={(nearest == float.MaxValue ? -1f : nearest):F2} grabRadius={grabRadius}");
        }

        private void ReleaseThrow()
        {
            if (_heldBall == null) return;

            var ball = _heldBall;
            _heldBall = null;
            HideAimLine();

            // 沿当前瞄准抛物线掷出（手柄朝向 + 上仰角，速度大小 = throwSpeed）
            Vector3 vel = ComputeAimVelocity();
            ball.Throw(vel, Vector3.zero);
            // 投掷音效
            if (AudioManager.Instance != null) AudioManager.Instance.PlaySfx("Audio/SFX/sfx_throw");
            Haptic(0.3f, 0.5f);            StartCoroutine(MonitorBall(ball));
        }

        private void UpdateAimPreview()
        {
            if (_rightHand == null) return;
            var line = GetOrCreateAimLine();
            Vector3 start = _rightHand.position;
            var rayInteractor = _rightHand.GetComponent<XRRayInteractor>();
            if (rayInteractor != null) rayInteractor.GetLineOriginAndDirection(out start, out _);
            Vector3 vel = ComputeAimVelocity();
            Vector3 g = Physics.gravity;

            int count = Mathf.Min(parabolaPoints, Mathf.CeilToInt(parabolaMaxTime / Mathf.Max(0.01f, parabolaStep)));
            var points = new Vector3[count];
            int n = 0;
            for (int i = 0; i < count; i++)
            {
                float t = i * parabolaStep;
                Vector3 pos = start + vel * t + 0.5f * g * t * t;
                points[n++] = pos;
                // 低于起点一定高度即截断（落地）
                if (pos.y < start.y - parabolaMinHeight) break;
            }

            line.positionCount = n;
            line.SetPositions(points);
        }

        private Vector3 ComputeAimVelocity()
        {
            // 方向：优先取设备原生旋转（与 UI 射线/TrackedPoseDriver 同源，始终朝前）；
            // 设备无效时退回手柄 transform forward。叠加 aimAngleUp 上仰角，速度大小固定为 throwSpeed。
            Vector3 dir = Vector3.forward;
            if (_rightDevice.isValid &&
                _rightDevice.TryGetFeatureValue(UnityEngine.XR.CommonUsages.deviceRotation, out Quaternion devRot) &&
                devRot != Quaternion.identity)
            {
                dir = devRot * Vector3.forward;
            }
            if (_rightHand != null)
            {
                var rayInteractor = _rightHand.GetComponent<XRRayInteractor>();
                if (rayInteractor != null) rayInteractor.GetLineOriginAndDirection(out _, out dir);
                else dir = _rightHand.forward;
            }
            Quaternion aimRot = Quaternion.LookRotation(dir) * Quaternion.Euler(-aimAngleUp, 0f, 0f);
            return aimRot * Vector3.forward * throwSpeed;
        }

        private LineRenderer GetOrCreateAimLine()
        {
            if (_aimLine == null)
            {
                _aimLine = gameObject.AddComponent<LineRenderer>();
                _aimLine.useWorldSpace = true;
                _aimLine.positionCount = 0;
                _aimLine.startWidth = 0.02f;
                _aimLine.endWidth = 0.006f;
                _aimLine.numCapVertices = 4;
                _aimLine.startColor = new Color(1f, 0.95f, 0.4f, 0.9f);
                _aimLine.endColor = new Color(1f, 0.5f, 0.1f, 0.25f);
                if (lineMaterial != null) _aimLine.material = lineMaterial;
                else
                {
                    var shader = Shader.Find("Sprites/Default");
                    if (shader != null) _aimLine.material = new Material(shader);
                }
            }
            return _aimLine;
        }

        private void HideAimLine()
        {
            if (_aimLine != null && _aimLine.positionCount > 0)
                _aimLine.positionCount = 0;
        }

        /// <summary>
        /// 松开 Grip 且未按扳机 → 放回腰带（不投掷）
        /// </summary>
        private void CancelToBelt()
        {
            if (_heldBall == null) return;
            var ball = _heldBall;
            _heldBall = null;
            HideAimLine();
            ball.SnapToBelt();
            Haptic(0.1f, 0.2f);
        }

       

        private IEnumerator MonitorBall(PokeBallObject ball)
        {
            var rb = ball != null ? ball.GetComponent<Rigidbody>() : null;
            float timer = throwTimeout;
            float settled = 0f;

            while (ball != null && ball.State == PokeBallState.Thrown)
            {
                if (ball.HitMonster) yield break;   // 命中由 OnBallHitMonster 调度回归
                timer -= Time.deltaTime;
                if (timer <= 0f) break;

                if (rb != null && rb.linearVelocity.sqrMagnitude < settleSpeed * settleSpeed)
                {
                    settled += Time.deltaTime;
                    if (settled >= settleTime) break;
                }
                else settled = 0f;

                yield return null;
            }

            if (ball == null) yield break;
            if (ball.State == PokeBallState.Thrown)
                ball.FlyBackToBelt(0.5f);
        }

        private void OnBallHitMonster(PokeBallObject ball, GameObject monsterGo)
        {
            if (ball == null) return;
            ball.HitMonster = true;

            var ws = monsterGo != null ? monsterGo.GetComponentInParent<WildSprite>() : null;
            var monster = monsterGo != null ? monsterGo.GetComponentInParent<Monster>() : null;
            var trainer = monsterGo != null ? monsterGo.GetComponentInParent<BossTrainer>() : null;
            if (monster != null)
            {
                // 触发战斗入口（携带投掷球对应的精灵）
                monster.TriggerBattle(ball.sprite);
            }
            else if (trainer != null && !trainer.IsDefeated)
            {
                // 命中 Boss 本体 → 挑战 Boss（不可捕捉，失败整体回滚）
                trainer.TriggerBattle(ball.sprite);
            }
            else if (ws != null)
            {
                ws.ShowEnterBattlePrompt();
            }

            StartCoroutine(DelayedReturn(ball));
            // 命中怪物音效
            if (AudioManager.Instance != null) AudioManager.Instance.PlaySfx("Audio/SFX/sfx_hit_02");            StartCoroutine(DelayedReturn(ball));
        }

        private IEnumerator DelayedReturn(PokeBallObject ball)
        {
            yield return new WaitForSeconds(hitReturnDelay);
            if (ball != null && ball.State == PokeBallState.Thrown)
                ball.FlyBackToBelt(0.5f);
        }

        private void Haptic(float amplitude, float duration)
        {
            if (!_rightDevice.isValid) return;
            if (_rightDevice.TryGetHapticCapabilities(out var caps) && caps.supportsImpulse)
                _rightDevice.SendHapticImpulse(0u, amplitude, duration);
        }
    }
}
