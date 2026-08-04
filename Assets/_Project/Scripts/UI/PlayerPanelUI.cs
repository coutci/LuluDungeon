using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

namespace LuluDungeon
{
    /// <summary>
    /// 玩家个人面板(背包系统) - 卡通画框风格
    /// B 键(右手 secondaryButton)唤起/关闭;低头平板式始终跟随头显。
    /// 物品背包:4x6 格子网格(米色底+棕色描边),同物品堆叠(右下角数量下标),金币在右下角;
    /// 精灵背包:精灵列表(捕获球、腰间状态),可装到腰间/放回背包;
    /// 全部 UI 为 Canvas 元素(Image + UI.Text),按层级渲染,不存在深度遮挡问题。
    /// Canvas scale=0.001,坐标单位=像素(1px = 0.001m)。
    /// </summary>
    public class PlayerPanelUI : MonoBehaviour
    {
        public static PlayerPanelUI Instance { get; private set; }

        [Header("平板设置")]
        public float panelDistance = 0.7f;
        public float panelHeight = -0.10f;
        public float panelTilt = 8f;
        public Vector2 panelSize = new Vector2(740f, 480f);   // 像素(0.74m x 0.48m)

        // ===== 卡通配色(暖色系) =====
        private static readonly Color FrameColor   = new Color(0.36f, 0.24f, 0.14f, 1f);   // 深棕描边
        private static readonly Color PanelBg      = new Color(0.97f, 0.90f, 0.77f, 0.98f); // 米色面板
        private static readonly Color SlotBorder   = new Color(0.50f, 0.36f, 0.22f, 1f);   // 格子描边
        private static readonly Color SlotEmpty    = new Color(0.87f, 0.80f, 0.66f, 1f);   // 空格子
        private static readonly Color BallFill     = new Color(0.45f, 0.68f, 0.90f, 1f);   // 精灵球(蓝)
        private static readonly Color HealFill     = new Color(0.93f, 0.50f, 0.50f, 1f);   // 治疗药水(粉)
        private static readonly Color ReviveFill   = new Color(0.72f, 0.55f, 0.88f, 1f);   // 复活药水(紫)
        private static readonly Color ExpFill      = new Color(0.55f, 0.80f, 0.55f, 1f);   // 经验瓶(绿)
        private static readonly Color SkillFill = new Color(0.55f, 0.65f, 0.95f, 1f);   // 技能药水
        private static readonly Color BtnYellow    = new Color(0.99f, 0.82f, 0.38f, 1f);   // 主按钮(黄)
        private static readonly Color BtnGreen     = new Color(0.60f, 0.82f, 0.45f, 1f);   // 正面操作(绿)
        private static readonly Color BtnOrange    = new Color(0.95f, 0.60f, 0.30f, 1f);   // 反向操作(橙)
        private static readonly Color BtnRed       = new Color(0.90f, 0.42f, 0.35f, 1f);   // 关闭(红)
        private static readonly Color TitleColor   = new Color(0.75f, 0.40f, 0.15f, 1f);   // 标题(橙棕)
        private static readonly Color TextDark     = new Color(0.28f, 0.18f, 0.08f, 1f);   // 正文(深棕)
        private static readonly Color TextMuted    = new Color(0.55f, 0.44f, 0.30f, 1f);   // 次要文字
        private static readonly Color StatusColor  = new Color(0.75f, 0.25f, 0.15f, 1f);   // 状态提示(红棕)

        private GameObject _root;
        private Canvas _panelCanvas;
        private Text _titleText;
        private Text _statusText;
        private Text _goldText;
        private GameObject _backBtn;

        private GameObject _homeRoot;
        private GameObject _itemsRoot;
        private GameObject _spritesRoot;
        private GameObject _systemRoot;
        private Text _systemInfoText;

        private enum View { Home, Items, Sprites, System }
        private View _view = View.Home;

