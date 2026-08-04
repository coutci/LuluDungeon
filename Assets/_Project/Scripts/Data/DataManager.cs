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
        // 存档版本：v3 = 能量系统（取代次数池）+ 技能池索引；v2 旧档自动迁移
        public int saveVersion = 3;

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

        // 能量（v3；v2 旧档缺失时保持初始值 15 = 满能量）
        public int energy = SpriteInstance.MaxEnergy;

        // 装备的技能：技能池索引（-1 = 未装备；v2 旧档为槽位索引，迁移语义与前 3 技能一致）
        public List<int> equippedSkills = new List<int>();

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
                saveVersion = 3,
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
                    energy = sprite.energy,
                    isDead = sprite.isDead
                };
                // 技能池索引：spriteID → config → pool 定位技能身份（null 存 -1）
                var config = gm.allSpriteConfigs.Find(s => s.name == sprite.spriteID);
                var pool = config != null
                    ? new[] { config.skill1, config.skill2, config.skill3, config.skill4, config.skill5 }
                    : null;
                for (int i = 0; i < sprite.skills.Length; i++)
                {
                    int idx = -1;
                    if (sprite.skills[i] != null && pool != null)
                        idx = System.Array.IndexOf(pool, sprite.skills[i]);
                    entry.equippedSkills.Add(idx);
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

            // 版本校验：v3 = 能量系统（旧档自动迁移：能量满、技能按池索引恢复）；v2 之前的旧档（10~0 楼层语义）作废
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
                    sprite.energy = entry.energy;

                    // 装备技能：先清空 5 槽（构造器已填满池技能），再按存档的技能池索引恢复
                    // v2 旧档 equippedSkills 为槽位索引（0,1,2,-1,-1）→ 恢复为池前 3 技能（与迁移语义一致）
                    var pool = new[] { config.skill1, config.skill2, config.skill3, config.skill4, config.skill5 };
                    for (int i = 0; i < 5; i++) sprite.skills[i] = null;
                    if (entry.equippedSkills != null && entry.equippedSkills.Count >= 3)
                    {
                        for (int i = 0; i < 5; i++)
                        {
                            int idx = i < entry.equippedSkills.Count ? entry.equippedSkills[i] : -1;
                            sprite.skills[i] = (idx >= 0 && idx < pool.Length) ? pool[idx] : null;
                        }
                    }
                    else
                    {
                        sprite.skills[0] = pool[0];
                        sprite.skills[1] = pool[1];
                        sprite.skills[2] = pool[2];
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
