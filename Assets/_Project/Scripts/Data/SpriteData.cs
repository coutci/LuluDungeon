using System.Collections.Generic;
using UnityEngine;

namespace LuluDungeon
{
    /// <summary>
    /// 精灵类型（区分野生、Boss等）
    /// </summary>
    public enum SpriteRole
    {
        Normal,
        Boss,
        Starter
    }

    /// <summary>
    /// 精灵数据 ScriptableObject
    /// </summary>
    [CreateAssetMenu(fileName = "NewSprite", menuName = "LuluDungeon/Sprite Data")]
    public class SpriteData : ScriptableObject
    {
        [Header("基本信息")]
        public string spriteName;
        public ElementType elementType;
        public SpriteRole role = SpriteRole.Normal;

        [Header("数值成长")]
        public float baseHP = 50f;
        public float baseAttack = 5f;
        public float hpPerLevel = 5f;
        public float attackPerLevel = 0.5f;

        [Header("行动值（个体区间）")]
        [Tooltip("个体行动值下限")]
        public float speedMin = 6f;
        [Tooltip("个体行动值上限")]
        public float speedMax = 10f;

        [Header("技能（5槽专属池，野怪随机3 / 玩家5选3）")]
        public SkillData skill1;
        public SkillData skill2;
        public SkillData skill3;
        public SkillData skill4;
        public SkillData skill5;

        [Header("3D表现 - 占位体")]
        public Color placeholderColor = Color.white;
        public float modelScale = 1f;

        [Header("捕捉")]
        [Range(0, 1)]
        public float catchRateModifier = 1f;

        /// <summary>
        /// 获取某等级下的基准最大HP（不含个体波动，波动在 SpriteInstance 层）
        /// </summary>
        public float GetMaxHP(int level)
        {
            return baseHP + level * hpPerLevel;
        }

        /// <summary>
        /// 获取某等级下的基准攻击力（不含个体波动）
        /// </summary>
        public float GetAttack(int level)
        {
            return baseAttack + level * attackPerLevel;
        }
    }
}