        private class ItemSlot
        {
            public Image fill;
            public Text nameText;
            public Text countText;
        }
        private readonly List<ItemSlot> _slots = new List<ItemSlot>();
        private readonly List<GameObject> _spriteRows = new List<GameObject>();

        private float _statusTimer;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
            DontDestroyOnLoad(gameObject);
            BuildPanel();
        }

        private void OnEnable()
        {
            UnityEngine.SceneManagement.SceneManager.sceneLoaded += OnSceneLoaded;
        }

        private void OnDisable()
        {
            UnityEngine.SceneManagement.SceneManager.sceneLoaded -= OnSceneLoaded;
        }

        /// <summary>
        /// 切场景（回主菜单/Game Over）时自动关闭面板：
        /// 面板是 DontDestroyOnLoad 跨场景保留的，若不关闭会跟随相机悬在主菜单面板前方，
        /// 遮挡 MainMenu 按钮并造成重叠。
        /// </summary>
        private void OnSceneLoaded(UnityEngine.SceneManagement.Scene scene, UnityEngine.SceneManagement.LoadSceneMode mode)
        {
            Close();
        }

        private void OnDestroy()
        {
            UnityEngine.SceneManagement.SceneManager.sceneLoaded -= OnSceneLoaded;
            if (Instance == this) Instance = null;
        }

        private void Update()
        {
            if (InputDevicesHelper.IsRightSecondaryDown())
                Toggle();

            if (!_root.activeSelf) return;

            FollowCamera(_root);

            if (_statusTimer > 0f)
            {
                _statusTimer -= Time.deltaTime;
                if (_statusTimer <= 0f && _statusText != null)
                    _statusText.text = "";
            }
        }

        // ===== 开/关 =====
        public void Toggle()
        {
            if (_root.activeSelf) Close();
            else Open();
        }

        public void Open()
        {
            _view = View.Home;
            ShowView();
            SnapToCamera();   // 打开即就位，避免阻尼飘入过程中点击不稳定
            _root.SetActive(true);
        }

        /// <summary>瞬移定位到相机前方（打开时用；之后由 Update 阻尼跟随）</summary>
        private void SnapToCamera()
        {
            var cam = Camera.main;
            if (cam == null || _root == null) return;
            Vector3 fwd = cam.transform.forward;
            fwd.y = 0;
            fwd.Normalize();
            _root.transform.position = cam.transform.position + fwd * panelDistance + Vector3.up * panelHeight;
            _root.transform.LookAt(cam.transform);
            _root.transform.Rotate(0, 180, 0);
            _root.transform.Rotate(panelTilt, 0, 0);
            _followVelocity = Vector3.zero;
        }

        public void Close()
        {
            _root.SetActive(false);
        }

