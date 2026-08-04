using System;
using System.Collections.Generic;
using UnityEngine;

namespace LuluDungeon
{
    /// <summary>
    /// 精灵球槽位（空 / 装载）
    /// </summary>
    [Serializable]
    public class PokeBallSlot
    {
        public bool isLoaded;             // 是否装载精灵
        public SpriteInstance sprite;     // 装载的精灵（空球为 null）
    }

    /// <summary>独立计时的状态实例（概率取最大、各自计时、效果不叠加）</summary>
    [System.Serializable]
    public class StatusTick
    {
        public float prob;      // 判定概率
        public int turns;       // 剩余判定回合
        public bool delayed;    // 上状态当回合不判定
        public StatusTick(float p, int t) { prob = p; turns = t; delayed = true; }
    }

    /// <summary>独立计时的攻防层（±0.5/层，每层 3 回合）</summary>
    [System.Serializable]
    public class BuffLayer
    {
        public float amount;    // ±0.5
        public int turns;       // 剩余回合
        public BuffLayer(float a, int t) { amount = a; turns = t; }
    }

    /// <summary>
    /// 战斗中的精灵实例数据
    /// </summary>
    public class SpriteInstance
    {
        public string spriteID;
        public string spriteName;
        public ElementType elementType;
        public int level = 1;
        public float currentHP;
        public float maxHP;
        public float attack;
        public float strength = 1f;
        public float protect = 1f;
        public int exp;
        public PokeBallQuality caughtWith = PokeBallQuality.Normal;
        public bool isDead;   // 死亡标志：true = 只播死亡动画、不可装到腰间、仅可用复活药水

        // ---- 个体波动（生成时定死，持久化）----
        public float g_hp = 1f;     // 个体血量系数 ±15%
        public float g_atk = 1f;    // 个体攻击系数 ±8%
        public float speed = 6f;    // 行动值（生成时定死）

        // ---- 成长基准（从 SpriteData 拷贝，供 LevelUp 使用）----
        public float baseHp = 50f;
        public float baseAtk = 5f;
        public float hpPerLv = 5f;
        public float atkPerLv = 0.5f;

        public SkillData[] skills = new SkillData[5];

        // ---- 技能次数池（持久资源，跨战斗不恢复；升级重置 / 技能药水恢复）----
        public int ultimateUses = SkillUses.UltimateMax;
        public int attackUses = SkillUses.BasicMax;
        public int priorityUses = SkillUses.PriorityMax;
        public int supportUses = SkillUses.SupportMax;   // 增益/减益/净化共享
        public int defenseUses = SkillUses.DefenseMax;
        public int healUses = SkillUses.HealMax;

        // ---- 每技能独立次数（与 skills 槽位一一对应，优先于旧类型池）----
        public int[] skillUses = new int[5];

        // ---- 战斗状态（v5：各层独立计时，概率取最大，效果不叠加）----
        public System.Collections.Generic.List<StatusTick> burnTicks = new System.Collections.Generic.List<StatusTick>();
        public System.Collections.Generic.List<StatusTick> poisonTicks = new System.Collections.Generic.List<StatusTick>();
        public System.Collections.Generic.List<StatusTick> sandTicks = new System.Collections.Generic.List<StatusTick>();
        public System.Collections.Generic.List<StatusTick> paralyzeTicks = new System.Collections.Generic.List<StatusTick>();
        public int leechSeedTurns;              // 寄生种子剩余回合（每次吸取 ≤ 自身 maxHP 10%）
        public SpriteInstance leechSeedOwner;   // 寄生种子归属（吸取回血对象，战斗内）
        public bool burnActiveThisTurn;         // 本回合灼烧已生效（目标造成伤害 -15%）
        public bool confusionActive;            // 混乱：行动时 30% 攻击自己（×0.8），1 回合
        public System.Collections.Generic.List<BuffLayer> strengthLayers = new System.Collections.Generic.List<BuffLayer>();
        public System.Collections.Generic.List<BuffLayer> protectLayers = new System.Collections.Generic.List<BuffLayer>();
        public int speedLevel;                  // 速度层 ±1 点/层（最多 ±3 点）
        public int speedLevelTurns;             // 速度层剩余回合

