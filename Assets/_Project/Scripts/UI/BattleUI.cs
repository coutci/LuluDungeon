using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

namespace LuluDungeon
{
    /// <summary>
    /// 战斗UI面板 - World Space Canvas
    /// 同步回合制：玩家锁定行动（技能/精灵球/药水/切换/逃跑）
    /// </summary>
    public class BattleUI : MonoBehaviour
    {
        [Header("技能按钮")]
        public Button skill1Button;
        public Button skill2Button;
        public Button skill3Button;
        public UnityEngine.UI.Text skill1Text;
        public UnityEngine.UI.Text skill2Text;
        public UnityEngine.UI.Text skill3Text;

        [Header("功能按钮")]
        public Button switchButton;
        public Button bagButton;
        public Button escapeButton;

        [Header("背包-精灵球")]
        public GameObject bagPanel;
        public Button[] pokeBallButtons;          // 4 球：Chip/Normal/Great/Ultra

        [Header("背包-药水")]
        public Button healButton;                 // 治疗药水
        public Button skillBottleButton;          // 技能药水

        [Header("切换精灵面板")]
        public GameObject switchPanel;
        public Button[] switchSlotButtons;

        [Header("信息显示")]
        public UnityEngine.UI.Text battleMessageText;
        public UnityEngine.UI.Text playerSpriteName;
        public UnityEngine.UI.Text enemySpriteName;
        public Slider playerHPBar;
        public Slider enemyHPBar;
        public UnityEngine.UI.Text playerHPText;
        public UnityEngine.UI.Text enemyHPText;

        [Header("UI容器")]
        [Tooltip("实际控制显隐的 Canvas 物体（避免禁用挂载物）")]
        public GameObject uiCanvas;

        private BattleManager _bm;
        private GameManager _gm;
        private bool _forcedSwitchMode;
        private Coroutine _endBattleCoroutine;   // 战斗结束延迟隐藏句柄(新战斗开始前需取消)
        private UnityEngine.UI.Text _energyText;   // 动态创建的玩家能量显示（跟随 HP 文本下方）

        private void Start()
        {
            _bm = BattleManager.Instance;
            _gm = GameManager.Instance;

            // 绑定按钮事件（XRSimpleInteractable 射线交互，与个人面板一致）
            BindXRButton(skill1Button, () => _bm.LockPlayerSkill(0));
            BindXRButton(skill2Button, () => _bm.LockPlayerSkill(1));
            BindXRButton(skill3Button, () => _bm.LockPlayerSkill(2));
            BindXRButton(switchButton, ToggleSwitchPanel);
            BindXRButton(bagButton, ToggleBagPanel);
            BindXRButton(escapeButton, () => _bm.LockPlayerEscape());
            BindXRButton(healButton, () => { _bm.LockPlayerHealBottle(); ToggleBagPanel(); });
            BindXRButton(skillBottleButton, () => { _bm.LockPlayerSkillBottle(); ToggleBagPanel(); });

            if (_bm != null)
            {
                _bm.OnStateChanged += OnBattleStateChanged;
                _bm.OnBattleMessage += OnMessageReceived;
            }

            EventBus.Subscribe(EventBus.ON_BATTLE_START, Show);
            EventBus.Subscribe(EventBus.ON_BATTLE_END, Hide);

            Hide();
        }

        private void OnDestroy()
        {
            if (_bm != null)
            {
                _bm.OnStateChanged -= OnBattleStateChanged;
                _bm.OnBattleMessage -= OnMessageReceived;
            }
            EventBus.Unsubscribe(EventBus.ON_BATTLE_START, Show);
            EventBus.Unsubscribe(EventBus.ON_BATTLE_END, Hide);
        }