        // ===== 构建 ===== 
        private void BuildPanel()
        {
            _root = new GameObject("PlayerPanel");
            DontDestroyOnLoad(_root);
            _root.SetActive(false);

            var canvasGo = new GameObject("Canvas");
            canvasGo.transform.SetParent(_root.transform, false);
            var canvas = canvasGo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            canvas.worldCamera = Camera.main;
            _panelCanvas = canvas;
            canvasGo.GetComponent<RectTransform>().localScale = new Vector3(0.001f, 0.001f, 0.001f);

            // 画框:外层深棕描边 + 内层米色面板
            var frameGo = new GameObject("Frame");
            frameGo.transform.SetParent(canvasGo.transform, false);
            var frameImg = frameGo.AddComponent<Image>();
            frameImg.color = FrameColor;
            frameImg.rectTransform.sizeDelta = panelSize + Vector2.one * 30f;

            var bgGo = new GameObject("BG");
            bgGo.transform.SetParent(canvasGo.transform, false);
            var bg = bgGo.AddComponent<Image>();
            bg.color = PanelBg;
            bg.rectTransform.sizeDelta = panelSize;

            // 标题 + 分隔线
            _titleText = CreateText(canvasGo.transform, "个人面板", TitleColor, 30, new Vector3(0f, 195f, 0f));
            var lineGo = new GameObject("TitleLine");
            lineGo.transform.SetParent(canvasGo.transform, false);
            lineGo.transform.localPosition = new Vector3(0f, 165f, 0f);
            var lineImg = lineGo.AddComponent<Image>();
            lineImg.color = BtnOrange;
            lineImg.rectTransform.sizeDelta = new Vector2(520f, 9f);

            _statusText = CreateText(canvasGo.transform, "", StatusColor, 17, new Vector3(-220f, -205f, 0f));

            CreateButton(canvasGo.transform, "X", new Vector3(300f, 195f, 0f), new Vector2(70f, 60f), BtnRed, Close, 24);
            _backBtn = CreateButton(canvasGo.transform, "返回", new Vector3(-300f, 195f, 0f), new Vector2(110f, 60f), BtnOrange, () => { _view = View.Home; ShowView(); }, 20);

            _homeRoot = new GameObject("HomeRoot");
            _homeRoot.transform.SetParent(canvasGo.transform, false);
            _itemsRoot = new GameObject("ItemsRoot");
            _itemsRoot.transform.SetParent(canvasGo.transform, false);
            _spritesRoot = new GameObject("SpritesRoot");
            _spritesRoot.transform.SetParent(canvasGo.transform, false);
            _systemRoot = new GameObject("SystemRoot");
            _systemRoot.transform.SetParent(canvasGo.transform, false);

            BuildHomeView();
            BuildItemsGrid();
            BuildSystemView();
        }

        private void BuildHomeView()
        {
            CreateButton(_homeRoot.transform, "物品背包", new Vector3(0f, 75f, 0f), new Vector2(340f, 70f), BtnYellow, () => { _view = View.Items; ShowView(); }, 26);
            CreateButton(_homeRoot.transform, "精灵背包", new Vector3(0f, -15f, 0f), new Vector2(340f, 70f), BtnYellow, () => { _view = View.Sprites; ShowView(); }, 26);
            CreateButton(_homeRoot.transform, "系统", new Vector3(0f, -105f, 0f), new Vector2(340f, 70f), BtnYellow, () => { _view = View.System; ShowView(); }, 26);
            CreateText(_homeRoot.transform, "按 B 键关闭面板", TextMuted, 17, new Vector3(0f, -200f, 0f));
        }

        // 4 列 x 6 行 = 24 格
        private void BuildItemsGrid()
        {
            float[] xs = { -270f, -90f, 90f, 270f };
            float[] ys = { 135f, 92f, 49f, 6f, -37f, -80f };

            for (int r = 0; r < 6; r++)
            {
                for (int c = 0; c < 4; c++)
                {
                    var slot = new ItemSlot();
                    var go = new GameObject("Slot_" + (r * 4 + c));
                    go.transform.SetParent(_itemsRoot.transform, false);
                    go.transform.localPosition = new Vector3(xs[c], ys[r], 0f);

                    // 描边(深棕)
                    var borderGo = new GameObject("Border");
                    borderGo.transform.SetParent(go.transform, false);
                    var borderImg = borderGo.AddComponent<Image>();
                    borderImg.color = SlotBorder;
                    borderImg.rectTransform.sizeDelta = new Vector2(150f, 38f);

                    // 填充(内层)
                    var fillGo = new GameObject("Fill");
                    fillGo.transform.SetParent(go.transform, false);
                    var fillImg = fillGo.AddComponent<Image>();
                    fillImg.color = SlotEmpty;
                    fillImg.rectTransform.sizeDelta = new Vector2(136f, 24f);
                    slot.fill = fillImg;

                    slot.nameText = CreateText(go.transform, "", TextDark, 15, new Vector3(0f, 6f, 0f));
                    slot.countText = CreateText(go.transform, "", TextDark, 13, new Vector3(52f, -9f, 0f));

                    _slots.Add(slot);
                }
            }

            // 金币:右下角(不占格子)
            _goldText = CreateText(_itemsRoot.transform, "", TitleColor, 17, new Vector3(240f, -205f, 0f));
        }