        /// <summary>是否存活：isDead 为唯一真相源（所有扣血点需同步 SyncDeathFlag）</summary>
        public bool IsAlive => !isDead;

        /// <summary>检查并同步死亡标志（HP<=0 → isDead，扣血后调用）</summary>
        public void SyncDeathFlag()
        {
            if (currentHP <= 0f) isDead = true;
        }

                /// <summary>深拷贝精灵实例（Boss 挑战制快照用；技能配置为只读共享资产）</summary>
        public SpriteInstance Clone()
        {
            var c = (SpriteInstance)MemberwiseClone();
            if (skills != null) c.skills = (SkillData[])skills.Clone();
            if (skillUses != null) c.skillUses = (int[])skillUses.Clone();
            return c;
        }

        /// <summary>复活：清死亡标志并回满血（仅复活药水可用，治疗药水不能复活）</summary>
        public void Revive()
        {
            isDead = false;
            currentHP = maxHP;
        }

        public SpriteInstance(SpriteData data, int lvl)
        {
            spriteID = data.name;
            spriteName = data.spriteName;
            elementType = data.elementType;
            level = lvl;
            g_hp = UnityEngine.Random.Range(0.90f, 1.10f);   // 个体波动 ±10%（v5.2 收窄）
            g_atk = UnityEngine.Random.Range(0.95f, 1.05f);   // 个体波动 ±5%（v5.2 收窄）
            baseHp = data.baseHP;
            baseAtk = data.baseAttack;
            hpPerLv = data.hpPerLevel;
            atkPerLv = data.attackPerLevel;
            speed = UnityEngine.Random.Range(data.speedMin, data.speedMax) + lvl * 0.2f;
            RecalcStats();
            currentHP = maxHP;
            strength = 1f;
            protect = 1f;
            skills[0] = data.skill1;
            skills[1] = data.skill2;
            skills[2] = data.skill3;
            skills[3] = data.skill4;
            skills[4] = data.skill5;
            ResetAbilityUses();
        }

        /// <summary>
        /// 按个体波动系数重算 maxHP / attack（升级或读档后调用）
        /// </summary>
        public void RecalcStats()
        {
            maxHP = CalcMaxHP(level);
            attack = CalcAttack(level);
        }

        public float CalcMaxHP(int lvl)
        {
            // 基础不波动，成长部分 × 个体系数
            return baseHp + lvl * hpPerLv * g_hp;
        }

        public float CalcAttack(int lvl)
        {
            return baseAtk + lvl * atkPerLv * g_atk;
        }

        public void FullHeal()
        {
            currentHP = maxHP;
        }

        /// <summary>
        /// 升级：属性按个体系数成长 + 次数池重置满 + 全回复
        /// </summary>
        public void LevelUp(int levels = 1)
        {
            for (int i = 0; i < levels; i++)
            {
                level += 1;
                maxHP += hpPerLv * g_hp;
                attack += atkPerLv * g_atk;
            }
            ResetAbilityUses();
            currentHP = maxHP;
        }

        // ---- 次数池 ----
        public void ResetAbilityUses()
        {
            ultimateUses = SkillUses.UltimateMax;
            attackUses = SkillUses.BasicMax;
            priorityUses = SkillUses.PriorityMax;
            supportUses = SkillUses.SupportMax;
            defenseUses = SkillUses.DefenseMax;
            healUses = SkillUses.HealMax;

            // 每技能独立次数：与 skills 槽位一一对应
            if (skillUses == null || skillUses.Length != 5) skillUses = new int[5];
            for (int i = 0; i < 5; i++)
                skillUses[i] = skills[i] != null ? SkillUses.GetMax(skills[i].skillType) : 0;
        }