        private void Update()
        {
            if (_bm == null || _gm == null) return;

            // 面板跟随头显（阻尼平滑，防抖动）：前方 0.8m、低于头显 0.45m、前倾 20°
            if (uiCanvas != null && uiCanvas.activeSelf)
            {
                var cam = Camera.main;
                if (cam != null)
                {
                    Vector3 fwd = cam.transform.forward;
                    fwd.y = 0f;
                    if (fwd.sqrMagnitude < 0.001f) fwd = cam.transform.forward;
                    fwd.Normalize();

                    Vector3 targetPos = cam.transform.position + fwd * 0.8f + Vector3.down * 0.45f;
                    float smooth = Time.deltaTime * 8f;
                    uiCanvas.transform.position = Vector3.Lerp(uiCanvas.transform.position, targetPos, smooth);

                    // 目标旋转 = 面向相机(绕Y) + 绕自身Y 180° + 前倾 20°
                    Quaternion targetRot = Quaternion.LookRotation(fwd)
                        * Quaternion.Euler(20f, 0f, 0f);
                    uiCanvas.transform.rotation = Quaternion.Slerp(uiCanvas.transform.rotation, targetRot, smooth);
                }
            }

            if (_bm.CurrentState == BattleState.Intro) return;

            // 更新HP条
            var ps = _bm.PlayerSprite;
            var es = _bm.EnemySprite;

            // 常驻刷新双方名字（切换精灵后同步显示）
            if (playerSpriteName != null && ps != null)
                playerSpriteName.text = ps.spriteName;
            if (enemySpriteName != null && es != null)
                enemySpriteName.text = es.spriteName;

            if (playerHPBar != null && ps != null)
            {
                playerHPBar.maxValue = ps.maxHP;
                playerHPBar.value = Mathf.Lerp(playerHPBar.value, ps.currentHP, Time.deltaTime * 5f);
            }
            if (enemyHPBar != null && es != null)
            {
                enemyHPBar.maxValue = es.maxHP;
                enemyHPBar.value = Mathf.Lerp(enemyHPBar.value, es.currentHP, Time.deltaTime * 5f);
            }
            if (playerHPText != null && ps != null)
                playerHPText.text = $"{ps.currentHP:F0}/{ps.maxHP:F0}";
            if (enemyHPText != null && es != null)
                enemyHPText.text = $"{es.currentHP:F0}/{es.maxHP:F0}";
            EnsureEnergyText();
            if (_energyText != null && ps != null)
                _energyText.text = $"⚡ {ps.energy}/{SpriteInstance.MaxEnergy}";

            // 常驻刷新技能显示（面板打开时跳过，避免每帧重新启用被禁用的主按钮）
            bool panelOpen = (switchPanel != null && switchPanel.activeSelf) || (bagPanel != null && bagPanel.activeSelf);
            if (ps != null && _bm.CurrentState == BattleState.PlayerTurn && !panelOpen)
            {
                RefreshSkillButtons();
            }
        }

        private void OnBattleStateChanged(BattleState state)
        {
            bool isPlayerTurn = state == BattleState.PlayerTurn;
            SetButtonsInteractable(isPlayerTurn);

            if (state == BattleState.ForcedSwitch)
            {
                _forcedSwitchMode = true;
                SetButtonsInteractable(false);
                if (switchPanel) switchPanel.SetActive(true);
                RefreshSwitchSlots();
            }
            else if (state == BattleState.PlayerTurn)
            {
                if (switchPanel) switchPanel.SetActive(false);
                if (bagPanel) bagPanel.SetActive(false);
            }

            if (state == BattleState.Victory || state == BattleState.Defeat || state == BattleState.Escaped)
            {
                _endBattleCoroutine = StartCoroutine(EndBattleDelay());
            }
        }

        private IEnumerator EndBattleDelay()
        {
            yield return new WaitForSeconds(2.5f);
            Hide();
        }

        private void OnMessageReceived(string msg)
        {
            if (battleMessageText != null)
                battleMessageText.text = msg;
        }

        private void SetButtonsInteractable(bool interactable)
        {
            SetButtonInteractable(skill1Button, interactable);
            SetButtonInteractable(skill2Button, interactable);
            SetButtonInteractable(skill3Button, interactable);
            SetButtonInteractable(switchButton, interactable);
            SetButtonInteractable(bagButton, interactable);
            SetButtonInteractable(escapeButton, interactable && !_bm.IsBossBattle);
            SetButtonInteractable(healButton, interactable && _gm.HasItem("Heal Bottle"));
            SetButtonInteractable(skillBottleButton, interactable && _gm.HasItem("Energy Bottle"));

            if (interactable)
            {
                RefreshSkillButtons();
            }
        }

