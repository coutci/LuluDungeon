using System.Collections.Generic;
using UnityEngine;

namespace LuluDungeon
{
    /// <summary>
    /// 怪物组件：状态标记 + 完整战斗数据（SpriteInstance 承载）
    /// + 头顶名称/等级标签（始终朝向玩家）
    /// </summary>
    public class Monster : MonoBehaviour
    {
        [Header("状态")]
        public MonsterState state = MonsterState.Exploring;

        [Header("精灵配置")]
        public SpriteData spriteData;

        [Header("显示名称（模型名）")]
        public string displayName = "";

        [Header("战斗数据（由 Init 生成）")]
        public SpriteInstance spriteInstance;

        [Header("头顶标签")]
        [Tooltip("标签格式：名称 + 等级")]
        public bool showLabel = true;
        public float labelHeight = 2.5f;
        public float labelScale = 0.04f;

        // ---- 便捷属性（代理到 spriteInstance）----
        public int Level => spriteInstance != null ? spriteInstance.level : 0;
        public ElementType Element => spriteInstance != null ? spriteInstance.elementType : ElementType.Fire;
        public float MaxHP => spriteInstance != null ? spriteInstance.maxHP : 0f;
        public float CurrentHP => spriteInstance != null ? spriteInstance.currentHP : 0f;
        public int Exp => spriteInstance != null ? spriteInstance.exp : 0;
        public SkillData[] Skills => spriteInstance != null ? spriteInstance.skills : null;

        public bool InBattle => state == MonsterState.Battle;
        public bool IsAlive => spriteInstance == null || spriteInstance.IsAlive;

        private GameObject _label;

        /// <summary>
        /// 初始化：按层生成等级，创建 SpriteInstance + 头顶标签
        /// </summary>
        /// <summary>
        /// 初始化：按层生成等级，创建 SpriteInstance + 头顶标签
        /// </summary>
        public void Init(int floor)
        {
            int lvl = RandomLevelForFloor(floor);
            spriteInstance = new SpriteInstance(spriteData, lvl);
            CreateLabel();
        }

        /// <summary>
        /// 设计文档等级公式：普通野生 = 4×(11-floor) + rand(0,5)
        /// </summary>
        public static int RandomLevelForFloor(int floor)
        {
            return Mathf.Max(1, 3 * floor + Random.Range(1, 5));
        }

        /// <summary>Boss 队伍等级：同层野怪等级 + 4~6</summary>
        public static int RandomBossLevelForFloor(int floor)
        {
            return RandomLevelForFloor(floor) + Random.Range(4, 7);
        }

        /// <summary>
        /// 被精灵球击中：弹出"进入战斗"提示（旧占位，战斗已接入后仅 WildSprite 使用）
        /// </summary>
        public void ShowEnterBattlePrompt()
        {
            var go = new GameObject("BattlePrompt");
            go.transform.SetParent(transform, false);
            go.transform.localPosition = new Vector3(0, labelHeight + 0.5f, 0);

            var tm = go.AddComponent<TextMesh>();
            tm.text = "进入战斗！";
            tm.fontSize = 120;
            tm.characterSize = labelScale;
            tm.color = Color.yellow;
            tm.anchor = TextAnchor.MiddleCenter;
            tm.alignment = TextAlignment.Center;

            var bb = go.AddComponent<Billboard>();
            bb.flipDirection = true;   // TextMesh 从 -Z 面可读
            bb.lockYAxis = true;

            Destroy(go, 2.5f);
        }

        /// <summary>受击</summary>
        public void TakeDamage(float damage)
        {
            if (spriteInstance != null)
            {
                spriteInstance.currentHP = Mathf.Max(0f, spriteInstance.currentHP - damage);
                spriteInstance.SyncDeathFlag();
            }
        }

        /// <summary>获得经验</summary>
        public void GainExp(int amount)
        {
            if (spriteInstance != null)
                spriteInstance.exp += amount;
        }

        /// <summary>
        /// 投球命中后触发战斗入口
        /// </summary>
        public void TriggerBattle(SpriteInstance playerSprite = null)
        {
            if (state == MonsterState.Battle) return;
            if (spriteInstance == null || !spriteInstance.IsAlive) return;

            state = MonsterState.Battle;
            var bm = BattleManager.Instance;
            if (bm != null)
            {
                bm.StartBattle(spriteInstance, false, this, playerSprite);
            }
        }

        /// <summary>
        /// 战斗结束回调：胜利（击败/捕捉）→ 怪物消失；逃跑/全灭 → 恢复探索
        /// </summary>
        public void OnBattleEnded(bool removed)
        {
            state = MonsterState.Exploring;
            if (removed)
            {
                Destroy(gameObject, 1.5f);
            }
        }

        /// <summary>
        /// 创建头顶标签：名称 + 等级，始终朝向玩家
        /// </summary>
        private void CreateLabel()
        {
            if (!showLabel || spriteInstance == null) return;
            if (_label != null) return;

            _label = new GameObject("Label");
            _label.transform.SetParent(transform, false);
            _label.transform.localPosition = new Vector3(0, labelHeight, 0);

            // 名称：优先模型名，回退到配置名
            string name = string.IsNullOrEmpty(displayName) ? spriteInstance.spriteName : displayName;

            var tm = _label.AddComponent<TextMesh>();
            tm.text = $"{name} lv.{spriteInstance.level:D2}";
            tm.fontSize = 80;
            tm.characterSize = labelScale;
            tm.color = Color.white;
            tm.anchor = TextAnchor.MiddleCenter;
            tm.alignment = TextAlignment.Center;

            // 使用默认 TextMesh 材质（白色文字，正常渲染）

            var bb = _label.AddComponent<Billboard>();
            bb.flipDirection = true;  // TextMesh 从 -Z 面可读
            bb.lockYAxis = true;
        }
    }
}