        /// <summary>
        /// 技能药水：所有类型次数恢复 上限×percent（向上取整，不超上限）
        /// </summary>
        public void RefillUses(float percent)
        {
            ultimateUses = Mathf.Min(SkillUses.UltimateMax, ultimateUses + Mathf.CeilToInt(SkillUses.UltimateMax * percent));
            attackUses = Mathf.Min(SkillUses.BasicMax, attackUses + Mathf.CeilToInt(SkillUses.BasicMax * percent));
            priorityUses = Mathf.Min(SkillUses.PriorityMax, priorityUses + Mathf.CeilToInt(SkillUses.PriorityMax * percent));
            supportUses = Mathf.Min(SkillUses.SupportMax, supportUses + Mathf.CeilToInt(SkillUses.SupportMax * percent));
            defenseUses = Mathf.Min(SkillUses.DefenseMax, defenseUses + Mathf.CeilToInt(SkillUses.DefenseMax * percent));
            healUses = Mathf.Min(SkillUses.HealMax, healUses + Mathf.CeilToInt(SkillUses.HealMax * percent));

            // 每技能独立次数恢复（保留旧类型池同步，兼容旧逻辑）
            if (skillUses == null || skillUses.Length != 5) skillUses = new int[5];
            for (int i = 0; i < 5; i++)
            {
                if (skills[i] == null) continue;
                int max = SkillUses.GetMax(skills[i].skillType);
                skillUses[i] = Mathf.Min(max, skillUses[i] + Mathf.CeilToInt(max * percent));
            }
        }

        public int GetUses(SkillType t)
        {
            switch (t)
            {
                case SkillType.Ultimate: return ultimateUses;
                case SkillType.Basic: return attackUses;
                case SkillType.Priority: return priorityUses;
                case SkillType.Buff:
                case SkillType.Debuff:
                case SkillType.Cleanse: return supportUses;
                case SkillType.Defense: return defenseUses;
                case SkillType.Heal: return healUses;
                default: return 0;
            }
        }

        /// <summary>技能所在槽位索引，-1 = 不在技能列表</summary>
        public int IndexOfSkill(SkillData skill)
        {
            if (skill == null) return -1;
            for (int i = 0; i < skills.Length; i++)
                if (skills[i] == skill) return i;
            return -1;
        }

        /// <summary>按技能实例查询独立剩余次数（与技能槽位对应）</summary>
        public int GetUses(SkillData skill)
        {
            int idx = IndexOfSkill(skill);
            return idx >= 0 ? skillUses[idx] : 0;
        }

        public void SpendUses(SkillType t)
        {
            switch (t)
            {
                case SkillType.Ultimate: ultimateUses = Mathf.Max(0, ultimateUses - 1); break;
                case SkillType.Basic: attackUses = Mathf.Max(0, attackUses - 1); break;
                case SkillType.Priority: priorityUses = Mathf.Max(0, priorityUses - 1); break;
                case SkillType.Buff:
                case SkillType.Debuff:
                case SkillType.Cleanse: supportUses = Mathf.Max(0, supportUses - 1); break;
                case SkillType.Defense: defenseUses = Mathf.Max(0, defenseUses - 1); break;
                case SkillType.Heal: healUses = Mathf.Max(0, healUses - 1); break;
            }
        }

        /// <summary>按技能实例消耗独立次数（与技能槽位对应）</summary>
        public void SpendUses(SkillData skill)
        {
            int idx = IndexOfSkill(skill);
            if (idx >= 0) skillUses[idx] = Mathf.Max(0, skillUses[idx] - 1);
        }

        /// <summary>
        /// 该技能是否可用（次数>0）
        /// </summary>
        public bool CanUseSkill(SkillData skill)
        {
            return skill != null && GetUses(skill) > 0;
        }

        // ---- 状态效果（v5）----
        /// <summary>施加状态：新实例当回合不判定（delayed），从对方下一回合初开始判定</summary>
        public void ApplyStatus(StatusEffect e, int turns, float prob)
        {
            switch (e)
            {
                case StatusEffect.Burn:
                    burnTicks.Add(new StatusTick(prob, turns));
                    break;
                case StatusEffect.Poison:
                    poisonTicks.Add(new StatusTick(prob, turns));
                    break;
                case StatusEffect.Sandstorm:
                    sandTicks.Add(new StatusTick(prob, turns));
                    break;
                case StatusEffect.Paralyze:
                    paralyzeTicks.Add(new StatusTick(prob, turns));
                    break;
                case StatusEffect.LeechSeed:
                    leechSeedTurns = Mathf.Max(leechSeedTurns, turns);
                    break;
                case StatusEffect.Confusion:
                    confusionActive = true;
                    break;
            }
        }

