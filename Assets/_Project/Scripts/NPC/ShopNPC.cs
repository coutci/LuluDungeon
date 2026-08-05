using UnityEngine;

namespace LuluDungeon
{
    /// <summary>
    /// 商店 NPC：靠近显示"商店"按钮（单层，扳机触发）→ 商店平板（跟随头显）
    /// 商品/价格参考设计文档 §7.2，右上角关闭按钮
    /// </summary>
    public class ShopNPC : MonoBehaviour
    {
        [Header("交互")]
        public float interactDistance = 2.5f;
        public Vector3 buttonOffset = new Vector3(0.9f, 1.2f, 0f);

        [Header("商店平板")]
        public float panelDistance = 0.7f;
        public float panelHeight = -0.10f;

        [Header("商品（设计文档 §7.2）")]
        public string[] itemNames = new string[]
        {
            "Chip PokeBall", "Normal PokeBall", "Great PokeBall", "Ultra PokeBall", "Heal Bottle", "Revive Bottle", "Energy Bottle"
        };
        public int[] itemPrices = new int[] { 20, 50, 100, 200, 50, 80, 60 };

        private bool _shopOpen;
        private GameObject _interactBtn;
        private GameObject _shopRoot;
        private UnityEngine.UI.Text _goldText;
        private Canvas _shopCanvas;
        private GameObject[] _buyBtns;

        private void Start()
        {
            CreateInteractButton();
            CreateShopPanel();
        }

        private void Update()
        {
            if (_interactBtn != null)
            {
                var cam = Camera.main;
                if (cam != null)
                {
                    float dist = Vector3.Distance(transform.position, cam.transform.position);
                    _interactBtn.SetActive(dist < interactDistance && !_shopOpen);
                }
            }

            if (_shopOpen && _shopRoot != null)
                FollowCamera(_shopRoot);
        }

        // ===== 交互按钮（单层，扳机触发）=====
        private void CreateInteractButton()
        {
            _interactBtn = new GameObject("ShopBtn");
            _interactBtn.transform.SetParent(transform, false);
            _interactBtn.transform.localPosition = buttonOffset;

            var box = _interactBtn.AddComponent<BoxCollider>();
            box.size = new Vector3(0.84f, 0.44f, 0.05f);

            // 卡通画框按钮:深棕描边 + 黄色填充(薄板)
            var frame = GameObject.CreatePrimitive(PrimitiveType.Cube);
            frame.name = "Frame";
            frame.transform.SetParent(_interactBtn.transform, false);
            frame.transform.localScale = new Vector3(0.84f, 0.44f, 0.02f);
            frame.GetComponent<MeshRenderer>().material = CreateFlatMaterial(new Color(0.36f, 0.24f, 0.14f));

            var fill = GameObject.CreatePrimitive(PrimitiveType.Cube);
            fill.name = "Fill";
            fill.transform.SetParent(_interactBtn.transform, false);
            fill.transform.localPosition = new Vector3(0f, 0f, 0.012f);
            fill.transform.localScale = new Vector3(0.80f, 0.40f, 0.012f);
            fill.GetComponent<MeshRenderer>().material = CreateFlatMaterial(new Color(0.99f, 0.82f, 0.38f));

            CreateLabel(_interactBtn.transform, "商店", new Color(0.28f, 0.18f, 0.08f), 0.014f, new Vector3(0, 0, 0.05f));

            var bb = _interactBtn.AddComponent<Billboard>();
            bb.flipDirection = true;
            bb.lockYAxis = true;

            AddTrigger(_interactBtn, OpenShop);
            _interactBtn.SetActive(false);
        }

