using UnityEngine;

namespace LuluDungeon
{
    /// <summary>
    /// 道具类型
    /// </summary>
    public enum ItemType
    {
        PokeBall,       // 精灵球
        HealBottle,     // 治疗药水
        SkillBottle,    // 技能药水（恢复技能次数）
        ReviveBottle,   // 复活药水
        ExpBottle       // 经验药水（战斗内禁用）
    }

    /// <summary>
    /// 精灵球品质
    /// </summary>
    public enum PokeBallQuality
    {
        Chip,    // 基础
        Normal,  // 普通
        Great,   // 稀有
        Ultra    // 超级
    }

    /// <summary>
    /// 道具数据 ScriptableObject
    /// </summary>
    [CreateAssetMenu(fileName = "NewItem", menuName = "LuluDungeon/Item Data")]
    public class ItemData : ScriptableObject
    {
        [Header("基本信息")]
        public string itemName;
        public ItemType itemType;
        public int price = 20;
        [TextArea(2, 3)]
        public string description;

        [Header("精灵球属性")]
        public PokeBallQuality ballQuality = PokeBallQuality.Chip;
        [Tooltip("捕捉随机值范围下限")]
        public int catchMin = 45;
        [Tooltip("捕捉随机值范围上限")]
        public int catchMax = 105;

        /// <summary>
        /// 获取精灵球的随机判定值
        /// </summary>
        public int GetCatchRoll()
        {
            return Random.Range(catchMin, catchMax + 1);
        }

        [Header("药水属性")]
        [Tooltip("治疗药水回复量（占总HP比例）")]
        public float healPercent = 1f;
        [Tooltip("技能药水恢复次数比例（占总上限比例）")]
        public float skillRefillPercent = 0.5f;
    }
}