        /// <summary>回合初状态结算：判定各状态（概率取最大实例），返回本回合状态伤害（真伤）</summary>
        public float TickStartOfTurn()
        {
            float dmg = 0f;
            burnActiveThisTurn = RollStatus(burnTicks);          // 灼烧：判定成功 -4% + 本回合伤害 -15%
            if (burnActiveThisTurn) dmg += maxHP * 0.04f;
            if (RollStatus(poisonTicks)) dmg += maxHP * 0.06f;    // 中毒 -6%
            if (RollStatus(sandTicks)) dmg += maxHP * 0.04f;      // 沙暴 -4%（v5.2）
            if (leechSeedTurns > 0) { leechSeedTurns--; dmg += maxHP * 0.05f; }  // 寄生种子 5%
            if (confusionActive) confusionActive = false;         // 混乱持续 1 回合
            if (dmg > 0f)
            {
                currentHP = Mathf.Max(0f, currentHP - dmg);
                SyncDeathFlag();
            }
            return dmg;
        }

        /// <summary>对一组独立计时的状态实例判定：概率取最大，成功返回 true；所有实例回合-1并清理</summary>
        private static bool RollStatus(System.Collections.Generic.List<StatusTick> ticks)
        {
            if (ticks == null || ticks.Count == 0) return false;
            // 当回合不判定：全部延迟实例仅清除延迟标记（不消耗回合）
            bool anyDelayed = false;
            for (int i = ticks.Count - 1; i >= 0; i--)
            {
                if (ticks[i].delayed) { ticks[i].delayed = false; anyDelayed = true; }
            }
            if (anyDelayed) return false;
            // 概率取最大
            float maxProb = 0f;
            foreach (var t in ticks) if (t.prob > maxProb) maxProb = t.prob;
            bool success = UnityEngine.Random.value < maxProb;
            // 回合-1并清理
            for (int i = ticks.Count - 1; i >= 0; i--)
            {
                ticks[i].turns--;
                if (ticks[i].turns <= 0) ticks.RemoveAt(i);
            }
            return success;
        }

        /// <summary>本回合是否被麻痹跳过行动（概率取最大实例）</summary>
        public bool IsParalyzedThisTurn()
        {
            return RollStatus(paralyzeTicks);
        }

        /// <summary>回合末结算：攻防层/速度层持续-1</summary>
        public void TickEndOfTurn()
        {
            for (int i = strengthLayers.Count - 1; i >= 0; i--)
            {
                strengthLayers[i].turns--;
                if (strengthLayers[i].turns <= 0) strengthLayers.RemoveAt(i);
            }
            for (int i = protectLayers.Count - 1; i >= 0; i--)
            {
                protectLayers[i].turns--;
                if (protectLayers[i].turns <= 0) protectLayers.RemoveAt(i);
            }
            if (speedLevelTurns > 0)
            {
                speedLevelTurns--;
                if (speedLevelTurns <= 0) speedLevel = 0;
            }
            RecalcMods();
        }

        // ---- 攻防层 / 速度层 ----
        /// <summary>施加攻/防层（±0.5），攻击侧上限 2 层、防御侧上限 3 层；当前回合立即生效</summary>
        public void ApplyLayer(bool isStrength, float amount)
        {
            var list = isStrength ? strengthLayers : protectLayers;
            int positive = 0, negative = 0;
            foreach (var l in list) { if (l.amount > 0) positive++; else negative++; }
            int max = isStrength ? 2 : 3;
            if (amount > 0 && positive >= max) return;
            if (amount < 0 && negative >= max) return;
            list.Add(new BuffLayer(amount, 3));
            RecalcMods();
        }

        /// <summary>清除全部增益层（净化用；减益不清）</summary>
        public void CleansePositiveLayers()
        {
            for (int i = strengthLayers.Count - 1; i >= 0; i--)
                if (strengthLayers[i].amount > 0) strengthLayers.RemoveAt(i);
            for (int i = protectLayers.Count - 1; i >= 0; i--)
                if (protectLayers[i].amount > 0) protectLayers.RemoveAt(i);
            RecalcMods();
        }