        /// <summary>主操作区按钮整体启停（面板打开时禁用，防止面板按钮与主按钮重叠误触）</summary>
        private void SetMainButtonsInteractable(bool on)
        {
            SetButtonInteractable(skill1Button, on);
            SetButtonInteractable(skill2Button, on);
            SetButtonInteractable(skill3Button, on);
            SetButtonInteractable(switchButton, on);
            SetButtonInteractable(bagButton, on);
            SetButtonInteractable(escapeButton, on && _bm != null && !_bm.IsBossBattle);
            SetButtonInteractable(healButton, on && _gm != null && _gm.HasItem("Heal Bottle"));
            SetButtonInteractable(skillBottleButton, on && _gm != null && _gm.HasItem("Energy Bottle"));
        }

        /// <summary>
        /// 为按钮添加 BoxCollider + XRSimpleInteractable（射线直接交互，参照个人面板）
        /// </summary>
        private static void BindXRButton(Button btn, UnityEngine.Events.UnityAction callback)
        {
            if (btn == null) return;
            var go = btn.gameObject;
            var box = go.GetComponent<BoxCollider>();
            if (box == null)
            {
                box = go.AddComponent<BoxCollider>();
                var rt = go.GetComponent<RectTransform>();
                // 世界尺寸 = 本地像素 × lossyScale（兼容任意 Canvas 缩放，修正旧 ×0.001 假设）
                float w = rt != null ? rt.rect.width * rt.lossyScale.x : 0.2f;
                float h = rt != null ? rt.rect.height * rt.lossyScale.y : 0.08f;
                box.size = new Vector3(w, h, 0.02f);
            }
            var inter = go.GetComponent<XRSimpleInteractable>();
            if (inter == null) inter = go.AddComponent<XRSimpleInteractable>();
            inter.selectEntered.RemoveAllListeners();
            inter.selectEntered.RemoveAllListeners();
            inter.selectEntered.AddListener(_ =>
            {
                // UI 点击音效
                if (AudioManager.Instance != null) AudioManager.Instance.PlaySfx("Audio/SFX/sfx_ui_click");
                callback();
            });
        }

        /// <summary>同时控制 uGUI 灰显与 XRSimpleInteractable 可用性</summary>
        private static void SetButtonInteractable(Button btn, bool on)
        {
            if (btn == null) return;
            btn.interactable = on;
            var inter = btn.GetComponent<XRSimpleInteractable>();
            if (inter != null) inter.enabled = on;
        }

        /// <summary>获取/创建按钮的 XRSimpleInteractable（动态槽位用）</summary>
        private static XRSimpleInteractable GetOrAddInteractable(GameObject go)
        {
            var box = go.GetComponent<BoxCollider>();
            if (box == null)
            {
                box = go.AddComponent<BoxCollider>();
                var rt = go.GetComponent<RectTransform>();
                // 世界尺寸 = 本地像素 × lossyScale（兼容任意 Canvas 缩放）
                float w = rt != null ? rt.rect.width * rt.lossyScale.x : 0.2f;
                float h = rt != null ? rt.rect.height * rt.lossyScale.y : 0.08f;
                box.size = new Vector3(w, h, 0.02f);
            }
            var inter = go.GetComponent<XRSimpleInteractable>();
            if (inter == null) inter = go.AddComponent<XRSimpleInteractable>();
            return inter;
        }

