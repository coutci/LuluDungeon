using System.Collections.Generic;
using UnityEngine;

namespace LuluDungeon
{
    /// <summary>
    /// Boss 精灵持有者（trainer）：Specter 模型站在牢房中，
    /// 投球命中本体触发 Boss 战；队伍按层配置规模、从怪物池随机抽取，
    /// 按顺序出场；每次挑战满状态；失败/逃跑由 BattleManager 回滚玩家状态；
    /// 整队击败后 Boss 消失并激活楼梯。
    /// </summary>
    public class BossTrainer : MonoBehaviour
    {
        [Header("层配置")]
        public int floor;                  // 所在层（由生成器设置）
        public bool defeated = false;

        [Header("展示")]
        public bool showLabel = true;
        public float labelHeight = 2.5f;
        public float labelScale = 0.04f;

        public bool IsDefeated => defeated;

        private GameObject _label;

        private void Start()
        {
            EventBus.Subscribe<BattleResult>(EventBus.ON_BATTLE_END, OnBattleEnded);
            if (showLabel) CreateLabel();
        }

        private void OnDestroy()
        {
            EventBus.Unsubscribe<BattleResult>(EventBus.ON_BATTLE_END, OnBattleEnded);
        }

        /// <summary>
        /// 队伍规模：0-2 层 2 只 / 3-5 层 3 只 / 6-7 层 4 只 / 8-9 层 5 只
        /// </summary>
        public static int TeamSizeForFloor(int floor)
        {
            return 2 + floor / 2;   // 0,1→2 / 2,3→3 / 4,5→4 / 6,7→5 / 8,9→6
        }

        /// <summary>从怪物池随机抽取队伍（队内不重复），等级按层加成</summary>
        public static List<SpriteInstance> BuildTeam(int floor)
        {
            var team = new List<SpriteInstance>();
            var preloader = FindFirstObjectByType<Preloader>();
            if (preloader == null) return team;

            var pool = new List<int>();
            for (int i = 0; i < preloader.monsterSpriteDatas.Count; i++)
                if (preloader.monsterSpriteDatas[i] != null) pool.Add(i);
            if (pool.Count == 0) return team;

            // Fisher-Yates 洗牌取前 size 只（队内不重复）
            for (int i = pool.Count - 1; i > 0; i--)
            {
                int j = Random.Range(0, i + 1);
                (pool[i], pool[j]) = (pool[j], pool[i]);
            }

            int size = Mathf.Min(TeamSizeForFloor(floor), pool.Count);
            int baseLvl = Monster.RandomBossLevelForFloor(floor);
            for (int i = 0; i < size; i++)
            {
                var sd = preloader.monsterSpriteDatas[pool[i]];
                var inst = new SpriteInstance(sd, baseLvl + Random.Range(0, 3));
                Spawner.AssignRandomSkills(inst);
                team.Add(inst);
            }
            return team;
        }

        /// <summary>投球命中触发：进入 Boss 战（队伍全新满状态）</summary>
        public void TriggerBattle(SpriteInstance playerSprite = null)
        {
            if (defeated) return;
            var team = BuildTeam(floor);
            if (team.Count == 0) return;
            var bm = BattleManager.Instance;
            if (bm != null) bm.StartBattle(team, true, null, playerSprite);
        }

        private void OnBattleEnded(BattleResult result)
        {
            if (!result.isBoss) return;   // 只响应 Boss 战
            if (result.playerWon && !defeated)
            {
                defeated = true;
                var gm = GameManager.Instance;
                if (gm != null) gm.OnBossDefeated(floor);
                StartCoroutine(PlayDeathAndDestroy());
            }
            // 失败/逃跑：Boss 保持满状态待挑战（队伍在下次 TriggerBattle 重新生成）
        }

        private void CreateLabel()
        {
            if (_label != null) return;
            _label = new GameObject("Label");
            _label.transform.SetParent(transform, false);
            _label.transform.localPosition = new Vector3(0, labelHeight, 0);

            var tm = _label.AddComponent<TextMesh>();
            tm.text = $"Boss Lv.{Monster.RandomBossLevelForFloor(floor)}";
            tm.fontSize = 80;
            tm.characterSize = labelScale;
            tm.color = Color.red;
            tm.anchor = TextAnchor.MiddleCenter;
            tm.alignment = TextAlignment.Center;

            var bb = _label.AddComponent<Billboard>();
            bb.flipDirection = true;   // TextMesh 从 -Z 面可读
            bb.lockYAxis = true;
        }

        /// <summary>击败后播放 Die 死亡动画再销毁（无动画组件则直接销毁）</summary>
        private System.Collections.IEnumerator PlayDeathAndDestroy()
        {
            var anim = GetComponentInChildren<Animator>();
            if (anim != null)
            {
                anim.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                // 暂停 NPCIdle 强制待机，避免覆盖死亡动画
                var idle = GetComponent<NPCIdle>();
                if (idle != null) idle.enabled = false;

                var driver = anim.gameObject.GetComponent<BattleAnimDriver>();
                if (driver == null) driver = anim.gameObject.AddComponent<BattleAnimDriver>();
                string clip = driver.ResolveClip("Die", "die");
                if (clip != null)
                {
                    yield return StartCoroutine(driver.PlayOnce(clip, false));
                    yield return new WaitForSeconds(0.3f);
                    Destroy(gameObject);
                    yield break;
                }
            }
            Destroy(gameObject);
        }

        /// <summary>给 Boss 模型根节点添加覆盖整体包围盒的 BoxCollider（供投球命中检测）</summary>
        public static void AddTrainerCollider(GameObject obj)
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
    }
}