        /// <summary>当前防御正层数（防御指令用）</summary>
        public int PositiveProtectLayers()
        {
            int n = 0;
            foreach (var l in protectLayers) if (l.amount > 0) n++;
            return n;
        }

        /// <summary>是否有攻击增益层（蓄能冲撞用）</summary>
        public bool HasStrengthBuff()
        {
            foreach (var l in strengthLayers) if (l.amount > 0) return true;
            return false;
        }

        /// <summary>速度层：±1 点/层（最多 ±3），持续 3 回合（当前回合生效）</summary>
        public void ApplySpeedLevel(int delta)
        {
            speedLevel = Mathf.Clamp(speedLevel + delta, -3, 3);
            speedLevelTurns = 3;
        }

        /// <summary>重算攻防系数</summary>
        public void RecalcMods()
        {
            float s = 1f, p = 1f;
            foreach (var l in strengthLayers) s += l.amount;
            foreach (var l in protectLayers) p += l.amount;
            strength = Mathf.Clamp(s, 0.5f, 2.0f);
            protect = Mathf.Clamp(p, 0.5f, 2.5f);
        }
    }

    /// <summary>
    /// 玩家数据
    /// </summary>
    [Serializable]
    public class PlayerData
    {
        public int currentFloor = 0;
        public bool bossDefeated;      // 当前层 Boss 是否已击败（楼梯门控）
        public bool guideGifted;      // 向导是否已赠送初始精灵（防读档后重复送）
        public int gold = 100;
        public int chipPokeBalls = 0;
        public int normalPokeBalls = 2;
        public int greatPokeBalls = 0;
        public int ultraPokeBalls = 0;
        public int healBottles = 0;
        public int reviveBottles = 0;   // 复活药水
        public int expBottles = 0;      // 经验瓶
        public int skillBottles = 0;    // 技能药水
        public List<SpriteInstance> spriteBag = new List<SpriteInstance>();
        public int activeSpriteIndex = 0;
        public List<PokeBallSlot> pokeBallSlots = new List<PokeBallSlot>();

        /// <summary>
        /// 确保腰间有固定数量的槽位（空槽 = 未装载）
        /// </summary>
        public void EnsureBeltSlots(int count = 6)
        {
            while (pokeBallSlots.Count < count)
                pokeBallSlots.Add(new PokeBallSlot());
        }

        public SpriteInstance ActiveSprite
        {
            get
            {
                if (spriteBag.Count == 0) return null;
                if (activeSpriteIndex >= spriteBag.Count) activeSpriteIndex = 0;
                return spriteBag[activeSpriteIndex];
            }
        }

        /// <summary>
        /// 是否有任何存活精灵
        /// </summary>
        public bool HasAliveSprite => spriteBag.Exists(s => s.IsAlive);
    }

    /// <summary>
    /// 游戏全局管理器 - 单例
    /// </summary>
    public class GameManager : MonoBehaviour
    {
        public static GameManager Instance { get; private set; }

        [Header("游戏状态")]
        public PlayerData playerData = new PlayerData();
        public int activeSaveSlot = -1;   // 当前绑定的存档槽（1~5），-1 = 未绑定（编辑器直开调试）

        [Header("精灵配置")]
        public List<SpriteData> allSpriteConfigs = new List<SpriteData>();

        [Header("道具配置")]
        public List<ItemData> allItemConfigs = new List<ItemData>();

        [Header("地板层配置")]
        public int startFloor = 0;   // 新楼层体系：0 层起点 → 9 层最终 Boss
        public int endFloor = 9;

        public bool IsPaused { get; private set; }
        public bool IsInBattle { get; private set; }

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
            DontDestroyOnLoad(gameObject);

            playerData.currentFloor = startFloor;
            playerData.EnsureBeltSlots(6);
        

            // 全局音频:确保 AudioManager 存在(跨场景持久)
            AudioManager.EnsureInstance();}

