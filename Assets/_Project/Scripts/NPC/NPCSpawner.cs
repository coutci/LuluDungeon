using UnityEngine;

namespace LuluDungeon
{
    /// <summary>
    /// NPC 生成器：商人（ChestMonster，带商店）+ 向导（EvilMage，带对话/赠送）
    /// </summary>
    public class NPCSpawner : MonoBehaviour
    {
        [Header("标签")]
        public string merchantLabel = "商店";
        public string guideLabel = "向导";

        [Header("生成位置")]
        public Vector3 merchantPos = new Vector3(10f, 0.05f, 2f);
        public Vector3 guidePos = new Vector3(17.4f, 0.05f, 10f);

        private void Start()
        {
            // 楼层内容已由 DungeonGenerator 统一生成，旧路径不再使用
            if (DungeonGenerator.Instance != null) return;

            var preloader = GetComponent<Preloader>();
            if (preloader == null) { Debug.LogError("[NPCSpawner] Preloader missing!"); return; }

            var fg = transform.Find("Floor_00");
            if (fg == null) return;

            // 商人（ChestMonster）— 南入口，带商店
            if (preloader.merchantPrefab != null)
            {
                var m = Instantiate(preloader.merchantPrefab, fg);
                m.name = "NPC_Merchant";
                m.transform.localPosition = merchantPos;
                var mi = m.AddComponent<NPCIdle>();
                mi.label = merchantLabel;
                m.AddComponent<ShopNPC>();
            }

            // 向导（EvilMage）— 中央，带对话/赠送
            if (preloader.guidePrefab != null)
            {
                var g = Instantiate(preloader.guidePrefab, fg);
                g.name = "NPC_Guide";
                g.transform.localPosition = guidePos;
                g.transform.localRotation = Quaternion.Euler(0, 180, 0);
                var gi = g.AddComponent<NPCIdle>();
                gi.label = guideLabel;

                var gn = g.AddComponent<GuideNPC>();
                gn.giftSpriteData = preloader.stingRaySpriteData;
            }
        }
    }
}
