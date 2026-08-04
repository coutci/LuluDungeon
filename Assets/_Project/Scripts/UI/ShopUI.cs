using UnityEngine;
using UnityEngine.UI;
using TMPro;

namespace LuluDungeon
{
    /// <summary>
    /// 商店UI面板
    /// </summary>
    public class ShopUI : MonoBehaviour
    {
        [Header("商品按钮")]
        public Button[] buyButtons;
        public TextMeshProUGUI[] priceTexts;
        public TextMeshProUGUI[] nameTexts;

        [Header("玩家信息")]
        public TextMeshProUGUI goldText;
        public Button closeButton;

        private GameManager _gm;

        // 商店商品配置
        private readonly (string name, int price)[] _shopItems = new (string, int)[]
        {
            ("Chip PokeBall", 20),
            ("Normal PokeBall", 50),
            ("Great PokeBall", 100),
            ("Ultra PokeBall", 200),
            ("Heal Bottle", 50)
        };

        private void Start()
        {
            _gm = GameManager.Instance;

            if (closeButton) closeButton.onClick.AddListener(Hide);

            for (int i = 0; i < buyButtons.Length && i < _shopItems.Length; i++)
            {
                int index = i;
                var btn = buyButtons[i];
                if (btn != null)
                {
                    btn.onClick.AddListener(() => BuyItem(index));
                }

                if (nameTexts.Length > i && nameTexts[i] != null)
                    nameTexts[i].text = _shopItems[i].name;
                if (priceTexts.Length > i && priceTexts[i] != null)
                    priceTexts[i].text = $"{_shopItems[i].price}r";
            }

            EventBus.Subscribe(EventBus.ON_SHOP_ENTER, Show);
            EventBus.Subscribe(EventBus.ON_SHOP_EXIT, Hide);
            EventBus.Subscribe(EventBus.ON_GOLD_CHANGED, RefreshButtons);

            gameObject.SetActive(false);
        }

        private void OnDestroy()
        {
            EventBus.Unsubscribe(EventBus.ON_SHOP_ENTER, Show);
            EventBus.Unsubscribe(EventBus.ON_SHOP_EXIT, Hide);
            EventBus.Unsubscribe(EventBus.ON_GOLD_CHANGED, RefreshButtons);
        }

        public void Show()
        {
            gameObject.SetActive(true);
            RefreshButtons();
        }

        public void Hide()
        {
            gameObject.SetActive(false);
        }

        private void BuyItem(int index)
        {
            if (_gm == null) return;
            var item = _shopItems[index];

            if (_gm.playerData.gold >= item.price)
            {
                _gm.ChangeGold(-item.price);

                if (item.name == "Heal Bottle")
                {
                    _gm.AddItem("Heal Bottle", 1);
                    // 立即治疗当前精灵
                    var sprite = _gm.playerData.ActiveSprite;
                    if (sprite != null) sprite.FullHeal();
                }
                else
                {
                    _gm.AddItem(item.name, 1);
                }

                Debug.Log($"[Shop] Purchased {item.name} for {item.price}r");
            }
            else
            {
                Debug.Log("[Shop] Not enough gold!");
                // TODO: 播放余额不足的抖动动画
            }

            RefreshButtons();
        }

        private void RefreshButtons()
        {
            if (_gm == null) return;

            if (goldText != null)
                goldText.text = $"💰 {_gm.playerData.gold}r";

            for (int i = 0; i < buyButtons.Length && i < _shopItems.Length; i++)
            {
                if (buyButtons[i] != null)
                    buyButtons[i].interactable = _gm.playerData.gold >= _shopItems[i].price;
            }
        }
    }
}
