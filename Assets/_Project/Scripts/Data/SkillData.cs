using UnityEngine;

namespace LuluDungeon
{
    /// <summary>
    /// 技能类型
    /// </summary>
    public enum SkillType
    {
        Basic = 0,      // 普攻
        Buff = 1,       // 增益
        Ultimate = 2,   // 大招
        Debuff = 3,     // 减益（负面）
        Cleanse = 4,    // 净化（清除对方增益）
        Defense = 5,    // 防御（预判减伤）
        Heal = 6,       // 回复
        Priority = 7,   // 先制（必定先手）
        Hazard = 8      // 持续伤害状态技（沙暴）
    }

    /// <summary>
    /// Buff类型
    /// </summary>
    public enum BuffType
    {
        None,
        Strength,   // 攻击强化
        Protect     // 防御强化
    }

    /// <summary>
    /// 状态效果（附属在攻击技能上，按概率附加）
    /// </summary>
    public enum StatusEffect
    {
        None,
        Burn,           // 灼烧：回合初判定成功 -4% maxHP + 本回合伤害 -15%
        Poison,         // 中毒：回合初判定成功 -6% maxHP
        Paralyze,       // 麻痹：回合初判定成功跳过本回合行动
        Sandstorm,      // 沙暴：回合初判定成功 -3% maxHP（5 回合，不可净化）
        LeechSeed,      // 寄生种子：回合初吸取 5% maxHP（每次 ≤ 自身 10%）
        Confusion       // 混乱：行动时 30% 攻击自己（×0.8），1 回合
    }

    /// <summary>
    /// 技能数据 ScriptableObject
    /// </summary>
    [CreateAssetMenu(fileName = "NewSkill", menuName = "LuluDungeon/Skill Data")]
    public class SkillData : ScriptableObject
    {
        [Header("基本信息")]
        public string skillName;
        [TextArea(2, 4)]
        public string description;
        public SkillType skillType;
        public ElementType elementType;

        [Header("战斗数值")]
        [Tooltip("伤害倍率（仅攻击类有效）")]
        public float damageMultiplier = 1.0f;
        [Tooltip("Buff/减益类型")]
        public BuffType buffType = BuffType.None;
        [Tooltip("Buff/减益数值增量")]
        public float buffAmount = 0.5f;
        [Tooltip("回复比例（仅Heal类有效）")]
        public float healPercent = 0f;
        [Tooltip("吸血比例：回复造成伤害的百分比（仅攻击类）")]
        public float lifestealPercent = 0f;

        [Header("状态附加（仅攻击类）")]
        public StatusEffect statusEffect = StatusEffect.None;
        [Range(0f, 1f)]
        public float statusChance = 0f;

        [Header("持续回合")]
        [Tooltip("增益/减益/状态持续回合数")]
        public int duration = 3;

        [Header("表现")]
        public string animationTrigger = "Attack";
        public string effectName;

        /// <summary>
        /// 是否造成伤害的技能
        /// </summary>
        public bool IsDamageSkill
        {
            get
            {
                return skillType == SkillType.Basic || skillType == SkillType.Ultimate
                    || skillType == SkillType.Priority;
            }
        }

        /// <summary>
        /// 是否为攻击类（可被防御挡下）
        /// </summary>
        public bool IsAttackClass
        {
            get
            {
                return skillType == SkillType.Basic || skillType == SkillType.Ultimate
                    || skillType == SkillType.Priority;
            }
        }
    }

    /// <summary>
    /// 技能次数池工具：类型 → 每场战斗次数上限
    /// 增益/减益/净化共享同一池（supportUses）
    /// </summary>
    public static class SkillUses
    {
        public const int UltimateMax = 2;
        public const int BasicMax = 5;
        public const int PriorityMax = 5;
        public const int SupportMax = 3;    // 增益/减益/净化共享
        public const int DefenseMax = 5;
        public const int HealMax = 3;

        public static int GetMax(SkillType t)
        {
            switch (t)
            {
                case SkillType.Ultimate: return UltimateMax;
                case SkillType.Basic: return BasicMax;
                case SkillType.Priority: return PriorityMax;
                case SkillType.Buff:
                case SkillType.Debuff:
                case SkillType.Cleanse: return SupportMax;
                case SkillType.Defense: return DefenseMax;
                case SkillType.Heal: return HealMax;
                case SkillType.Hazard: return SupportMax;   // 与辅助共享 3 次
                default: return 0;
            }
        }
    }
}
