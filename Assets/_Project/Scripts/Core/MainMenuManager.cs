using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

namespace LuluDungeon
{
    /// <summary>
    /// 主菜单：开始新游戏 / 读取存档（含删除确认）/ 退出游戏
    /// UI 为 World Space Canvas（uGUI 代码构建，与商店/面板同风格，手柄射线点击）
    /// </summary>
    public class MainMenuManager : MonoBehaviour
    {
        public const string DungeonScene = "SampleScene";

        private const float PanelDist = 0.7f;    // 面板与玩家距离
        private const float PanelHeight = -0.10f; // 面板相对相机高度（略低于视线，与商店面板一致）

        private GameObject _root;          // 面板根（Canvas 挂载点，每帧跟随相机）
        private GameObject _mainRoot;      // 主面板（三按钮）
        private GameObject _listRoot;      // 存档列表面板
        private GameObject _confirmRoot;   // 删除确认面板
        private UnityEngine.UI.Text _tipText;
        private UnityEngine.UI.Text _confirmText;
        private Canvas _canvas;

        private int _pendingDeleteSlot = -1;   // 待确认删除的槽号

        private GameManager _gm;
        private DataManager _dm;

        private void Start()
        {
            _gm = GameManager.Instance;
            _dm = DataManager.Instance;
            if (_gm == null || _dm == null)
            {
                Debug.LogError("[MainMenu] GameManager/DataManager missing in MainMenu scene!");
                return;
            }

            CreateMainMenuUI();
            ShowMain();

            // 主菜单 BGM(使用地牢探索曲,轻快冒险感)
            if (AudioManager.Instance != null) AudioManager.Instance.PlayBgm(BgmType.Dungeon);        }

        // ==================== 面板构建 ====================

        private void CreateMainMenuUI()
        {
            _root = new GameObject("MainMenuRoot");

            var canvasGo = new GameObject("Canvas");
            canvasGo.transform.SetParent(_root.transform, false);
            var canvas = canvasGo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            canvas.worldCamera = Camera.main != null ? Camera.main : FindAnyObjectByType<Camera>();
            canvasGo.GetComponent<RectTransform>().localScale = new Vector3(0.001f, 0.001f, 0.001f);
            _canvas = canvas;

            CreateUIText(canvasGo.transform, "LuluDungeon 地牢", new Color(0.75f, 0.40f, 0.15f), 36, new Vector3(0f, 252f, -40f), new Vector2(900f, 100f));

            // ---- 主面板 ----
            _mainRoot = new GameObject("MainPanel");
            _mainRoot.transform.SetParent(canvasGo.transform, false);
            CreateImage(_mainRoot.transform, new Vector2(760f, 560f), new Color(0.36f, 0.24f, 0.14f, 1f), Vector3.zero);
            CreateImage(_mainRoot.transform, new Vector2(730f, 530f), new Color(0.97f, 0.90f, 0.77f, 1f), Vector3.zero);

            CreateButton(_mainRoot.transform, "开始新游戏", 130f, new Vector2(700f, 65f), StartNewGame);
            CreateButton(_mainRoot.transform, "读取存档", 25f, new Vector2(700f, 65f), ShowSlotList);
            CreateButton(_mainRoot.transform, "退出游戏", -80f, new Vector2(700f, 65f), QuitGame);

            _tipText = CreateUIText(canvasGo.transform, "", new Color(0.80f, 0.20f, 0.10f), 18, new Vector3(0f, -200f, 0f), new Vector2(900f, 60f));

            // ---- 存档列表面板 ----
            _listRoot = new GameObject("SlotListPanel");
            _listRoot.transform.SetParent(canvasGo.transform, false);
            CreateImage(_listRoot.transform, new Vector2(820f, 660f), new Color(0.36f, 0.24f, 0.14f, 1f), Vector3.zero);
            CreateImage(_listRoot.transform, new Vector2(790f, 630f), new Color(0.97f, 0.90f, 0.77f, 1f), Vector3.zero);
            CreateUIText(_listRoot.transform, "读取存档", new Color(0.75f, 0.40f, 0.15f), 32, new Vector3(0f, 285f, 0f), new Vector2(900f, 80f));
            CreateButton(_listRoot.transform, "返回", -290f, new Vector2(220f, 60f), ShowMain);
            _listRoot.SetActive(false);

            // ---- 删除确认面板 ----
            _confirmRoot = new GameObject("ConfirmPanel");
            _confirmRoot.transform.SetParent(canvasGo.transform, false);
            _confirmRoot.transform.localPosition = new Vector3(0f, 0f, -40f);
            CreateImage(_confirmRoot.transform, new Vector2(580f, 320f), new Color(0.28f, 0.18f, 0.10f, 1f), Vector3.zero);
            CreateImage(_confirmRoot.transform, new Vector2(550f, 290f), new Color(0.97f, 0.90f, 0.77f, 1f), Vector3.zero);
            _confirmText = CreateUIText(_confirmRoot.transform, "删除该存档？", new Color(0.75f, 0.40f, 0.15f), 24, new Vector3(0f, 90f, 0f), new Vector2(520f, 60f));
            CreateButton(_confirmRoot.transform, "确认删除", 10f, new Vector2(240f, 60f), ConfirmDelete);
            CreateButton(_confirmRoot.transform, "取消", -80f, new Vector2(240f, 60f), CancelDelete);
            _confirmRoot.SetActive(false);

            // 面板定位：跟随相机（位置 + 朝向由 Update 每帧维持）
            FollowCamera(_root);
        }