        // ===== 商店平板 =====
        private void CreateShopPanel()
        {
            _shopRoot = new GameObject("ShopPanel");

            var canvasGo = new GameObject("Canvas");
            canvasGo.transform.SetParent(_shopRoot.transform, false);
            var canvas = canvasGo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            canvas.worldCamera = Camera.main;
            canvasGo.GetComponent<RectTransform>().localScale = new Vector3(0.001f, 0.001f, 0.001f);

            // 卡通画框:深棕描边 + 米色面板(像素坐标)
            var frameGo = new GameObject("Frame");
            frameGo.transform.SetParent(canvasGo.transform, false);
            var frameImg = frameGo.AddComponent<UnityEngine.UI.Image>();
            frameImg.color = new Color(0.36f, 0.24f, 0.14f, 1f);
            frameImg.rectTransform.sizeDelta = new Vector2(830f, 630f);

            var bg = new GameObject("BG");
            bg.transform.SetParent(canvasGo.transform, false);
            var img = bg.AddComponent<UnityEngine.UI.Image>();
            img.color = new Color(0.97f, 0.90f, 0.77f, 1f);
            var bgRt = img.rectTransform;
            bgRt.sizeDelta = new Vector2(800f, 600f);

            _shopCanvas = canvas;
            CreateUIText(canvasGo.transform, "商店", new Color(0.75f, 0.40f, 0.15f), 28, new Vector3(0f, 260f, 0f));
            _goldText = CreateUIText(canvasGo.transform, "", new Color(0.28f, 0.18f, 0.08f), 16, new Vector3(0f, 190f, 0f));

            _buyBtns = new GameObject[itemNames.Length];
            for (int i = 0; i < itemNames.Length; i++)
            {
                int idx = i;
                float y = (0.125f - i * 0.065f) * 1000f;
                _buyBtns[i] = CreateBuyButton(idx, y);
            }

            CreateCloseButton();
            _shopRoot.SetActive(false);
        }

        private GameObject CreateBuyButton(int index, float y)
        {
            var btn = new GameObject("BuyBtn_" + index);
            btn.transform.SetParent(_shopCanvas.transform, false);
            btn.transform.localPosition = new Vector3(0f, y, 0f);

            // 描边(深棕)
            var borderGo = new GameObject("Border");
            borderGo.transform.SetParent(btn.transform, false);
            var borderImg = borderGo.AddComponent<UnityEngine.UI.Image>();
            borderImg.color = new Color(0.36f, 0.24f, 0.14f, 1f);
            borderImg.rectTransform.sizeDelta = new Vector2(740f, 65f);

            // 填充(米黄)
            var fillGo = new GameObject("Fill");
            fillGo.transform.SetParent(btn.transform, false);
            var fillImg = fillGo.AddComponent<UnityEngine.UI.Image>();
            fillImg.color = new Color(0.85f, 0.78f, 0.55f, 1f);
            fillImg.rectTransform.sizeDelta = new Vector2(700f, 60f);

            CreateUIText(btn.transform, itemNames[index] + "  " + itemPrices[index] + "r",
                new Color(0.28f, 0.18f, 0.08f), 20, Vector3.zero);

            var box = btn.AddComponent<BoxCollider>();
            box.size = new Vector3(0.74f, 0.065f, 0.02f);

            AddTrigger(btn, () => Buy(index));
            return btn;
        }

        private void CreateCloseButton()
        {
            var closeBtn = new GameObject("CloseBtn");
            closeBtn.transform.SetParent(_shopCanvas.transform, false);
            closeBtn.transform.localPosition = new Vector3(320f, 260f, 0f);

            var borderGo = new GameObject("Border");
            borderGo.transform.SetParent(closeBtn.transform, false);
            var borderImg = borderGo.AddComponent<UnityEngine.UI.Image>();
            borderImg.color = new Color(0.36f, 0.24f, 0.14f, 1f);
            borderImg.rectTransform.sizeDelta = new Vector2(110f, 110f);

            var fillGo = new GameObject("Fill");
            fillGo.transform.SetParent(closeBtn.transform, false);
            var fillImg = fillGo.AddComponent<UnityEngine.UI.Image>();
            fillImg.color = new Color(0.90f, 0.42f, 0.35f, 1f);
            fillImg.rectTransform.sizeDelta = new Vector2(100f, 100f);

            CreateUIText(closeBtn.transform, "X", new Color(0.28f, 0.18f, 0.08f), 28, Vector3.zero);

            var box = closeBtn.AddComponent<BoxCollider>();
            box.size = new Vector3(0.11f, 0.11f, 0.02f);

            AddTrigger(closeBtn, CloseShop);
        }

