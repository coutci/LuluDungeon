using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactors;
using UnityEngine.XR.Interaction.Toolkit.Interactors.Casters;
using UnityEngine.XR.Interaction.Toolkit.Interactors.Visuals;

namespace LuluDungeon
{
    /// <summary>
    /// 手柄射线末端光点：射线命中物体/UI 面板时，在命中点显示指示光点（跟随命中点实时移动）。
    /// 常驻对象（由 GameManager 自举创建），交互器列表每 0.5s 刷新一次。
    /// </summary>
    public class RayPointerDot : MonoBehaviour
    {
        public static RayPointerDot Instance { get; private set; }

        private Transform _dot;
        private readonly List<XRBaseInteractor> _interactors = new List<XRBaseInteractor>();
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
            RefreshInteractors();
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
            outer.transform.localScale = new Vector3(0.065f, 0.065f, 0.014f);
            var outerCol = outer.GetComponent<Collider>();
            if (outerCol != null) Destroy(outerCol);
            var outerShader = Shader.Find("Universal Render Pipeline/Unlit");
            if (outerShader == null) outerShader = Shader.Find("Unlit/Color");
            var outerMat = new Material(outerShader);
            outerMat.color = new Color(0.95f, 0.45f, 0.10f);
            outer.GetComponent<MeshRenderer>().sharedMaterial = outerMat;

            // 中心亮点（亮黄球）
            var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            go.name = "Dot";
            go.transform.SetParent(transform, false);
            go.transform.localScale = Vector3.one * 0.026f;
            var col = go.GetComponent<Collider>();
            if (col != null) Destroy(col);
            var mr = go.GetComponent<MeshRenderer>();
            var shader = Shader.Find("Universal Render Pipeline/Unlit");
            if (shader == null) shader = Shader.Find("Unlit/Color");
            var mat = new Material(shader);
            mat.color = new Color(1f, 0.90f, 0.30f);
            mr.sharedMaterial = mat;

            _dot = go.transform;
            _dot.gameObject.SetActive(false);
        }

        private void RefreshInteractors()
        {
            _interactors.Clear();
            var all = FindObjectsByType<XRBaseInteractor>(FindObjectsSortMode.None);
            foreach (var inter in all)
                if (inter != null) _interactors.Add(inter);

            // 全局收紧射线命中（任意场景生效）：默认 ConeCast 半径 0.1m 会同时罩住多个按钮导致误触
            foreach (var caster in FindObjectsByType<CurveInteractionCaster>(FindObjectsSortMode.None))
            {
                caster.sphereCastRadius = Mathf.Min(caster.sphereCastRadius, 0.01f);
                caster.hitDetectionType = CurveInteractionCaster.HitDetectionType.Raycast;
            }
            foreach (var ray in FindObjectsByType<XRRayInteractor>(FindObjectsSortMode.None))
            {
                ray.sphereCastRadius = Mathf.Min(ray.sphereCastRadius, 0.01f);
                ray.hitDetectionType = XRRayInteractor.HitDetectionType.Raycast;
            }
        }

        private void Update()
        {
            if (_dot == null) return;

            _refreshTimer -= Time.deltaTime;
            if (_refreshTimer <= 0f)
            {
                _refreshTimer = 0.5f;
                RefreshInteractors();
            }

            bool visible = false;
            Vector3 pos = Vector3.zero;

            foreach (var inter in _interactors)
            {
                if (inter == null || !inter.isActiveAndEnabled) continue;

                var ray = inter as XRRayInteractor;
                if (ray != null)
                {
                    // 3D 命中（按钮/可交互物）
                    if (ray.TryGetCurrent3DRaycastHit(out var hit)) { pos = hit.point; visible = true; break; }
                    // UI 命中（面板/按钮）
                    if (ray.TryGetCurrentUIRaycastResult(out var ui) && ui.isValid) { pos = ui.worldPosition; visible = true; break; }
                    continue;
                }

                var nf = inter as NearFarInteractor;
                if (nf != null)
                {
                    // UI 命中（uGUI 面板）
                    if (nf.TryGetCurrentUIRaycastResult(out var ui) && ui.isValid) { pos = ui.worldPosition; visible = true; break; }
                    // 射线命中（3D collider 按钮/可交互物）：曲线端点 = 命中点
                    var epType = nf.TryGetCurveEndPoint(out var ep);
                    if (epType == EndPointType.ValidCastHit || epType == EndPointType.UI)
                    {
                        pos = ep;
                        visible = true;
                        break;
                    }
                }
            }

            if (visible)
            {
                _dot.position = pos;
                _dot.gameObject.SetActive(true);
            }
            else
            {
                _dot.gameObject.SetActive(false);
            }
        }
    }
}
