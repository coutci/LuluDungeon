using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Interactors;
using UnityEngine.XR.Interaction.Toolkit.Interactors.Visuals;

namespace LuluDungeon
{
    /// <summary>
    /// 手柄射线末端光点：判定手柄射线与"可点击面板"（世界空间 Canvas）的相交位置，在交点显示光点。
    /// 纯数学求交（Plane + Rect 范围判定），不依赖 XR 命中状态，稳定可靠。
    /// 由 GameManager 自举创建，跨场景常驻；交互器与面板列表每 0.5s 刷新。
    /// </summary>
    public class RayPointerDot : MonoBehaviour
    {
        public static RayPointerDot Instance { get; private set; }

        private Transform _dot;
        private readonly List<XRBaseInteractor> _interactors = new List<XRBaseInteractor>();
        private readonly List<RectTransform> _panels = new List<RectTransform>();
        private float _refreshTimer;

        /// <summary>确保全局光点存在（GameManager.Awake 调用）</summary>
        public static void Ensure()
        {
            if (Instance != null) return;
            var go = new GameObject("RayPointerDotSystem");
            go.AddComponent<RayPointerDot>();
        }

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
            CreateDot();
            Refresh();
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        private void CreateDot()
        {
            // 外层靶心底衬（暗橙扁球）
            var outer = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            outer.name = "Outer";
            outer.transform.SetParent(transform, false);
            outer.transform.localScale = new Vector3(0.07f, 0.07f, 0.016f);
            var oc = outer.GetComponent<Collider>();
            if (oc != null) Destroy(oc);
            var outerShader = Shader.Find("Universal Render Pipeline/Unlit");
            if (outerShader == null) outerShader = Shader.Find("Unlit/Color");
            var om = new Material(outerShader);
            om.color = new Color(0.95f, 0.45f, 0.10f);
            outer.GetComponent<MeshRenderer>().sharedMaterial = om;

            // 中心亮点（亮黄球）
            var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            go.name = "Dot";
            go.transform.SetParent(transform, false);
            go.transform.localScale = Vector3.one * 0.028f;
            var col = go.GetComponent<Collider>();
            if (col != null) Destroy(col);
            var shader = Shader.Find("Universal Render Pipeline/Unlit");
            if (shader == null) shader = Shader.Find("Unlit/Color");
            var mat = new Material(shader);
            mat.color = new Color(1f, 0.90f, 0.30f);
            go.GetComponent<MeshRenderer>().sharedMaterial = mat;

            _dot = go.transform;
            _dot.gameObject.SetActive(false);
        }

        private void Refresh()
        {
            _interactors.Clear();
            var all = FindObjectsByType<XRBaseInteractor>(FindObjectsSortMode.None);
            foreach (var inter in all)
                if (inter != null && inter.isActiveAndEnabled) _interactors.Add(inter);

            _panels.Clear();
            foreach (var canvas in FindObjectsByType<Canvas>(FindObjectsSortMode.None))
            {
                if (canvas == null || !canvas.gameObject.activeInHierarchy) continue;
                if (canvas.renderMode != RenderMode.WorldSpace) continue;
                var rt = canvas.GetComponent<RectTransform>();
                if (rt == null) continue;
                if (rt.rect.width < 1f || rt.rect.height < 1f) continue;
                _panels.Add(rt);
            }
        }

        private void Update()
        {
            if (_dot == null) return;

            _refreshTimer -= Time.deltaTime;
            if (_refreshTimer <= 0f)
            {
                _refreshTimer = 0.5f;
                Refresh();
            }

            Vector3 pos;
            bool visible = false;
            foreach (var inter in _interactors)
            {
                if (inter == null || !inter.isActiveAndEnabled) continue;
                if (TryGetPanelPoint(inter.transform, out pos))
                {
                    _dot.position = pos;
                    visible = true;
                    break;
                }
            }

            _dot.gameObject.SetActive(visible);
        }

        /// <summary>
        /// 手柄射线与所有可点击面板求交：命中且交点落在面板矩形内 → 返回交点（沿射线前移 1.2cm 避免陷入面板平面）
        /// </summary>
        private bool TryGetPanelPoint(Transform hand, out Vector3 point)
        {
            point = Vector3.zero;
            if (hand == null) return false;

            Vector3 origin = hand.position;
            Vector3 dir = hand.forward;

            // 曲线射线（NearFarInteractor）用端点反推方向，比 forward 更准
            var nf = hand.GetComponentInParent<NearFarInteractor>();
            if (nf != null && nf.TryGetCurveEndPoint(out var end) != EndPointType.None && end != Vector3.zero)
            {
                Vector3 d = end - origin;
                if (d.sqrMagnitude > 0.0001f) dir = d.normalized;
            }
            var ray = new Ray(origin, dir);

            float best = float.MaxValue;
            foreach (var rt in _panels)
            {
                if (rt == null) continue;
                var plane = new Plane(rt.forward, rt.position);
                if (!plane.Raycast(ray, out float dist)) continue;
                if (dist < 0.05f || dist > 8f) continue;
                Vector3 hit = ray.GetPoint(dist);
                Vector3 local = rt.InverseTransformPoint(hit);
                var rect = rt.rect;
                if (Mathf.Abs(local.x) > rect.width * 0.5f) continue;
                if (Mathf.Abs(local.y) > rect.height * 0.5f) continue;
                if (dist < best) { best = dist; point = hit; }
            }

            if (best < float.MaxValue)
            {
                point += ray.direction * 0.012f;   // 前移，让光点完整显示在面板前方
                return true;
            }
            return false;
        }
    }
}
