using System.Collections.Generic;
using UnityEngine;

namespace LuluDungeon
{
    public class Spawner : MonoBehaviour
    {
        public int min = 3, max = 6;
        public float areaMin = 3f, areaMax = 31f;

        void Start()
        {
            // 楼层内容已由 DungeonGenerator 统一生成，旧路径不再使用
            if (DungeonGenerator.Instance != null) return;

            var preloader = GetComponent<Preloader>();
            if (preloader == null) { Debug.LogError("[Spawner] Preloader missing!"); return; }
            var fg = transform.Find("Floor_00");
            if (fg == null) { Debug.LogError("[Spawner] Floor_00 missing!"); return; }

            // 当前层（等级公式依据）
            int floor = GameManager.Instance != null ? GameManager.Instance.playerData.currentFloor : 10;

            int n = Random.Range(min, max + 1);
            for (int i = 0; i < n; i++)
            {
                int idx = preloader.GetRandomMonsterIndex();
                var pf = preloader.GetMonsterPrefab(idx);
                if (pf == null) continue;

                var obj = Instantiate(pf, fg);
                obj.name = "Monster_" + pf.name + "_" + i;
                obj.transform.localPosition = new Vector3(Random.Range(areaMin, areaMax), 0.05f, Random.Range(areaMin, areaMax));

                // 数据初始化
                var monster = obj.AddComponent<Monster>();
                monster.spriteData = preloader.GetMonsterSpriteData(idx);
                monster.displayName = GetChineseName(CleanName(pf.name));  // 中文模型名
                if (monster.spriteData != null)
                {
                    monster.Init(floor);
                    AssignRandomSkills(monster.spriteInstance);
                }
                else
                {
                    Debug.LogWarning("[Spawner] No SpriteData for monster index " + idx);
                }

                obj.AddComponent<Wander>();

                // 模型自带无碰撞体，动态加一个覆盖整体包围盒的碰撞体（供精灵球命中检测）
                AddMonsterCollider(obj);
            }
        }

        /// <summary>
        /// 给怪物根节点添加 BoxCollider，尺寸 = 所有渲染器合并包围盒
        /// </summary>
        public static void AddMonsterCollider(GameObject obj)
        {
            var renderers = obj.GetComponentsInChildren<Renderer>();
            if (renderers.Length == 0) return;

            var bounds = renderers[0].bounds;
            for (int i = 1; i < renderers.Length; i++)
                bounds.Encapsulate(renderers[i].bounds);

            var col = obj.AddComponent<BoxCollider>();
            col.center = obj.transform.InverseTransformPoint(bounds.center);
            col.size = obj.transform.InverseTransformVector(bounds.size);
        }

        /// <summary>
        /// 清理模型名：去掉 PBRDefault / PBR 后缀
        /// </summary>
        public static string CleanName(string raw)
        {
            return raw.Replace("PBRDefault", "").Replace("PBR", "");
        }

        /// <summary>
        /// 模型名 → 中文名映射
        /// </summary>
        public static string GetChineseName(string modelName)
        {
            switch (modelName)
            {
                case "Dragon":        return "龙";
                case "BattleBee":     return "战蜂";
                case "TurtleShell":   return "龟甲兽";
                case "MonsterPlant":  return "食人花";
                case "Golem":         return "石魔像";
                case "Slime":         return "史莱姆";
                case "StingRay":     return "鳐鱼";
                case "Beholder":     return "眼魔";
                case "MushroomAngry": return "蘑菇怪";
                case "Cyclops":      return "独眼巨人";
                default:              return modelName; // 未知名称回退英文
            }
        }

        /// <summary>
        /// 为野怪随机分配 3 个技能（保证至少 1 个攻击技能）
        /// </summary>
        public static void AssignRandomSkills(LuluDungeon.SpriteInstance inst)
        {
            if (inst == null) return;
            var pool = new List<SkillData>();
            foreach (var s in inst.skills)
                if (s != null) pool.Add(s);
            if (pool.Count == 0) return;

            // Fisher-Yates 洗牌
            for (int i = pool.Count - 1; i > 0; i--)
            {
                int j = Random.Range(0, i + 1);
                var tmp = pool[i];
                pool[i] = pool[j];
                pool[j] = tmp;
            }

            // 至少 1 个攻击技能
            SkillData first = null;
            foreach (var s in pool)
            {
                if (s.IsAttackClass) { first = s; break; }
            }
            if (first == null) first = pool[0];

            var selected = new List<SkillData> { first };
            foreach (var s in pool)
            {
                if (selected.Count >= 3) break;
                if (!selected.Contains(s)) selected.Add(s);
            }

            inst.skills[0] = selected.Count > 0 ? selected[0] : null;
            inst.skills[1] = selected.Count > 1 ? selected[1] : null;
            inst.skills[2] = selected.Count > 2 ? selected[2] : null;
            inst.skills[3] = null;
            inst.skills[4] = null;

        }
    }
}
