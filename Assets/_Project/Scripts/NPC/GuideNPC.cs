using UnityEngine;

namespace LuluDungeon
{
    /// <summary>
    /// 向导 NPC：身旁"对话"按钮（单层，扳机触发）→ 对话平板（跟随头显，点击任意处继续）
    /// → 赠送装精灵的球（一次性）
    /// </summary>
    public class GuideNPC : MonoBehaviour
    {
        [Header("交互")]
        public float interactDistance = 2.5f;
        public Vector3 buttonOffset = new Vector3(0.9f, 1.2f, 0f);

        [Header("对话面板（面前平板）")]
        public float panelDistance = 0.7f;
        public float panelHeight = -0.10f;

        [Header("赠送")]
        public SpriteData giftSpriteData;
        public int giftLevel = 5;

        [Header("对话内容")]
        [TextArea(2, 3)]
        public string[] dialogue = new string[]
        {
            "欢迎来到地牢！我是这里的向导。",
            "你要从这里一路向下，穿越10层，捕捉精灵、击败强大的Boss。",
            "你还没有精灵伙伴吧？我送你一只——这是鳐鱼！",
            "它已经装在这个精灵球里了。带上它，出发吧！"
        };

        private bool _hasTalked;
        private bool _inDialogue;
        private int _dialogueIndex;

        private GameObject _interactBtn;
        private GameObject _canvasGo;
        private UnityEngine.UI.Text _dialogueText;
        private Canvas _dialogueCanvas;

        private void Start()
        {
            // 读档恢复：向导已赠送过则不再显示对话按钮
            var gm = GameManager.Instance;
            if (gm != null && gm.playerData.guideGifted) _hasTalked = true;
            CreateInteractButton();
            CreateDialoguePanel();
        }

        private void Update()
        {
            if (_hasTalked)
            {
                if (_interactBtn != null) _interactBtn.SetActive(false);
                return;
            }

            var cam = Camera.main;
            if (cam == null) return;

            float dist = Vector3.Distance(transform.position, cam.transform.position);
            if (_interactBtn != null)
                _interactBtn.SetActive(dist < interactDistance && !_inDialogue);

            if (_inDialogue && _canvasGo != null && _canvasGo.activeSelf)
                FollowCamera(_canvasGo);
        }

        // ===== 交互按钮（单层，扳机触发）=====
        private void CreateInteractButton()
        {
            _interactBtn = new GameObject("InteractBtn");
            _interactBtn.transform.SetParent(transform, false);
            _interactBtn.transform.localPosition = buttonOffset;

            var box = _interactBtn.AddComponent<BoxCollider>();
            box.size = new Vector3(0.84f, 0.44f, 0.05f);

            // 卡通画框按钮:深棕描边 + 米黄填充(薄板,非凸起方块)
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

            CreateLabel(_interactBtn.transform, "对话", new Color(0.28f, 0.18f, 0.08f), 0.014f, new Vector3(0, 0, 0.05f));

            var bb = _interactBtn.AddComponent<Billboard>();
            bb.flipDirection = true;
            bb.lockYAxis = true;

            AddTrigger(_interactBtn, StartDialogue);
            _interactBtn.SetActive(false);
        }

        // ===== 对话平板（点击任意处继续）=====
        private void CreateDialoguePanel()
        {
            _canvasGo = new GameObject("GuideDialogue");

            var canvasGo = new GameObject("Canvas");
            canvasGo.transform.SetParent(_canvasGo.transform, false);
            var canvas = canvasGo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            canvas.worldCamera = Camera.main;
            canvasGo.GetComponent<RectTransform>().localScale = new Vector3(0.001f, 0.001f, 0.001f);

            // 卡通画框:深棕描边 + 米色面板(像素坐标)
            var frameGo = new GameObject("Frame");
            frameGo.transform.SetParent(canvasGo.transform, false);
            var frameImg = frameGo.AddComponent<UnityEngine.UI.Image>();
            frameImg.color = new Color(0.36f, 0.24f, 0.14f, 1f);
            frameImg.rectTransform.sizeDelta = new Vector2(630f, 430f);

            var bg = new GameObject("BG");
            bg.transform.SetParent(canvasGo.transform, false);
            var img = bg.AddComponent<UnityEngine.UI.Image>();
            img.color = new Color(0.97f, 0.90f, 0.77f, 1f);
            var bgRt = img.rectTransform;
            bgRt.sizeDelta = new Vector2(600f, 400f);

            _dialogueCanvas = canvas;
            _dialogueText = CreateUIText(canvasGo.transform, "", new Color(0.28f, 0.18f, 0.08f), 26, new Vector3(0f, 0f, 0f));

            // 整个平板可点击（扳机任意位置 → 继续）
            var box = _canvasGo.AddComponent<BoxCollider>();
            box.size = new Vector3(0.6f, 0.4f, 0.1f);
            AddTrigger(_canvasGo, OnContinue);

            _canvasGo.SetActive(false);
        }

        // ===== 对话流程 =====
        private void StartDialogue()
        {
            if (_hasTalked || _inDialogue) return;
            _inDialogue = true;
            _dialogueIndex = 0;
            _interactBtn.SetActive(false);
            ShowLine();
        }

        private void ShowLine()
        {
            if (_canvasGo == null) return;
            FollowCamera(_canvasGo);
            _canvasGo.SetActive(true);
            _dialogueText.text = WrapText(dialogue[_dialogueIndex]);
        }

        private void OnContinue()
        {
            _dialogueIndex++;
            if (_dialogueIndex >= dialogue.Length)
                EndDialogue();
            else
                ShowLine();
        }

        private void EndDialogue()
        {
            _inDialogue = false;
            _canvasGo.SetActive(false);
            _hasTalked = true;
            GiveGift();
        }

        // ===== 赠送 =====
        private void GiveGift()
        {
            var gm = GameManager.Instance;
            if (gm == null || giftSpriteData == null) return;

            var sprite = new SpriteInstance(giftSpriteData, giftLevel);
            // 加入精灵背包并自动装载到腰间第一个空槽（腰带固定 6 槽，不追加槽位）
            gm.AddSpriteToBag(sprite);
            gm.playerData.guideGifted = true;   // 持久化赠送标记

            var belt = FindFirstObjectByType<PokeBallBelt>();
            if (belt != null) belt.RefreshBalls();

            Debug.Log("[GuideNPC] Gifted StingRay Lv." + giftLevel + " in a loaded PokeBall.");
        }

        // ===== 定位（位置 SmoothDamp + 旋转 Slerp 阻尼，避免抖动） =====
        private Vector3 _followVelocity;

        private void FollowCamera(GameObject go)
        {
            var cam = Camera.main;
            if (_dialogueCanvas != null && _dialogueCanvas.worldCamera == null)
                _dialogueCanvas.worldCamera = cam;
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

        private string WrapText(string text, int charsPerLine = 8)
        {
            if (text.Length <= charsPerLine) return text;
            string result = "";
            for (int i = 0; i < text.Length; i += charsPerLine)
            {
                int len = Mathf.Min(charsPerLine, text.Length - i);
                result += text.Substring(i, len);
                if (i + charsPerLine < text.Length) result += "\n";
            }
            return result;
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