        // ===== 视图 =====
        private void ShowView()
        {
            _homeRoot.SetActive(_view == View.Home);
            _itemsRoot.SetActive(_view == View.Items);
            _spritesRoot.SetActive(_view == View.Sprites);
            _systemRoot.SetActive(_view == View.System);
            if (_backBtn != null) _backBtn.SetActive(_view != View.Home);

            _titleText.text = _view switch
            {
                View.Home => "个人面板",
                View.Items => "物品背包",
                View.Sprites => "精灵背包",
                View.System => "系统",
                _ => "个人面板"
            };

            switch (_view)
            {
                case View.Home: break;
                case View.Items: RefreshItemsView(); break;
                case View.Sprites: RefreshSpritesView(); break;
                case View.System: RefreshSystemView(); break;
            }
        }

        private void BuildSystemView()
        {
            _systemInfoText = CreateText(_systemRoot.transform, "", TitleColor, 20, new Vector3(0f, 110f, 0f));
            CreateText(_systemRoot.transform, "返回主菜单或退出前会自动保存", TextMuted, 16, new Vector3(0f, 55f, 0f));
            CreateButton(_systemRoot.transform, "保存并返回主菜单", new Vector3(0f, -20f, 0f), new Vector2(380f, 75f), BtnGreen, () =>
            {
                var gm = GameManager.Instance;
                if (gm != null) gm.ReturnToMainMenu();
            }, 22);
            CreateButton(_systemRoot.transform, "保存并退出游戏", new Vector3(0f, -115f, 0f), new Vector2(380f, 75f), BtnRed, () =>
            {
                var dm = DataManager.Instance;
                if (dm != null) dm.AutoSave();
                Time.timeScale = 1f;
#if UNITY_EDITOR
                UnityEditor.EditorApplication.isPlaying = false;
#else
                Application.Quit();
#endif
            }, 22);
        }

        private void RefreshSystemView()
        {
            var gm = GameManager.Instance;
            if (gm == null || _systemInfoText == null) return;
            string slotTxt = gm.activeSaveSlot >= 1 ? "存档" + gm.activeSaveSlot : "未绑定存档";
            _systemInfoText.text = $"{slotTxt} · 第{gm.playerData.currentFloor}层";
        }

        private void RefreshItemsView()
        {
            var gm = GameManager.Instance;
            if (gm == null) return;
            var pd = gm.playerData;

            // 8 种物品：有数量的紧凑排列，跳过空格子
            string[] itemNames = { "芯片球", "普通球", "高级球", "超级球", "治疗药水", "复活药水", "经验瓶", "技能药水" };
            int[] itemCounts =
            {
                pd.chipPokeBalls, pd.normalPokeBalls, pd.greatPokeBalls, pd.ultraPokeBalls,
                pd.healBottles, pd.reviveBottles, pd.expBottles, pd.skillBottles
            };
            Color[] itemFills = { BallFill, BallFill, BallFill, BallFill, HealFill, ReviveFill, ExpFill, SkillFill };

            int slotIdx = 0;
            for (int i = 0; i < itemNames.Length; i++)
            {
                if (itemCounts[i] <= 0) continue;
                if (slotIdx >= _slots.Count) break;
                var slot = _slots[slotIdx];
                slot.fill.color = itemFills[i];
                slot.nameText.text = itemNames[i];
                slot.countText.text = itemCounts[i] > 1 ? "x" + itemCounts[i] : "";
                slotIdx++;
            }
            for (; slotIdx < _slots.Count; slotIdx++)
            {
                var slot = _slots[slotIdx];
                slot.fill.color = SlotEmpty;
                slot.nameText.text = "";
                slot.countText.text = "";
            }

            if (_goldText != null)
                _goldText.text = "金币: " + pd.gold + " r";
        }