        /// <summary>动态创建玩家能量文本（挂在 HP 文本下方，复用现有字体）</summary>
        private void EnsureEnergyText()
        {
            if (_energyText != null || uiCanvas == null || playerHPText == null) return;
            var go = new GameObject("PlayerEnergyText");
            go.transform.SetParent(playerHPText.transform.parent, false);
            var rt = go.AddComponent<RectTransform>();
            rt.anchoredPosition = playerHPText.rectTransform.anchoredPosition + new Vector2(0f, -30f);
            rt.sizeDelta = new Vector2(400f, 26f);
            _energyText = go.AddComponent<UnityEngine.UI.Text>();
            _energyText.font = playerHPText.font;
            _energyText.fontSize = 20;
            _energyText.alignment = TextAnchor.MiddleCenter;
            _energyText.color = new Color(0.30f, 0.65f, 1f);   // 亮蓝（与面板底色区分）
            _energyText.raycastTarget = false;
        }

        /// <summary>技能按钮：显示技能名 + 能量消耗；能量不足时攻击类显示基础攻击兜底、非攻击类灰显</summary>
        private void RefreshSkillButtons()
        {
            if (_bm == null || _bm.PlayerSprite == null) return;

            RefreshSkillButton(skill1Button, skill1Text, 0);
            RefreshSkillButton(skill2Button, skill2Text, 1);
            RefreshSkillButton(skill3Button, skill3Text, 2);
        }

        private void RefreshSkillButton(Button btn, UnityEngine.UI.Text txt, int index)
        {
            if (btn == null || txt == null) return;
            var skill = _bm.GetEquippedSkill(index);
            var ps = _bm.PlayerSprite;

            // 空槽或攻击类能量不足 → 基础攻击兜底（不耗能量，点击自动降级）
            if (skill == null || (ps != null && !ps.CanUseSkill(skill) && skill.IsAttackClass))
            {
                txt.text = "基础攻击";
                SetButtonInteractable(btn, true);
                return;
            }

            bool usable = ps != null && ps.CanUseSkill(skill);
            int cost = SpriteInstance.EnergyCostOf(skill);
            txt.text = $"{skill.skillName} ⚡{cost}";
            SetButtonInteractable(btn, usable);
        }
        private void ToggleSwitchPanel()
        {
            if (switchPanel == null || _gm == null) return;
            // 强制切换（阵亡）时禁止关闭面板，防止槽位回调失效
            if (_forcedSwitchMode)
            {
                switchPanel.SetActive(true);
                RefreshSwitchSlots();
                return;
            }
            _forcedSwitchMode = false;
            bool active = !switchPanel.activeSelf;
            switchPanel.SetActive(active);
            if (bagPanel && active) bagPanel.SetActive(false);

            if (active)
            {
                // 打开切换面板：禁用主操作按钮，防止与面板按钮重叠误触
                SetMainButtonsInteractable(false);
                RefreshSwitchSlots();
            }
            else
            {
                // 关闭：按战斗状态恢复主按钮
                SetButtonsInteractable(_bm != null && _bm.CurrentState == BattleState.PlayerTurn);
            }
        }

        private void ToggleBagPanel()
        {
            if (bagPanel == null) return;
            bool active = !bagPanel.activeSelf;
            bagPanel.SetActive(active);
            if (switchPanel && active) switchPanel.SetActive(false);

            if (active)
            {
                // 打开背包面板：禁用主操作按钮，防止重叠误触
                SetMainButtonsInteractable(false);
                RefreshBagSlots();
            }
            else
            {
                // 关闭：按战斗状态恢复主按钮
                SetButtonsInteractable(_bm != null && _bm.CurrentState == BattleState.PlayerTurn);
            }
        }

