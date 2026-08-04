using System.Collections.Generic;
using UnityEngine;

namespace LuluDungeon
{
    /// <summary>
    /// 构建预加载器：持有所有 Prefab/材质引用，防止 URP 构建裁剪
    /// </summary>
    public class Preloader : MonoBehaviour
    {
        [Header("怪物 Prefab（与 SpriteData 索引一一对应）")]
        public List<GameObject> monsterPrefabs = new List<GameObject>();
        public List<SpriteData> monsterSpriteDatas = new List<SpriteData>();

        [Header("NPC Prefab")]
        public GameObject guidePrefab;
        public GameObject merchantPrefab;
        public GameObject specterPrefab;        // Boss 持有者（Specter）模型
        public SpriteData stingRaySpriteData;   // 向导赠送的鳐鱼

        [Header("腰带球材质")]
        public Material ballMaterial;

        [Header("Idle 动画控制器")]
        public RuntimeAnimatorController idleController;

        /// <summary>
        /// 随机取一只怪物，返回索引（配合 monsterSpriteDatas 使用）
        /// </summary>
        public int GetRandomMonsterIndex()
        {
            return monsterPrefabs.Count > 0 ? Random.Range(0, monsterPrefabs.Count) : -1;
        }

        public GameObject GetMonsterPrefab(int index)
        {
            return (index >= 0 && index < monsterPrefabs.Count) ? monsterPrefabs[index] : null;
        }

        public SpriteData GetMonsterSpriteData(int index)
        {
            return (index >= 0 && index < monsterSpriteDatas.Count) ? monsterSpriteDatas[index] : null;
        }
    }
}