        private void RefreshSpritesView()
        {
            var gm = GameManager.Instance;
            if (gm == null) return;

            foreach (var row in _spriteRows)
                if (row != null) Destroy(row);
            _spriteRows.Clear();

            var bag = gm.playerData.spriteBag;
            const int maxRows = 6;
            int shown = Mathf.Min(maxRows, bag.Count);
            float[] ys = { 140f, 90f, 40f, -10f, -60f, -110f };

            for (int i = 0; i < shown; i++)
            {
                var sprite = bag[i];
                float y = ys[i];
                int beltSlot = gm.GetBeltSlotOf(sprite);

                string ballName = sprite.caughtWith switch
                {
                    PokeBallQuality.Chip => "芯片",
                    PokeBallQuality.Great => "高级",
                    PokeBallQuality.Ultra => "超级",
                    _ => "普通"
                };
                string posText = beltSlot >= 0 ? "腰" + (beltSlot + 1) : "包中";
                string hpText = sprite.IsAlive
                    ? sprite.currentHP.ToString("F0") + "/" + sprite.maxHP.ToString("F0")
                    : "阵亡";

                // 名称截断,防止超出面板
                string name = Truncate(sprite.spriteName, 4);
                string info = name + " Lv." + sprite.level + " " + ballName + " " + posText + " " + hpText;

                var row = new GameObject("SpriteRow_" + i);
                row.transform.SetParent(_spritesRoot.transform, false);

                CreateText(row.transform, info,
                    sprite.IsAlive ? TextDark : TextMuted,
                    16, new Vector3(-235f, y, 0f));

                if (!sprite.IsAlive)
                {
                    // 阵亡：不可装到腰间（按钮灰显）；仅可用复活药水复活
                    CreateButton(row.transform, "装到腰间", new Vector3(250f, y, 0f), new Vector2(150f, 45f), BtnGreen, () => { }, 18, false);
                    CreateButton(row.transform, "复活", new Vector3(390f, y, 0f), new Vector2(100f, 45f), ReviveFill, () => ReviveSprite(sprite), 18, gm.HasItem("Revive Bottle"));
                }
                else if (beltSlot >= 0)
                    CreateButton(row.transform, "放回背包", new Vector3(250f, y, 0f), new Vector2(150f, 45f), BtnOrange, () => UnequipFromBelt(sprite), 18);
                else
                    CreateButton(row.transform, "装到腰间", new Vector3(250f, y, 0f), new Vector2(150f, 45f), BtnGreen, () => EquipToBelt(sprite), 18);

                _spriteRows.Add(row);
            }

            if (bag.Count > maxRows)
                CreateText(_spritesRoot.transform, "…共 " + bag.Count + " 只，仅显示前 " + maxRows + " 只", TextMuted, 15, new Vector3(0f, -165f, 0f));
        }

        // ===== 腰带操作 =====
        private void EquipToBelt(SpriteInstance sprite)
        {
            var gm = GameManager.Instance;
            if (gm == null) return;
            if (gm.TryEquipSpriteToBelt(sprite))
                ShowStatus(sprite.spriteName + " 已装到腰间");
            else
                ShowStatus("腰带已满，无法装载！");
            RefreshBeltAndView();
        }

        private void UnequipFromBelt(SpriteInstance sprite)
        {
            var gm = GameManager.Instance;
            if (gm == null) return;
            gm.UnequipSpriteFromBelt(sprite);
            ShowStatus(sprite.spriteName + " 已放回背包");
            RefreshBeltAndView();
        }

        /// <summary>复活阵亡精灵：消耗一瓶复活药水（仅死亡标志为真的精灵可用）</summary>
        private void ReviveSprite(SpriteInstance sprite)
        {
            var gm = GameManager.Instance;
            if (gm == null || sprite == null || sprite.IsAlive) return;
            if (!gm.HasItem("Revive Bottle"))
            {
                ShowStatus("没有复活药水！");
                return;
            }
            gm.UseItem("Revive Bottle");
            sprite.Revive();
            ShowStatus(sprite.spriteName + " 已复活！");
            RefreshBeltAndView();
        }