        private void RefreshSwitchSlots()
        {
            if (switchSlotButtons == null || _gm == null) return;
            var slots = _gm.playerData.pokeBallSlots;
            for (int i = 0; i < switchSlotButtons.Length; i++)
            {
                var btn = switchSlotButtons[i];
                if (btn == null) continue;

                // 只显示腰间（战斗队伍）装载的精灵
                if (i < slots.Count && slots[i] != null && slots[i].isLoaded && slots[i].sprite != null)
                {
                    var sprite = slots[i].sprite;
                    btn.gameObject.SetActive(true);
                    var label = btn.GetComponentInChildren<UnityEngine.UI.Text>();
                    if (label) label.text = $"{sprite.spriteName} Lv.{sprite.level} HP:{sprite.currentHP:F0}";
                    SetButtonInteractable(btn, sprite.IsAlive);
                    int index = i;
                    var inter = GetOrAddInteractable(btn.gameObject);
                    inter.selectEntered.RemoveAllListeners();
                    if (_forcedSwitchMode)
                    {
                        inter.selectEntered.AddListener(_ => { if (AudioManager.Instance != null) AudioManager.Instance.PlaySfx("Audio/SFX/sfx_ui_click"); _bm.ForceSwitch(index); });
                    }
                    else
                    {
                        inter.selectEntered.AddListener(_ => { if (AudioManager.Instance != null) AudioManager.Instance.PlaySfx("Audio/SFX/sfx_ui_click"); _bm.LockPlayerSwitch(index); ToggleSwitchPanel(); });
                    }
                }
                else
                {
                    btn.gameObject.SetActive(false);
                }
            }
        }

        private void RefreshBagSlots()
        {
            if (_gm == null) return;

            // 精灵球 ×4
            if (pokeBallButtons != null)
            {
                int[] counts = { _gm.playerData.chipPokeBalls, _gm.playerData.normalPokeBalls,
                                 _gm.playerData.greatPokeBalls, _gm.playerData.ultraPokeBalls };
                PokeBallQuality[] qualities = { PokeBallQuality.Chip, PokeBallQuality.Normal,
                                                PokeBallQuality.Great, PokeBallQuality.Ultra };

                for (int i = 0; i < pokeBallButtons.Length && i < 4; i++)
                {
                    var btn = pokeBallButtons[i];
                    if (btn == null) continue;

                    btn.gameObject.SetActive(true);
                    SetButtonInteractable(btn, counts[i] > 0 && _bm.CanCatch);
                    var label = btn.GetComponentInChildren<UnityEngine.UI.Text>();
                    var qualityName = qualities[i].ToString();
                    if (label) label.text = $"{qualityName} PokeBall x{counts[i]}";

                    int qIndex = i;
                    var inter = GetOrAddInteractable(btn.gameObject);
                    inter.selectEntered.RemoveAllListeners();
                    inter.selectEntered.AddListener(_ => { if (AudioManager.Instance != null) AudioManager.Instance.PlaySfx("Audio/SFX/sfx_ui_click"); _bm.LockPlayerBall(qualities[qIndex]); ToggleBagPanel(); });
                }
            }

            // 药水
            if (healButton != null)
            {
                var label = healButton.GetComponentInChildren<UnityEngine.UI.Text>();
                if (label) label.text = $"Heal Bottle x{_gm.playerData.healBottles}";
                SetButtonInteractable(healButton, _gm.playerData.healBottles > 0);
            }
            if (skillBottleButton != null)
            {
                var label = skillBottleButton.GetComponentInChildren<UnityEngine.UI.Text>();
                if (label) label.text = $"Energy Bottle x{_gm.playerData.skillBottles}";
                SetButtonInteractable(skillBottleButton, _gm.playerData.skillBottles > 0);
            }
        }

        public void Show()
        {
            // 取消上一场战斗挂起的延迟隐藏(防止旧协程隐藏新战斗 UI)
            if (_endBattleCoroutine != null) { StopCoroutine(_endBattleCoroutine); _endBattleCoroutine = null; }

            if (uiCanvas != null) uiCanvas.SetActive(true);
            else gameObject.SetActive(true);

            // 清空上一场战斗残留的操作提示
            if (battleMessageText != null) battleMessageText.text = "";
            var ps = _bm.PlayerSprite;
            var es = _bm.EnemySprite;

            if (playerSpriteName && ps != null) playerSpriteName.text = ps.spriteName;
            if (enemySpriteName && es != null) enemySpriteName.text = es.spriteName;

            if (switchPanel) switchPanel.SetActive(false);
            if (bagPanel) bagPanel.SetActive(false);
            _forcedSwitchMode = false;
        }

        public void Hide()
        {
            if (uiCanvas != null) uiCanvas.SetActive(false);
            else gameObject.SetActive(false);
        }
    }
}