        // ===== 商店逻辑 =====
        private void OpenShop()
        {
            if (_shopOpen) return;
            _shopOpen = true;
            _interactBtn.SetActive(false);
            _shopRoot.SetActive(true);
            RefreshGold();
        }

        private void CloseShop()
        {
            _shopOpen = false;
            _shopRoot.SetActive(false);
        }

        private void Buy(int index)
        {
            if (index < 0 || index >= itemNames.Length) return;
            var gm = GameManager.Instance;
            if (gm == null) return;

            int price = itemPrices[index];
            if (gm.playerData.gold < price)
            {
                Debug.Log("[Shop] 金币不足！");
                return;
            }
            gm.ChangeGold(-price);
            gm.AddItem(itemNames[index], 1);
            RefreshGold();
            Debug.Log("[Shop] 购买 " + itemNames[index] + " -" + price + "r");
        }

        private void RefreshGold()
        {
            var gm = GameManager.Instance;
            if (_goldText != null && gm != null)
                _goldText.text = "金币：" + gm.playerData.gold + "r";
        }

        // ===== 定位（位置 SmoothDamp + 旋转 Slerp 阻尼，避免抖动） =====
        private Vector3 _followVelocity;

        private void FollowCamera(GameObject go)
        {
            var cam = Camera.main;
            if (_shopCanvas != null && _shopCanvas.worldCamera == null)
                _shopCanvas.worldCamera = cam;
            if (cam == null) return;
            Vector3 fwd = cam.transform.forward;
            fwd.y = 0;
            fwd.Normalize();
            Vector3 targetPos = cam.transform.position + fwd * panelDistance + Vector3.up * panelHeight;
            go.transform.position = Vector3.SmoothDamp(go.transform.position, targetPos, ref _followVelocity, 0.15f);
            Quaternion targetRot = Quaternion.LookRotation(cam.transform.position - targetPos)
                                 * Quaternion.Euler(0f, 180f, 0f)
                                 * Quaternion.Euler(8f, 0f, 0f);
            go.transform.rotation = Quaternion.Slerp(go.transform.rotation, targetRot, 1f - Mathf.Exp(-10f * Time.deltaTime));
        }

        // ===== 工具 =====
        private void AddTrigger(GameObject go, UnityEngine.Events.UnityAction callback)
        {
            var inter = go.AddComponent<UnityEngine.XR.Interaction.Toolkit.Interactables.XRSimpleInteractable>();
            go.AddComponent<HoverHighlight>();
            inter.selectEntered.AddListener(_ => callback());
        }

        private UnityEngine.UI.Text CreateUIText(Transform parent, string content, Color color, float fontSize, Vector3 localPos)
        {
            var go = new GameObject("UIText");
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            var rt = go.AddComponent<RectTransform>();
            rt.sizeDelta = new Vector2(900f, 300f);
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

        private TextMesh CreateLabel(Transform parent, string text, Color color, float charSize, Vector3 localPos)
        {
            var go = new GameObject("Text");
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            var tm = go.AddComponent<TextMesh>();
            tm.text = text;
            tm.fontSize = 100;
            tm.characterSize = charSize;
            tm.color = color;
            tm.anchor = TextAnchor.MiddleCenter;
            tm.alignment = TextAlignment.Center;
            return tm;
        }

        private Material CreateGlowMaterial(Color color)
        {
            var mat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            mat.color = color;
            mat.SetFloat("_Surface", 0f);
            mat.EnableKeyword("_EMISSION");
            mat.SetColor("_EmissionColor", color * 0.6f);
            return mat;
        }

        private Material CreateFlatMaterial(Color color)
        {
            var shader = Shader.Find("Universal Render Pipeline/Lit");
            var mat = shader != null ? new Material(shader) : new Material(Shader.Find("Standard"));
            mat.color = color;
            return mat;
        }
    }
}
