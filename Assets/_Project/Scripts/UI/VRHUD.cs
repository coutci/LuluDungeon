using UnityEngine;
using UnityEngine.UI;
using TMPro;

namespace LuluDungeon
{
    /// <summary>
    /// VR HUD - 显示在玩家手腕位置
    /// </summary>
    public class VRHUD : MonoBehaviour
    {
        [Header("HUD 元素")]
        public TextMeshProUGUI goldText;
        public TextMeshProUGUI spriteNameText;
        public TextMeshProUGUI spriteLevelText;
        public Slider hpSlider;
        public TextMeshProUGUI hpText;
        public TextMeshProUGUI floorText;

        private GameManager _gm;

        private void Start()
        {
            _gm = GameManager.Instance;

            EventBus.Subscribe(EventBus.ON_GOLD_CHANGED, Refresh);
            EventBus.Subscribe<int>(EventBus.ON_LEVEL_CHANGE, OnLevelChanged);
        }

        private void OnDestroy()
        {
            EventBus.Unsubscribe(EventBus.ON_GOLD_CHANGED, Refresh);
            EventBus.Unsubscribe<int>(EventBus.ON_LEVEL_CHANGE, OnLevelChanged);
        }

        private void Update()
        {
            Refresh();
        }

        private void OnLevelChanged(int floor)
        {
            if (floorText != null)
                floorText.text = $"B{floor}F";
        }

        public void Refresh()
        {
            if (_gm == null) return;

            if (goldText != null)
                goldText.text = $"💰 {_gm.playerData.gold}r";

            if (floorText != null)
                floorText.text = $"B{_gm.playerData.currentFloor}F";

            var activeSprite = _gm.playerData.ActiveSprite;
            if (activeSprite != null)
            {
                if (spriteNameText != null)
                    spriteNameText.text = activeSprite.spriteName;
                if (spriteLevelText != null)
                    spriteLevelText.text = $"Lv.{activeSprite.level}";
                if (hpSlider != null)
                {
                    hpSlider.maxValue = activeSprite.maxHP;
                    hpSlider.value = activeSprite.currentHP;
                }
                if (hpText != null)
                    hpText.text = $"{activeSprite.currentHP:F0}/{activeSprite.maxHP:F0}";
            }
            else
            {
                if (spriteNameText != null)
                    spriteNameText.text = "No Sprite";
                if (spriteLevelText != null)
                    spriteLevelText.text = "";
                if (hpSlider != null)
                    hpSlider.value = 0;
                if (hpText != null)
                    hpText.text = "0/0";
            }
        }
    }
}