        private void Start()
        {
            EventBus.Subscribe<BattleResult>(EventBus.ON_BATTLE_END, OnBattleEnd);
            EventBus.Subscribe(EventBus.ON_GAME_OVER, OnGameOver);
        }

        private void OnDestroy()
        {
            EventBus.Unsubscribe<BattleResult>(EventBus.ON_BATTLE_END, OnBattleEnd);
            EventBus.Unsubscribe(EventBus.ON_GAME_OVER, OnGameOver);
        }

        public void SetBattleState(bool inBattle)
        {
            IsInBattle = inBattle;
        }

        public void PauseGame()
        {
            IsPaused = true;
            Time.timeScale = 0f;
        }

        public void ResumeGame()
        {
            IsPaused = false;
            Time.timeScale = 1f;
        }

        public void ChangeGold(int amount)
        {
            playerData.gold = Mathf.Max(0, playerData.gold + amount);
            EventBus.Publish(EventBus.ON_GOLD_CHANGED);
        }

        public void AddSpriteToBag(SpriteInstance sprite)
        {
            playerData.spriteBag.Add(sprite);
            if (playerData.spriteBag.Count == 1)
                playerData.activeSpriteIndex = 0;
            // 新精灵自动装载到第一个空腰带槽（腰带已满则留在背包）
            TryEquipSpriteToBelt(sprite);
        }

        /// <summary>腰间槽是否已满</summary>
        public bool IsBeltFull => playerData.pokeBallSlots.TrueForAll(s => s.isLoaded);

        /// <summary>查询精灵所在的腰间槽位，-1 = 不在腰间</summary>
        public int GetBeltSlotOf(SpriteInstance sprite)
        {
            var slots = playerData.pokeBallSlots;
            for (int i = 0; i < slots.Count; i++)
                if (slots[i].isLoaded && slots[i].sprite == sprite) return i;
            return -1;
        }

        /// <summary>将精灵装载到腰间（找第一个空槽），已装载返回 true</summary>
        public bool TryEquipSpriteToBelt(SpriteInstance sprite)
        {
            if (sprite == null) return false;
            if (GetBeltSlotOf(sprite) >= 0) return true;   // 已在腰间
            var slots = playerData.pokeBallSlots;
            for (int i = 0; i < slots.Count; i++)
            {
                if (!slots[i].isLoaded)
                {
                    slots[i].isLoaded = true;
                    slots[i].sprite = sprite;
                    return true;
                }
            }
            return false;   // 腰带已满
        }

        /// <summary>将精灵从腰间放回背包</summary>
        public void UnequipSpriteFromBelt(SpriteInstance sprite)
        {
            if (sprite == null) return;
            var slots = playerData.pokeBallSlots;
            for (int i = 0; i < slots.Count; i++)
            {
                if (slots[i].isLoaded && slots[i].sprite == sprite)
                {
                    slots[i].isLoaded = false;
                    slots[i].sprite = null;
                    return;
                }
            }
        }

                // ---- Boss 挑战制：战前快照 / 失败回滚 ----
        private PlayerData _battleSnapshot;

        /// <summary>Boss 战开始前：深拷贝玩家完整状态</summary>
        public void TakeBattleSnapshot()
        {
            _battleSnapshot = ClonePlayerData(playerData);
        }

        /// <summary>Boss 战失败/逃跑：整体回滚到战前状态</summary>
        public void RestoreBattleSnapshot()
        {
            if (_battleSnapshot == null) return;
            CopyPlayerData(_battleSnapshot, playerData);
            _battleSnapshot = null;
        }

        /// <summary>Boss 战胜利：丢弃快照</summary>
        public void DiscardBattleSnapshot()
        {
            _battleSnapshot = null;
        }

        /// <summary>深拷贝 PlayerData（精灵按索引重建腰带引用）</summary>
        private static PlayerData ClonePlayerData(PlayerData src)
        {
            var dst = new PlayerData
            {
                currentFloor = src.currentFloor,
                gold = src.gold,
                chipPokeBalls = src.chipPokeBalls,
                normalPokeBalls = src.normalPokeBalls,
                greatPokeBalls = src.greatPokeBalls,
                ultraPokeBalls = src.ultraPokeBalls,
                healBottles = src.healBottles,
                reviveBottles = src.reviveBottles,
                expBottles = src.expBottles,
                skillBottles = src.skillBottles,
                activeSpriteIndex = src.activeSpriteIndex,
                bossDefeated = src.bossDefeated
            };
            foreach (var s in src.spriteBag)
                dst.spriteBag.Add(s != null ? s.Clone() : null);
            CopyBeltRefs(src, dst);
            return dst;
        }