        private void Update()
        {
            if (_root != null) FollowCamera(_root);
        }

        /// <summary>始终跟随头显：面板位于相机前方略下方，正面朝向玩家（位置 SmoothDamp + 旋转 Slerp 阻尼，避免抖动）</summary>
        private Vector3 _followVelocity;

        private void FollowCamera(GameObject go)
        {
            var cam = Camera.main != null ? Camera.main : FindAnyObjectByType<Camera>();
            if (cam == null) return;
            Vector3 fwd = cam.transform.forward;
            fwd.y = 0f;
            fwd.Normalize();
            Vector3 targetPos = cam.transform.position + fwd * PanelDist + Vector3.up * PanelHeight;
            go.transform.position = Vector3.SmoothDamp(go.transform.position, targetPos, ref _followVelocity, 0.15f);
            Quaternion targetRot = Quaternion.LookRotation(cam.transform.position - targetPos)
                                 * Quaternion.Euler(0f, 180f, 0f);
            go.transform.rotation = Quaternion.Slerp(go.transform.rotation, targetRot, 1f - Mathf.Exp(-10f * Time.deltaTime));
        }

        private void BuildSlotRows()
        {
            // 清掉旧行（保留标题/返回按钮）
            var toRemove = new List<GameObject>();
            foreach (Transform child in _listRoot.transform)
                if (child.name.StartsWith("SlotRow_"))
                    toRemove.Add(child.gameObject);
            foreach (var go in toRemove) Destroy(go);

            var summaries = _dm.GetSlotSummaries();
            var occupied = new HashSet<int>();
            foreach (var s in summaries) occupied.Add(s.slot);

            for (int i = 1; i <= DataManager.MaxSlots; i++)
            {
                int slot = i;   // 局部副本：避免 for 循环闭包陷阱（循环结束后 i == MaxSlots+1）
                float y = 200f - (slot - 1) * 82f;
                var row = new GameObject("SlotRow_" + i);
                row.transform.SetParent(_listRoot.transform, false);
                row.transform.localPosition = new Vector3(0f, y, 0f);

                CreateImage(row.transform, new Vector2(760f, 70f), new Color(0.36f, 0.24f, 0.14f, 1f), Vector3.zero);
                CreateImage(row.transform, new Vector2(730f, 62f), new Color(0.85f, 0.78f, 0.55f, 1f), Vector3.zero);

                if (occupied.Contains(i))
                {
                    var info = summaries.Find(s => s.slot == i);
                    string time = string.IsNullOrEmpty(info.saveTime) ? "时间未知" : info.saveTime;
                    CreateUIText(row.transform, $"存档{i} · 第{info.floor}层 · {time}",
                        new Color(0.28f, 0.18f, 0.08f), 18, new Vector3(-130f, 0f, 0f), new Vector2(420f, 50f));

                    CreateMiniButton(row.transform, "读档", 250f, new Vector2(120f, 52f), () => LoadSlot(slot));
                    CreateMiniButton(row.transform, "删除", 355f, new Vector2(100f, 52f), () => AskDelete(slot));
                }
                else
                {
                    CreateUIText(row.transform, $"存档{i} · 空", new Color(0.55f, 0.45f, 0.30f), 18, new Vector3(-130f, 0f, 0f), new Vector2(420f, 50f));
                }
            }
        }

        // ==================== 逻辑 ====================

        private void ShowMain()
        {
            _listRoot.SetActive(false);
            _confirmRoot.SetActive(false);
            _mainRoot.SetActive(true);
        }

        private void ShowSlotList()
        {
            BuildSlotRows();
            _mainRoot.SetActive(false);
            _listRoot.SetActive(true);
        }

        private void StartNewGame()
        {
            int slot = _dm.FirstEmptySlot();
            if (slot < 0)
            {
                ShowTip("存档已满（5/5），请先删除一个存档");
                return;
            }
            _gm.StartNewGame(slot);
            UnityEngine.SceneManagement.SceneManager.LoadScene(DungeonScene);
        }

