using UnityEngine;
using UnityEngine.UI;
using TMPro;

namespace LuluDungeon
{
    /// <summary>
    /// 精灵列表UI面板
    /// </summary>
    public class SpriteListUI : MonoBehaviour
    {
        [Header("精灵槽位")]
        public Button[] spriteSlotButtons;
        public TextMeshProUGUI[] slotNameTexts;
        public TextMeshProUGUI[] slotLevelTexts;

        [Header("面板控制")]
        public Button closeButton;
        public GameObject panelRoot;

        private GameManager _gm;

        private void Start()
        {
            _gm = GameManager.Instance;

            if (closeButton) closeButton.onClick.AddListener(Hide);
            panelRoot?.SetActive(false);
            gameObject.SetActive(false);
        }

        public void Show()
        {
            gameObject.SetActive(true);
            panelRoot?.SetActive(true);
            Refresh();
        }

        public void Hide()
        {
            panelRoot?.SetActive(false);
            gameObject.SetActive(false);
        }

        public void Refresh()
        {
            if (_gm == null) return;

            for (int i = 0; i < spriteSlotButtons.Length; i++)
            {
                var btn = spriteSlotButtons[i];
                if (btn == null) continue;

                if (i < _gm.playerData.spriteBag.Count)
                {
                    var sprite = _gm.playerData.spriteBag[i];
                    btn.gameObject.SetActive(true);
                    btn.interactable = sprite.IsAlive;

                    if (i < slotNameTexts.Length && slotNameTexts[i] != null)
                        slotNameTexts[i].text = sprite.spriteName;
                    if (i < slotLevelTexts.Length && slotLevelTexts[i] != null)
                        slotLevelTexts[i].text = $"Lv.{sprite.level} HP:{sprite.currentHP:F0}/{sprite.maxHP:F0}";

                    int index = i;
                    btn.onClick.RemoveAllListeners();
                    btn.onClick.AddListener(() => SetActiveSprite(index));
                }
                else
                {
                    btn.gameObject.SetActive(false);
                }
            }
        }

        private void SetActiveSprite(int index)
        {
            if (_gm == null) return;
            _gm.playerData.activeSpriteIndex = index;
            EventBus.Publish(EventBus.ON_GOLD_CHANGED); // 用现有事件刷新HUD
            Hide();
        }
    }
}