        /// <summary>回写 PlayerData（快照 → 当前）</summary>
        private static void CopyPlayerData(PlayerData src, PlayerData dst)
        {
            dst.currentFloor = src.currentFloor;
            dst.gold = src.gold;
            dst.chipPokeBalls = src.chipPokeBalls;
            dst.normalPokeBalls = src.normalPokeBalls;
            dst.greatPokeBalls = src.greatPokeBalls;
            dst.ultraPokeBalls = src.ultraPokeBalls;
            dst.healBottles = src.healBottles;
            dst.reviveBottles = src.reviveBottles;
            dst.expBottles = src.expBottles;
            dst.skillBottles = src.skillBottles;
            dst.activeSpriteIndex = src.activeSpriteIndex;
            dst.bossDefeated = src.bossDefeated;

            dst.spriteBag.Clear();
            foreach (var s in src.spriteBag)
                dst.spriteBag.Add(s != null ? s.Clone() : null);
            CopyBeltRefs(src, dst);
        }

        /// <summary>按精灵在 spriteBag 中的索引重建腰带槽引用</summary>
        private static void CopyBeltRefs(PlayerData src, PlayerData dst)
        {
            dst.pokeBallSlots.Clear();
            dst.EnsureBeltSlots(6);
            for (int i = 0; i < dst.pokeBallSlots.Count; i++)
            {
                var srcSlot = i < src.pokeBallSlots.Count ? src.pokeBallSlots[i] : null;
                if (srcSlot != null && srcSlot.isLoaded && srcSlot.sprite != null)
                {
                    int idx = src.spriteBag.IndexOf(srcSlot.sprite);
                    if (idx >= 0 && idx < dst.spriteBag.Count)
                    {
                        dst.pokeBallSlots[i].isLoaded = true;
                        dst.pokeBallSlots[i].sprite = dst.spriteBag[idx];
                    }
                }
            }
        }

        /// <summary>开始新游戏：绑定空槽、重置数据、立即可读档</summary>
        public void StartNewGame(int slot)
        {
            if (slot < 1 || slot > DataManager.MaxSlots) return;
            activeSaveSlot = slot;
            playerData = new PlayerData();
            playerData.currentFloor = startFloor;
            playerData.EnsureBeltSlots(6);

            var dm = DataManager.Instance;
            if (dm != null)
            {
                dm.DeleteSave(slot);          // 覆盖旧档，从零开始
                dm.SaveGame(slot);            // 立即落盘（主菜单列表可见）
            }
            Debug.Log($"[GameManager] New game started on slot {slot}.");
        }

        /// <summary>读档：绑定槽位并载入数据；失败返回 false</summary>
        public bool LoadGameFromSlot(int slot)
        {
            var dm = DataManager.Instance;
            if (dm == null || !dm.HasSaveFile(slot) || !dm.LoadGame(slot)) return false;
            activeSaveSlot = slot;
            Debug.Log($"[GameManager] Game loaded from slot {slot}, floor {playerData.currentFloor}.");
            return true;
        }

        /// <summary>自动保存并返回主菜单（暂停菜单/退出流程）</summary>
        public void ReturnToMainMenu()
        {
            Time.timeScale = 1f;
            var dm = DataManager.Instance;
            if (dm != null) dm.AutoSave();
            UnityEngine.SceneManagement.SceneManager.LoadScene("MainMenu");
        }

        /// <summary>Game Over：全灭且无法恢复 → 回主菜单（保留存档，读档回到最近进层状态）</summary>
        private void OnGameOver()
        {
            Debug.Log("[GameManager] Game Over → return to main menu.");
            Time.timeScale = 1f;
            UnityEngine.SceneManagement.SceneManager.LoadScene("MainMenu");
        }

