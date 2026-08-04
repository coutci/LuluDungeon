using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace LuluDungeon
{
    /// <summary>
    /// 存档数据
    /// </summary>
    [System.Serializable]
    public class SaveData
    {
        // 存档版本：v2 = 新楼层体系（0 层起点 → 9 层 Boss，楼梯门控）；旧档直接作废
        public int saveVersion = 2;

        // 保存时间（yyyy-MM-dd HH:mm:ss，主菜单列表显示用）
        public string saveTime;

        // Player State
        public int currentFloor;
        public bool bossDefeated;   // 当前层 Boss 是否已击败（读档恢复楼梯状态）
        public bool guideGifted;   // 向导是否已赠送初始精灵
        public int gold;
        public int chipPokeBalls;
        public int normalPokeBalls;
        public int greatPokeBalls;
        public int ultraPokeBalls;
        public int healBottles;
        public int reviveBottles;
        public int expBottles;
        public int skillBottles;
        public List<int> beltSlots = new List<int>();   // 每槽对应 spriteBag 索引，-1 = 空

        // Sprite State
        public List<SpriteSaveEntry> sprites = new List<SpriteSaveEntry>();
        public int activeSpriteIndex;
    }

    [System.Serializable]
    public class SpriteSaveEntry
    {
        public string spriteID;
        public string spriteName;
        public int level;
        public float currentHP;
        public float maxHP;
        public float attack;
        public float strength;
        public float protect;
        public int exp;
        public PokeBallQuality caughtWith = PokeBallQuality.Normal;

        // 个体波动（旧档缺失时默认 1 = 基准值）
        public float g_hp = 1f;
        public float g_atk = 1f;
        public float speed = 6f;

        // 技能次数池（旧档缺失时默认满）
        public int ultimateUses = SkillUses.UltimateMax;
        public int attackUses = SkillUses.BasicMax;
        public int priorityUses = SkillUses.PriorityMax;
        public int supportUses = SkillUses.SupportMax;
        public int defenseUses = SkillUses.DefenseMax;
        public int healUses = SkillUses.HealMax;

        // 装备的技能索引（-1 = 未装备）
        public List<int> equippedSkills = new List<int>();

        // 每技能槽位独立剩余次数（新档；旧档为 null 时按旧类型池迁移）
        public int[] skillUses;

        // 死亡标志（旧档缺失时按 currentHP<=0 推导）
        public bool isDead;
    }

    /// <summary>存档槽摘要（主菜单读取存档列表用）</summary>
    [System.Serializable]
    public class SaveSlotInfo
    {
        public int slot;            // 存档编号 1~5
        public int floor;           // 存档所在层
        public string saveTime;     // 保存时间（可能为空 = 旧档）
    }

    /// <summary>
    /// 存档管理器（多槽：save_1.json ~ save_5.json，每槽覆盖式保存）
    /// </summary>
    public class DataManager : MonoBehaviour
    {
        public static DataManager Instance { get; private set; }

        public const int MaxSlots = 5;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
            DontDestroyOnLoad(gameObject);
        }

        public static string SavePath(int slot)
        {
            return Path.Combine(Application.persistentDataPath, $"save_{slot}.json");
        }

        /// <summary>保存到指定槽位（覆盖式）</summary>
        public void SaveGame(int slot)
        {
            if (slot < 1 || slot > MaxSlots) return;
            var gm = GameManager.Instance;
            if (gm == null) return;

            var save = new SaveData
            {
                saveVersion = 2,
                saveTime = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
                currentFloor = gm.playerData.currentFloor,
                bossDefeated = gm.playerData.bossDefeated,
                guideGifted = gm.playerData.guideGifted,
                gold = gm.playerData.gold,
                chipPokeBalls = gm.playerData.chipPokeBalls,
                normalPokeBalls = gm.playerData.normalPokeBalls,
                greatPokeBalls = gm.playerData.greatPokeBalls,
                ultraPokeBalls = gm.playerData.ultraPokeBalls,
                healBottles = gm.playerData.healBottles,
                reviveBottles = gm.playerData.reviveBottles,
                expBottles = gm.playerData.expBottles,
                skillBottles = gm.playerData.skillBottles,
                activeSpriteIndex = gm.playerData.activeSpriteIndex
            };

            foreach (var slotData in gm.playerData.pokeBallSlots)
            {
                int idx = (slotData != null && slotData.isLoaded && slotData.sprite != null)
                    ? gm.playerData.spriteBag.IndexOf(slotData.sprite) : -1;
                save.beltSlots.Add(idx);
            }

            foreach (var sprite in gm.playerData.spriteBag)
            {
                var entry = new SpriteSaveEntry
                {
                    spriteID = sprite.spriteID,
                    spriteName = sprite.spriteName,
                    level = sprite.level,
                    currentHP = sprite.currentHP,
                    maxHP = sprite.maxHP,
                    attack = sprite.attack,
                    strength = sprite.strength,
                    protect = sprite.protect,
                    exp = sprite.exp,
                    caughtWith = sprite.caughtWith,
                    g_hp = sprite.g_hp,
                    g_atk = sprite.g_atk,
                    speed = sprite.speed,
                    ultimateUses = sprite.ultimateUses,
                    attackUses = sprite.attackUses,
                    priorityUses = sprite.priorityUses,
                    supportUses = sprite.supportUses,
                    defenseUses = sprite.defenseUses,
                    healUses = sprite.healUses,
                    skillUses = sprite.skillUses != null ? (int[])sprite.skillUses.Clone() : null,
                    isDead = sprite.isDead
                };
                for (int i = 0; i < sprite.skills.Length; i++)
                {
                    entry.equippedSkills.Add(sprite.skills[i] != null ? i : -1);
                }
                save.sprites.Add(entry);
            }

            string json = JsonUtility.ToJson(save, true);
            File.WriteAllText(SavePath(slot), json);
            Debug.Log($"[DataManager] Game saved to {SavePath(slot)}");
        }

        /// <summary>自动保存：绑定存档槽时写入当前槽（进层/退出时调用）</summary>
        public void AutoSave()
        {
            var gm = GameManager.Instance;
            if (gm != null && gm.activeSaveSlot >= 1 && gm.activeSaveSlot <= MaxSlots)
                SaveGame(gm.activeSaveSlot);
        }

        public SaveData LoadSaveData(int slot)
        {
            string path = SavePath(slot);
            if (!File.Exists(path))
            {
                Debug.Log($"[DataManager] No save file found at {path}");
                return null;
            }

            string json = File.ReadAllText(path);
            return JsonUtility.FromJson<SaveData>(json);
        }

        /// <summary>读档：返回 false = 无档/旧版档已作废（调用方走新档流程）</summary>
        public bool LoadGame(int slot)
        {
            var save = LoadSaveData(slot);
            if (save == null) return false;

            // 版本校验：v2 之前的旧档（10~0 楼层语义）不兼容，作废重置
            if (save.saveVersion < 2)
            {
                Debug.LogWarning($"[DataManager] Old save version detected in slot {slot}, discarding.");
                DeleteSave(slot);
                return false;
            }

            var gm = GameManager.Instance;
            if (gm == null) return false;

            gm.playerData.currentFloor = save.currentFloor;
            gm.playerData.bossDefeated = save.bossDefeated;
            gm.playerData.guideGifted = save.guideGifted;
            gm.playerData.gold = save.gold;
            gm.playerData.chipPokeBalls = save.chipPokeBalls;
            gm.playerData.normalPokeBalls = save.normalPokeBalls;
            gm.playerData.greatPokeBalls = save.greatPokeBalls;
            gm.playerData.ultraPokeBalls = save.ultraPokeBalls;
            gm.playerData.healBottles = save.healBottles;
            gm.playerData.reviveBottles = save.reviveBottles;
            gm.playerData.expBottles = save.expBottles;
            gm.playerData.skillBottles = save.skillBottles;
            gm.playerData.activeSpriteIndex = save.activeSpriteIndex;

            gm.playerData.spriteBag.Clear();
            foreach (var entry in save.sprites)
            {
                var config = gm.allSpriteConfigs.Find(s => s.name == entry.spriteID);
                if (config != null)
                {
                    var sprite = new SpriteInstance(config, entry.level);
                    // 恢复个体波动（旧档 g=1 → 基准值）
                    sprite.g_hp = entry.g_hp > 0f ? entry.g_hp : 1f;
                    sprite.g_atk = entry.g_atk > 0f ? entry.g_atk : 1f;
                    sprite.speed = entry.speed > 0f ? entry.speed : (UnityEngine.Random.Range(config.speedMin, config.speedMax) + entry.level * 0.2f);
                    sprite.RecalcStats();
                    sprite.currentHP = entry.currentHP;
                    sprite.strength = entry.strength;
                    sprite.protect = entry.protect;
                    sprite.exp = entry.exp;
                    sprite.caughtWith = entry.caughtWith;
                    sprite.ultimateUses = entry.ultimateUses;
                    sprite.attackUses = entry.attackUses;
                    sprite.priorityUses = entry.priorityUses;
                    sprite.supportUses = entry.supportUses;
                    sprite.defenseUses = entry.defenseUses;
                    sprite.healUses = entry.healUses;

                    // 装备技能：新档按存档索引；旧档（空）迁移 = 前 3 槽
                    var pool = new[] { config.skill1, config.skill2, config.skill3, config.skill4, config.skill5 };
                    if (entry.equippedSkills != null && entry.equippedSkills.Count >= 3)
                    {
                        for (int i = 0; i < 3 && i < entry.equippedSkills.Count; i++)
                        {
                            int idx = entry.equippedSkills[i];
                            sprite.skills[i] = (idx >= 0 && idx < pool.Length) ? pool[idx] : null;
                        }
                    }
                    else
                    {
                        sprite.skills[0] = pool[0];
                        sprite.skills[1] = pool[1];
                        sprite.skills[2] = pool[2];
                    }

                    // 技能次数：新档按槽位恢复；旧档（字段缺失）按旧类型池迁移
                    if (entry.skillUses != null && entry.skillUses.Length >= 5)
                    {
                        sprite.skillUses = (int[])entry.skillUses.Clone();
                    }
                    else
                    {
                        for (int i = 0; i < 5; i++)
                            sprite.skillUses[i] = sprite.skills[i] != null ? sprite.GetUses(sprite.skills[i].skillType) : 0;
                    }

                    // 死亡标志：新档按存档；旧档（缺失）按 HP<=0 推导
                    sprite.isDead = entry.isDead || entry.currentHP <= 0f;

                    gm.playerData.spriteBag.Add(sprite);
                }
            }

            // 重建腰间 6 槽（按存档索引指向精灵背包）
            gm.playerData.pokeBallSlots.Clear();
            gm.playerData.EnsureBeltSlots(6);
            for (int i = 0; i < gm.playerData.pokeBallSlots.Count && i < save.beltSlots.Count; i++)
            {
                int idx = save.beltSlots[i];
                if (idx >= 0 && idx < gm.playerData.spriteBag.Count)
                {
                    gm.playerData.pokeBallSlots[i].isLoaded = true;
                    gm.playerData.pokeBallSlots[i].sprite = gm.playerData.spriteBag[idx];
                }
            }

            Debug.Log($"[DataManager] Game loaded from slot {slot}.");
            return true;
        }

        public void DeleteSave(int slot)
        {
            string path = SavePath(slot);
            if (File.Exists(path))
            {
                File.Delete(path);
                Debug.Log($"[DataManager] Save slot {slot} deleted.");
            }
        }

        public bool HasSaveFile(int slot)
        {
            return File.Exists(SavePath(slot));
        }

        /// <summary>列出所有非空槽摘要（主菜单读取存档列表用，按槽号升序）</summary>
        public List<SaveSlotInfo> GetSlotSummaries()
        {
            var list = new List<SaveSlotInfo>();
            for (int i = 1; i <= MaxSlots; i++)
            {
                if (!HasSaveFile(i)) continue;
                var save = LoadSaveData(i);
                if (save == null) continue;
                list.Add(new SaveSlotInfo
                {
                    slot = i,
                    floor = save.currentFloor,
                    saveTime = save.saveTime
                });
            }
            return list;
        }

        /// <summary>第一个空槽号；-1 = 槽位已满</summary>
        public int FirstEmptySlot()
        {
            for (int i = 1; i <= MaxSlots; i++)
                if (!HasSaveFile(i)) return i;
            return -1;
        }
    }
}
