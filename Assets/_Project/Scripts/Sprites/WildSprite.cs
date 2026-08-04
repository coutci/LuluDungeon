using System.Collections.Generic;
using UnityEngine;

namespace LuluDungeon
{
    /// <summary>
    /// 野外精灵 - 挂载在地牢中游荡的精灵上
    /// </summary>
    public class WildSprite : MonoBehaviour
    {
        [Header("精灵配置")]
        public SpriteData spriteData;
        public int level = 5;
        public bool isBoss = false;

        [Header("行为")]
        public float wanderRadius = 3f;
        public float wanderSpeed = 1f;
        public float idleTime = 2f;

        private Vector3 _startPosition;
        private float _idleTimer;
        private Vector3 _targetPosition;
        private bool _wandering;

        private void Start()
        {
            _startPosition = transform.position;
            _idleTimer = Random.Range(0, idleTime);

            // 如果没有配置精灵数据，随机分配
            if (spriteData == null && GameManager.Instance != null)
            {
                var configs = GameManager.Instance.allSpriteConfigs;
                if (configs.Count > 0)
                    spriteData = configs[Random.Range(0, configs.Count)];
            }

            CreatePlaceholderModel();
        }

        private void Update()
        {
            WanderBehavior();
        }

        private void WanderBehavior()
        {
            _idleTimer -= Time.deltaTime;

            if (_idleTimer <= 0 && !_wandering)
            {
                _wandering = true;
                Vector3 randomOffset = Random.insideUnitSphere * wanderRadius;
                randomOffset.y = 0;
                _targetPosition = _startPosition + randomOffset;
            }

            if (_wandering)
            {
                transform.position = Vector3.MoveTowards(transform.position, _targetPosition,
                    wanderSpeed * Time.deltaTime);

                if (Vector3.Distance(transform.position, _targetPosition) < 0.1f)
                {
                    _wandering = false;
                    _idleTimer = idleTime + Random.Range(-1f, 1f);
                }
            }
        }

        /// <summary>
        /// 创建占位方块模型
        /// </summary>
        private void CreatePlaceholderModel()
        {
            if (spriteData == null) return;

            // 清除已有子对象
            foreach (Transform child in transform)
                Destroy(child.gameObject);

            // 创建简单的方块占位体
            GameObject body = GameObject.CreatePrimitive(PrimitiveType.Cube);
            body.transform.SetParent(transform);
            body.transform.localPosition = Vector3.zero;
            body.transform.localScale = Vector3.one * spriteData.modelScale;

            var renderer = body.GetComponent<MeshRenderer>();
            if (renderer != null)
            {
                // 创建简单材质
                Material mat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
                mat.color = spriteData.placeholderColor;
                renderer.material = mat;
            }

            // 添加名字标签（World Space）
            var labelObj = new GameObject("NameLabel");
            labelObj.transform.SetParent(transform);
            labelObj.transform.localPosition = Vector3.up * 1.5f;
            labelObj.AddComponent<Billboard>();

            var tmp = labelObj.AddComponent<TMPro.TextMeshPro>();
            tmp.text = $"{spriteData.spriteName} Lv.{level}";
            tmp.fontSize = 4;
            tmp.alignment = TMPro.TextAlignmentOptions.Center;
            tmp.color = Color.white;
        }

        /// <summary>
        /// 被精灵球击中时触发战斗
        /// </summary>
        public void TriggerBattle()
        {
            var bm = BattleManager.Instance;
            if (bm == null)
            {
                Debug.LogError("BattleManager not found in scene!");
                return;
            }

            // 创建敌方精灵实例
            var enemySprite = new SpriteInstance(spriteData, level);
            bm.StartBattle(enemySprite, isBoss);

            // 隐藏野外精灵
            gameObject.SetActive(false);
        }

        /// <summary>
        /// 被精灵球击中：弹出"进入战斗"提示（战斗系统暂未接入，占位）
        /// </summary>
        public void ShowEnterBattlePrompt()
        {
            BattlePrompt.Show(transform, "进入战斗！", 2.5f);

            // 短暂停顿，营造被击中的效果
            if (!_wandering)
                _idleTimer = Mathf.Max(_idleTimer, 1.5f);
        }
    }

    /// <summary>
    /// 野外精灵生成器
    /// </summary>
    public class WildSpriteSpawner : MonoBehaviour
    {
        public GameObject wildSpritePrefab;

        public void SpawnWildSprite(Transform spawnPoint)
        {
            if (wildSpritePrefab == null) return;

            GameObject obj = Instantiate(wildSpritePrefab, spawnPoint.position,
                Quaternion.identity, spawnPoint);
            var ws = obj.GetComponent<WildSprite>();
            if (ws == null) ws = obj.AddComponent<WildSprite>();

            // 根据当前层数决定等级
            var gm = GameManager.Instance;
            int floor = gm != null ? gm.playerData.currentFloor : 10;
            ws.level = 4 * (11 - floor) + Random.Range(0, 6);

            // 随机分配精灵类型
            if (gm != null && gm.allSpriteConfigs.Count > 0)
            {
                ws.spriteData = gm.allSpriteConfigs[Random.Range(0, gm.allSpriteConfigs.Count)];
            }
        }
    }
}