        private void LoadSlot(int slot)
        {
            if (_gm.LoadGameFromSlot(slot))
            {
                UnityEngine.SceneManagement.SceneManager.LoadScene(DungeonScene);
            }
            else
            {
                ShowTip($"存档{slot}无法读取");
            }
        }

        private void AskDelete(int slot)
        {
            _pendingDeleteSlot = slot;
            if (_confirmText != null)
                _confirmText.text = $"确定删除 存档{slot} ？此操作不可恢复";
            _confirmRoot.SetActive(true);
        }

        private void ConfirmDelete()
        {
            if (_pendingDeleteSlot >= 1)
            {
                _dm.DeleteSave(_pendingDeleteSlot);
                _pendingDeleteSlot = -1;
                _confirmRoot.SetActive(false);
                BuildSlotRows();   // 刷新列表
            }
        }

        private void CancelDelete()
        {
            _pendingDeleteSlot = -1;
            _confirmRoot.SetActive(false);
        }

        private void QuitGame()
        {
            Debug.Log("[MainMenu] Quitting...");
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }

        private void ShowTip(string msg)
        {
            if (_tipText == null) return;
            _tipText.text = msg;
            StopCoroutine(nameof(ClearTip));
            StartCoroutine(ClearTip());
        }

        private IEnumerator ClearTip()
        {
            yield return new WaitForSeconds(3f);
            if (_tipText != null) _tipText.text = "";
        }

        // ==================== 工具 ====================

        private void CreateButton(Transform parent, string label, float y, Vector2 size, UnityAction onClick)
        {
            var btn = new GameObject("Btn_" + label);
            btn.transform.SetParent(parent, false);
            btn.transform.localPosition = new Vector3(0f, y, 0f);

            CreateImage(btn.transform, size, new Color(0.36f, 0.24f, 0.14f, 1f), Vector3.zero);
            CreateImage(btn.transform, size - new Vector2(10f, 10f), new Color(0.85f, 0.78f, 0.55f, 1f), Vector3.zero);
            CreateUIText(btn.transform, label, new Color(0.28f, 0.18f, 0.08f), 20, Vector3.zero, size);

            AddCollider(btn, size);
            AddTrigger(btn, onClick);
        }

        private void CreateMiniButton(Transform parent, string label, float x, Vector2 size, UnityAction onClick)
        {
            var btn = new GameObject("Mini_" + label);
            btn.transform.SetParent(parent, false);
            btn.transform.localPosition = new Vector3(x, 0f, 0f);

            CreateImage(btn.transform, size, new Color(0.36f, 0.24f, 0.14f, 1f), Vector3.zero);
            CreateImage(btn.transform, size - new Vector2(10f, 10f),
                label == "删除" ? new Color(0.90f, 0.42f, 0.35f, 1f) : new Color(0.85f, 0.78f, 0.55f, 1f), Vector3.zero);
            CreateUIText(btn.transform, label, new Color(0.28f, 0.18f, 0.08f), 18, Vector3.zero, size);

            AddCollider(btn, size);
            AddTrigger(btn, onClick);
        }

        private static void AddCollider(GameObject go, Vector2 size)
        {
            var box = go.AddComponent<BoxCollider>();
            box.size = new Vector3(size.x * 0.001f, size.y * 0.001f, 0.06f);
        }

        private static void AddTrigger(GameObject go, UnityAction callback)
        {
            var inter = go.AddComponent<UnityEngine.XR.Interaction.Toolkit.Interactables.XRSimpleInteractable>();
            inter.selectEntered.AddListener(_ =>
            {
                // UI 点击音效
                if (AudioManager.Instance != null) AudioManager.Instance.PlaySfx("Audio/SFX/sfx_ui_click");
                callback();
            });        }

        private static UnityEngine.UI.Image CreateImage(Transform parent, Vector2 size, Color color, Vector3 localPos)
        {
            var go = new GameObject("Image");
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            var img = go.AddComponent<UnityEngine.UI.Image>();
            img.color = color;
            img.rectTransform.sizeDelta = size;
            return img;
        }

        private static UnityEngine.UI.Text CreateUIText(Transform parent, string content, Color color, float fontSize, Vector3 localPos, Vector2 size)
        {
            var go = new GameObject("UIText");
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            var rt = go.AddComponent<RectTransform>();
            rt.sizeDelta = size;
            var text = go.AddComponent<UnityEngine.UI.Text>();
            text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            text.text = content;
            text.fontSize = (int)fontSize;
            text.color = color;
            text.alignment = TextAnchor.MiddleCenter;
            text.horizontalOverflow = HorizontalWrapMode.Overflow;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            return text;
        }
    }
}