        public void ChangeFloor(int newFloor)
        {
            playerData.currentFloor = newFloor;
            playerData.bossDefeated = false;   // 新层 Boss 未击败，楼梯保持封印
            EventBus.Publish<int>(EventBus.ON_LEVEL_CHANGE, newFloor);
        }

        /// <summary>Boss 击败：记录标记、激活楼梯；到达最终层触发通关</summary>
        public void OnBossDefeated(int floor)
        {
            playerData.bossDefeated = true;
            var dg = DungeonGenerator.Instance;
            if (dg != null) dg.ActivateStairs();
            if (floor >= endFloor)
            {
                EventBus.Publish(EventBus.ON_GAME_WIN);
            }
        }

        public void AddItem(string itemName, int count = 1)
        {
            switch (itemName)
            {
                case "Chip PokeBall": playerData.chipPokeBalls += count; break;
                case "Normal PokeBall": playerData.normalPokeBalls += count; break;
                case "Great PokeBall": playerData.greatPokeBalls += count; break;
                case "Ultra PokeBall": playerData.ultraPokeBalls += count; break;
                case "Heal Bottle": playerData.healBottles += count; break;
                case "Revive Bottle": playerData.reviveBottles += count; break;
                case "Exp Bottle": playerData.expBottles += count; break;
                case "Skill Bottle": playerData.skillBottles += count; break;
            }
        }

        public bool HasItem(string itemName)
        {
            return itemName switch
            {
                "Chip PokeBall" => playerData.chipPokeBalls > 0,
                "Normal PokeBall" => playerData.normalPokeBalls > 0,
                "Great PokeBall" => playerData.greatPokeBalls > 0,
                "Ultra PokeBall" => playerData.ultraPokeBalls > 0,
                "Heal Bottle" => playerData.healBottles > 0,
                "Revive Bottle" => playerData.reviveBottles > 0,
                "Exp Bottle" => playerData.expBottles > 0,
                "Skill Bottle" => playerData.skillBottles > 0,
                _ => false
            };
        }

        public void UseItem(string itemName)
        {
            switch (itemName)
            {
                case "Chip PokeBall": playerData.chipPokeBalls = Mathf.Max(0, playerData.chipPokeBalls - 1); break;
                case "Normal PokeBall": playerData.normalPokeBalls = Mathf.Max(0, playerData.normalPokeBalls - 1); break;
                case "Great PokeBall": playerData.greatPokeBalls = Mathf.Max(0, playerData.greatPokeBalls - 1); break;
                case "Ultra PokeBall": playerData.ultraPokeBalls = Mathf.Max(0, playerData.ultraPokeBalls - 1); break;
                case "Heal Bottle": playerData.healBottles = Mathf.Max(0, playerData.healBottles - 1); break;
                case "Revive Bottle": playerData.reviveBottles = Mathf.Max(0, playerData.reviveBottles - 1); break;
                case "Exp Bottle": playerData.expBottles = Mathf.Max(0, playerData.expBottles - 1); break;
                case "Skill Bottle": playerData.skillBottles = Mathf.Max(0, playerData.skillBottles - 1); break;
            }
        }

        private void OnBattleEnd(BattleResult result)
        {
            SetBattleState(false);
            if (result.playerWon && result.isBoss)
            {
                DiscardBattleSnapshot();   // Boss 战胜利：快照作废
            }
            if (result.playerWon && result.caughtSprite != null)
            {
                AddSpriteToBag(result.caughtSprite);
                ChangeGold(result.goldReward);
                EventBus.Publish<SpriteInstance>(EventBus.ON_SPRITE_CAUGHT, result.caughtSprite);
            }
            // 失败/全灭流程由 BattleManager 处理（含复活药水/金币兜底判断），此处不再触发 Game Over
        }
    }

    /// <summary>
    /// 战斗结果数据
    /// </summary>
    [Serializable]
    public class BattleResult
    {
        public bool playerWon;
        public bool caught;
        public SpriteInstance caughtSprite;
        public int goldReward;
        public bool escaped;
        public bool isBoss;   // 是否为 Boss 战（挑战制：胜利作废快照，失败回滚）
    }
}