        private void RefreshBeltAndView()
        {
            var belt = FindFirstObjectByType<PokeBallBelt>();
            if (belt != null) belt.RefreshBalls();
            if (_view == View.Sprites) RefreshSpritesView();
        }

        private void ShowStatus(string msg)
        {
            if (_statusText == null) return;
            _statusText.text = msg;
            _statusTimer = 2f;
        }

        // ===== 定位:低头平板式跟随头显（位置 SmoothDamp + 旋转 Slerp 阻尼，避免抖动） =====
        private Vector3 _followVelocity;

        private void FollowCamera(GameObject go)
        {
            var cam = Camera.main;
            if (_panelCanvas != null && _panelCanvas.worldCamera == null)
                _panelCanvas.worldCamera = cam;
            if (cam == null) return;
            Vector3 fwd = cam.transform.forward;
            fwd.y = 0;
            fwd.Normalize();
            Vector3 targetPos = cam.transform.position + fwd * panelDistance + Vector3.up * panelHeight;
            go.transform.position = Vector3.SmoothDamp(go.transform.position, targetPos, ref _followVelocity, 0.15f);
            Quaternion targetRot = Quaternion.LookRotation(cam.transform.position - targetPos)
                                 * Quaternion.Euler(0f, 180f, 0f)
                                 * Quaternion.Euler(panelTilt, 0f, 0f);
            go.transform.rotation = Quaternion.Slerp(go.transform.rotation, targetRot, 1f - Mathf.Exp(-10f * Time.deltaTime));
        }

        // ===== UI 构建工具(Canvas 元素,按层级渲染) =====
        private GameObject CreateButton(Transform parent, string label, Vector3 localPos, Vector2 size, Color color, System.Action onClick, float fontSize, bool interactable = true)
        {
            var go = new GameObject("Btn_" + label);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;

            // 深棕描边(外圈)
            var borderGo = new GameObject("Border");
            borderGo.transform.SetParent(go.transform, false);
            var borderImg = borderGo.AddComponent<Image>();
            borderImg.color = FrameColor;
            borderImg.rectTransform.sizeDelta = size + Vector2.one * 8f;

            // 亮色填充(内层)
            var fillGo = new GameObject("Fill");
            fillGo.transform.SetParent(go.transform, false);
            var fillImg = fillGo.AddComponent<Image>();
            fillImg.color = color;
            fillImg.rectTransform.sizeDelta = size;

            // 文字(Canvas 元素,创建顺序在后 → 显示在按钮之上)
            CreateText(go.transform, label, TextDark, fontSize, Vector3.zero);

            var box = go.AddComponent<BoxCollider>();
            box.size = new Vector3((size.x + 8f) * 0.001f, (size.y + 8f) * 0.001f, 0.06f);

            var inter = go.AddComponent<XRSimpleInteractable>();
            inter.selectEntered.AddListener(_ =>
            {
                // UI 点击音效
                if (AudioManager.Instance != null) AudioManager.Instance.PlaySfx("Audio/SFX/sfx_ui_click");
                onClick();
            });
            inter.enabled = interactable;
            if (!interactable)
            {
                // 灰显：填充变灰 + 禁用射线交互（按钮不可点击）
                fillImg.color = Color.Lerp(color, Color.gray, 0.6f);
            }
            return go;
        }

        private Text CreateText(Transform parent, string content, Color color, float fontSize, Vector3 localPos)
        {
            var go = new GameObject("Text");
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            var rt = go.AddComponent<RectTransform>();
            rt.sizeDelta = new Vector2(900f, 300f);   // 足够大,Overflow 居中显示
            var text = go.AddComponent<Text>();
            text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            text.text = content;
            text.fontSize = (int)fontSize;
            text.color = color;
            text.alignment = TextAnchor.MiddleCenter;
            text.horizontalOverflow = HorizontalWrapMode.Overflow;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            return text;
        }

        private string Truncate(string s, int max)
        {
            if (string.IsNullOrEmpty(s)) return s;
            return s.Length <= max ? s : s.Substring(0, max) + "…";
        }
    }
}
