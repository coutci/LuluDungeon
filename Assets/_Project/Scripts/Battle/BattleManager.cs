using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using Unity.XR.CoreUtils;

namespace LuluDungeon
{
    /// <summary>
    /// 战斗状态枚举
    /// </summary>
    public enum BattleState
    {
        Intro,          // 入场过渡
        PlayerTurn,     // 玩家锁定行动（同步回合：玩家选完即双方同时结算）
        Resolving,      // 结算中（动画/消息播放）
        Catching,       // 精灵球结算
        Fleeing,        // 逃跑（敌方必定先行动一次）
        ForcedSwitch,   // 阵亡强制切换
        Victory,        // 胜利
        Defeat,         // 失败
        Escaped         // 逃跑成功
    }

    /// <summary>
    /// 玩家锁定行动类型
    /// </summary>
    public enum PlayerActionType
    {
        None,
        Skill,          // 技能
        Ball,           // 精灵球
        HealBottle,     // 治疗药水
        SkillBottle,    // 技能药水
        Switch,         // 切换精灵
        Escape,         // 逃跑
        BasicAttack     // 兜底基础攻击（×0.7 无限）
    }

    /// <summary>
    /// 敌方决策
    /// </summary>
    public class EnemyDecision
    {
        public SkillData skill;         // 使用的技能（null = 发呆/基础攻击）
        public bool basicAttack;        // 兜底基础攻击（×0.7，无附加）
    }

    /// <summary>
    /// 战斗管理器 - 同步回合制状态机
    /// 双方同时锁定行动 → 防御/先制/行动值排序 → 结算
    /// </summary>
    public class BattleManager : MonoBehaviour
    {
        public static BattleManager Instance { get; private set; }

        [Header("战斗配置")]
        public float turnInterval = 1.8f;

        [Header("战场")]
        public Transform playerSpritePosition;
        public Transform enemySpritePosition;
        public GameObject battleArena;

        [Header("当前战斗状态")]
        [SerializeField] private BattleState _state = BattleState.Intro;
        [SerializeField] private SpriteInstance _playerSprite;
[SerializeField] private SpriteInstance _enemySprite;   // 当前出场的敌方精灵

        [Header("敌方队伍（Boss 战多精灵，按顺序出场）")]
        [SerializeField] private List<SpriteInstance> _enemyTeam = new List<SpriteInstance>();
        [SerializeField] private int _enemyIndex;               // 当前出场队伍下标
        private BossTrainer _bossTrainer;                       // Boss 战来源（胜利/失败通知用）prite;

        [SerializeField] private bool _isBossBattle;
        [SerializeField] private bool _canCatch = true;

        // 玩家锁定
        private PlayerActionType _playerAction = PlayerActionType.None;
        private int _playerSkillIndex;
        private PokeBallQuality _ballQuality;
        private int _switchIndex;
        private bool _playerDefending;      // 玩家本回合是否防御
        private bool _enemyDefending;       // 敌方本回合是否防御

        // 事件
        public System.Action<BattleState> OnStateChanged;
        public System.Action<string> OnBattleMessage;

        [Header("玩家传送")]
        public XROrigin xrOrigin;                 // XR Origin（留空自动查找）
        public Vector3 spectatorOffset = new Vector3(0f, 0.05f, 6f);  // 观战位相对竞技场偏移（y=地板顶面）

        private GameObject _playerModel;
        private GameObject _enemyModel;
        private Coroutine _turnCoroutine;
        private Monster _sourceMonster;     // 战斗来源怪物（战后清理）
        private Vector3 _playerOriginPos;
        private Quaternion _playerOriginRot;
        private Vector3? _battleAnchor;      // 战斗期间每帧强制的 origin 位置（防追踪漂移）
        private Coroutine _originLockCoroutine; // 战斗结束回原位后的短暂锁定协程
        private Vector3 _monsterOriginPos;
        private bool _hasMonsterOrigin;

        private const float BasicAttackMultiplier = 1.0f;   // 兜底基础攻击（v5.2：0.7 → 1.0）

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
            if (xrOrigin == null)
                xrOrigin = FindFirstObjectByType<XROrigin>();
            if (xrOrigin == null)
                Debug.LogError("[Battle] XR Origin not found — 战斗传送功能将不可用！");
        }

        /// <summary>
        /// 开始战斗
        /// </summary>
public void StartBattle(SpriteInstance enemy, bool isBoss = false, Monster sourceMonster = null, SpriteInstance playerSprite = null)
        {
            StartBattle(new List<SpriteInstance> { enemy }, isBoss, sourceMonster, playerSprite);
        }

                /// <summary>敌方队伍版：Boss 战多精灵按顺序出场；普通战队伍仅 1 只</summary>
        public void StartBattle(List<SpriteInstance> enemyTeam, bool isBoss, Monster sourceMonster, SpriteInstance playerSprite)
        {
            var gm = GameManager.Instance;
            if (gm == null) return;
            if (enemyTeam == null || enemyTeam.Count == 0 || enemyTeam[0] == null) return;

            // 出战精灵：优先投掷球携带的精灵，否则腰间第一个装载精灵
            _playerSprite = playerSprite != null ? playerSprite : FirstBeltSprite();
            if (_playerSprite == null) return;
            int activeIdx = gm.playerData.spriteBag.IndexOf(_playerSprite);
            if (activeIdx >= 0) gm.playerData.activeSpriteIndex = activeIdx;

            _enemyTeam = enemyTeam;
            _enemyIndex = 0;
            _enemySprite = _enemyTeam[0];
            _isBossBattle = isBoss;
            _canCatch = !isBoss;
            _sourceMonster = sourceMonster;

            // Boss 挑战制：进入战斗前快照玩家完整状态（失败/逃跑整体回滚）
            if (isBoss) gm.TakeBattleSnapshot();

            gm.SetBattleState(true);

            // 重置战斗态系数（增益/减益在战斗开始时清零）
            ResetBattleModifiers(_playerSprite);
            ResetBattleModifiers(_enemySprite);

            _playerAction = PlayerActionType.None;
            _playerDefending = false;
            _enemyDefending = false;

            // 传送：玩家 → 竞技场观战位；怪物 → 敌方站位
            if (xrOrigin == null)
            {
                var xro = FindFirstObjectByType<XROrigin>();
                if (xro != null) xrOrigin = xro;
            }
            if (xrOrigin != null)
            {
                // 记录玩家锚点（origin = 脚底位置），相机高度由设备追踪提供
                _playerOriginPos = xrOrigin.transform.position;
                _playerOriginRot = xrOrigin.transform.rotation;
                Vector3 target = (battleArena != null ? battleArena.transform.position : Vector3.zero) + spectatorOffset;
                xrOrigin.transform.position = target;
                _battleAnchor = target;
            }
            if (_sourceMonster != null)
            {
                _monsterOriginPos = _sourceMonster.transform.position;
                _hasMonsterOrigin = true;
            }

            StartCoroutine(BattleIntroSequence());
            // 战斗 BGM
            if (AudioManager.Instance != null) AudioManager.Instance.PlayBgm(BgmType.Battle);
        }

        /// <summary>重置精灵战斗态系数。clearHazards=false（敌方换人）时保留沙暴（场地效果跨换人）；速度减益跟精灵走，换人即清</summary>
        private void ResetBattleModifiers(SpriteInstance s, bool clearHazards = true)
        {
            if (s == null) return;
            s.strengthLayers.Clear();
            s.protectLayers.Clear();
            s.burnTicks.Clear();
            s.poisonTicks.Clear();
            if (clearHazards) s.sandTicks.Clear();
            s.paralyzeTicks.Clear();
            s.leechSeedTurns = 0;
            s.leechSeedOwner = null;
            s.confusionActive = false;
            s.speedLevel = 0;
            s.speedLevelTurns = 0;
            s.speedDebuffLevel = 0;
            s.RecalcMods();
        }

        private IEnumerator BattleIntroSequence()
        {
            SetState(BattleState.Intro);
            yield return new WaitForSeconds(0.5f);

            SpawnBattleModels();
            EventBus.Publish(EventBus.ON_BATTLE_START);

            yield return new WaitForSeconds(1f);
            SetState(BattleState.PlayerTurn);
        }

        /// <summary>
        /// 生成双方模型：优先资源包真实模型（按 Preloader 映射），无匹配用占位体
        /// </summary>
        private void SpawnBattleModels()
        {
            ClearBattleModels();
            // 敌方：优先用怪物本体（已传送至敌方站位），否则实例化副本
            if (_sourceMonster != null)

            {
                if (enemySpritePosition != null)
                    _sourceMonster.transform.position = enemySpritePosition.position;
                _enemyModel = _sourceMonster.gameObject;
                PlayBattleIdle(_sourceMonster.gameObject);
            }
            else
            {
                _enemyModel = SpawnModelFor(_enemySprite, enemySpritePosition);
                if (_enemyModel != null) PlayBattleIdle(_enemyModel);
            }

            _playerModel = SpawnModelFor(_playerSprite, playerSpritePosition);
            if (_playerModel != null)
            {
                PlayBattleIdle(_playerModel);
            }

            // 双方精灵面对面
            FaceOpponent(_playerModel, playerSpritePosition, enemySpritePosition);
            FaceOpponent(_enemyModel, enemySpritePosition, playerSpritePosition);
        }

        /// <summary>
        /// 让模型面向对方站位
        /// </summary>
        private static void FaceOpponent(GameObject model, Transform fromPos, Transform toPos)
        {
            if (model == null || fromPos == null || toPos == null) return;
            Vector3 dir = toPos.position - fromPos.position;
            dir.y = 0f;
            if (dir.sqrMagnitude < 0.001f) return;
            model.transform.rotation = Quaternion.LookRotation(dir.normalized);
        }

        /// <summary>
        /// 播放战斗待机动画：用 BattleAnimDriver 每帧覆盖演示状态机的自动过渡
        /// </summary>
        private static void PlayBattleIdle(GameObject go)
        {
            if (go == null) return;
            var anim = go.GetComponentInChildren<Animator>();
            if (anim == null || anim.runtimeAnimatorController == null) return;
            anim.cullingMode = AnimatorCullingMode.AlwaysAnimate;   // 战斗期间动画常开（防视野外冻结）
            string target = null;
            foreach (var c in anim.runtimeAnimatorController.animationClips)
            {
                string n = c.name.ToLower();
                if (n.Contains("idle") && n.Contains("battle") && !n.Contains("normal")) { target = c.name; break; }
            }
            if (target == null)
            {
                foreach (var c in anim.runtimeAnimatorController.animationClips)
                    if (c.name.ToLower().Contains("idle")) { target = c.name; break; }
            }
            if (target == null && anim.runtimeAnimatorController.animationClips.Length > 0)
                target = anim.runtimeAnimatorController.animationClips[0].name;
            var driver = anim.gameObject.GetComponent<BattleAnimDriver>();
            if (driver == null) driver = anim.gameObject.AddComponent<BattleAnimDriver>();
            driver.SetClip(target != null ? target : "");
            driver.SetActive(true);
        }

        /// <summary>精灵 → 当前战斗模型映射</summary>
        private GameObject ModelOf(SpriteInstance sprite)
        {
            if (sprite == null) return null;
            if (_playerSprite != null && sprite == _playerSprite) return _playerModel;
            if (_enemySprite != null && sprite == _enemySprite) return _enemyModel;
            return null;
        }

        /// <summary>确保模型 Animator 上有 BattleAnimDriver（无 Animator 返回 null，占位体安全跳过）</summary>
        private static BattleAnimDriver GetAnimDriver(GameObject go)
        {
            if (go == null) return null;
            var anim = go.GetComponentInChildren<Animator>();
            if (anim == null || anim.runtimeAnimatorController == null) return null;
            var driver = anim.gameObject.GetComponent<BattleAnimDriver>();
            if (driver == null) driver = anim.gameObject.AddComponent<BattleAnimDriver>();
            return driver;
        }

        /// <summary>播放攻击动画（随机 Attack01/02），播完自动回到待机，不阻塞结算流程</summary>
        private void PlayAttackAnimation(SpriteInstance attacker)
        {
            var driver = GetAnimDriver(ModelOf(attacker));
            if (driver == null) return;
            string clip = Random.value < 0.5f
                ? driver.ResolveClip("Attack01", "attack")
                : driver.ResolveClip("Attack02", "attack");
            if (clip == null) clip = driver.ResolveClip("Attack01", "attack");
            if (clip != null) StartCoroutine(driver.PlayOnce(clip, true));
        }

        /// <summary>播放受击动画（GetHit），不阻塞结算流程</summary>
        private void PlayHitAnimation(SpriteInstance defender)
        {
            var driver = GetAnimDriver(ModelOf(defender));
            if (driver == null) return;
            string clip = driver.ResolveClip("GetHit", "gethit");
            if (clip != null) StartCoroutine(driver.PlayOnce(clip, true));
        }

        // ==================== 技能特效 ====================

        /// <summary>
        /// 播放技能特效（Resources/Effects 下的 prefab）。
        /// effectName 格式："施法特效;命中特效1,命中特效2"（任一段可为空）。
        /// isCast=true → 解析分号前（挂在攻击方，施法蓄力）；false → 解析分号后（挂在防守方，命中爆发）。
        /// 播完自动销毁，带超时保护防止 looping 粒子卡住战斗演出。
        /// </summary>
        private void PlaySkillEffect(GameObject anchor, string effectName, bool isCast)
        {
            if (anchor == null || string.IsNullOrEmpty(effectName)) return;
            string part = isCast ? CastEffectOf(effectName) : HitEffectsOf(effectName);
            if (string.IsNullOrEmpty(part)) return;
            foreach (string raw in part.Split(','))
            {
                string name = raw.Trim();
                if (name.Length == 0) continue;
                var prefab = Resources.Load<GameObject>("Effects/" + name);
                if (prefab == null)
                {
                    Debug.LogWarning($"[Battle] 技能特效不存在: Effects/{name}");
                    continue;
                }
                var fx = Instantiate(prefab, anchor.transform.position + Vector3.up * EffectHeight(name, isCast), Quaternion.identity);
                StartCoroutine(EffectLifecycle(fx, isCast ? 1.6f : 4f));
            }
        }

        /// <summary>取 effectName 分号前的施法特效段（无分号返回空）</summary>
        private static string CastEffectOf(string effectName)
        {
            int idx = effectName.IndexOf(';');
            return idx < 0 ? "" : effectName.Substring(0, idx);
        }

                /// <summary>
        /// 特效挂点高度：施法环绕在模型胸口（+0.9）；地面圈类（Area_generic/top_down）贴地（0）；其余命中爆发在身体（+0.8）
        /// </summary>
        private static float EffectHeight(string effectName, bool isCast)
        {
            if (isCast) return 0.9f;
            if (effectName.Contains("Area_generic") || effectName.Contains("top_down")) return 0f;
            return 0.8f;
        }

        /// <summary>取 effectName 分号后的命中特效段（无分号则整串视为命中特效）</summary>
/// <summary>取 effectName 分号后的命中特效段（无分号则整串视为命中特效）</summary>
        private static string HitEffectsOf(string effectName)
        {
            int idx = effectName.IndexOf(';');
            return idx < 0 ? effectName : effectName.Substring(idx + 1);
        }

        /// <summary>特效生命周期：等所有粒子自然结束（超时强制销毁），不阻塞战斗流程</summary>
        private IEnumerator EffectLifecycle(GameObject fx, float timeout)
        {
            float t = 0f;
            while (t < timeout)
            {
                bool alive = false;
                foreach (var ps in fx.GetComponentsInChildren<ParticleSystem>(true))
                {
                    if (ps.IsAlive(true)) { alive = true; break; }
                }
                if (!alive) break;
                t += Time.deltaTime;
                yield return null;
            }
            if (fx != null) Destroy(fx);
        }

        /// <summary>播放死亡动画并等待播完（保持死亡姿势，不回到待机）</summary>
        private IEnumerator PlayDeathAnimation(SpriteInstance sprite)
        {
            var driver = GetAnimDriver(ModelOf(sprite));
            if (driver == null) yield break;
            string clip = driver.ResolveClip("Die", "die");
            if (clip == null) yield break;
            yield return StartCoroutine(driver.PlayOnce(clip, false));
        }

        private GameObject SpawnModelFor(SpriteInstance sprite, Transform pos)
        {
            if (sprite == null || pos == null) return null;

            // 尝试从 Preloader 映射找真实模型
            var preloader = FindFirstObjectByType<Preloader>();
            if (preloader != null)
            {
                for (int i = 0; i < preloader.monsterSpriteDatas.Count && i < preloader.monsterPrefabs.Count; i++)
                {
                    if (preloader.monsterSpriteDatas[i] != null && preloader.monsterPrefabs[i] != null
                        && preloader.monsterSpriteDatas[i].name == sprite.spriteID)
                    {
                        var go = Instantiate(preloader.monsterPrefabs[i], pos.position, pos.rotation);
                        go.transform.localScale = Vector3.one;
                        // 战斗模型强制动画常开：视野外 cull 会导致死亡/待机动画冻结
                        var a = go.GetComponentInChildren<Animator>();
                        if (a != null) a.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                        return go;
                    }
                }
            }

            // 占位体：Cube + 颜色
            var cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
            cube.transform.SetParent(pos, false);
            cube.transform.localPosition = Vector3.zero;
            cube.transform.localScale = Vector3.one * 1.2f;
            var sr = cube.GetComponent<Renderer>();
            var gm = GameManager.Instance;
            var config = sprite != null && gm != null ? gm.allSpriteConfigs.Find(c => c.name == sprite.spriteID) : null;
            if (config != null && sr != null) sr.material.color = config.placeholderColor;
            else if (sr != null) sr.material.color = Color.gray;
            return cube;
        }

        private void ClearBattleModels()
        {
            if (_playerModel != null) Destroy(_playerModel);
            // 怪物本体不在此销毁（由 OnBattleEnded 决定去留）
            if (_enemyModel != null && (_sourceMonster == null || _enemyModel != _sourceMonster.gameObject))
                Destroy(_enemyModel);
            _playerModel = null;
            _enemyModel = null;
        }

        // ==================== 玩家锁定行动 ====================

        /// <summary>锁定技能（技能下标 0-2 = 装备的 3 个）</summary>
        public void LockPlayerSkill(int skillIndex)
        {
            if (_state != BattleState.PlayerTurn) return;
            var skill = GetEquippedSkill(skillIndex);

            // 能量不足 → 攻击类自动降级为基础攻击（不耗能量）；非攻击类给出提示
            if (skill == null || !_playerSprite.CanUseSkill(skill))
            {
                if (skill != null && skill.IsAttackClass)
                {
                    _playerAction = PlayerActionType.BasicAttack;
                    StartResolve();
                }
                else if (skill != null)
                {
                    OnBattleMessage?.Invoke($"{_playerSprite.spriteName} 能量不足，无法使用 {skill.skillName}！");
                }
                return;
            }

            _playerAction = PlayerActionType.Skill;
            _playerSkillIndex = skillIndex;
            _playerSprite.SpendEnergy(skill);
            StartResolve();
        }

        /// <summary>锁定精灵球（必定先手）</summary>
        public void LockPlayerBall(PokeBallQuality quality)
        {
            if (_state != BattleState.PlayerTurn || !_canCatch) return;
            if (!GameManager.Instance.HasItem($"{quality} PokeBall")) return;

            GameManager.Instance.UseItem($"{quality} PokeBall");
            _playerAction = PlayerActionType.Ball;
            _ballQuality = quality;
            StartResolve();
        }

        /// <summary>锁定治疗药水</summary>
        public void LockPlayerHealBottle()
        {
            if (_state != BattleState.PlayerTurn) return;
            if (!GameManager.Instance.HasItem("Heal Bottle")) return;

            GameManager.Instance.UseItem("Heal Bottle");
            _playerAction = PlayerActionType.HealBottle;
            StartResolve();
        }

        /// <summary>锁定能量药剂（恢复出战精灵 10 点能量）</summary>
        public void LockPlayerSkillBottle()
        {
            if (_state != BattleState.PlayerTurn) return;
            if (!GameManager.Instance.HasItem("Energy Bottle")) return;

            GameManager.Instance.UseItem("Energy Bottle");
            _playerAction = PlayerActionType.SkillBottle;
            StartResolve();
        }

        /// <summary>锁定切换精灵（消耗本回合行动）</summary>
        public void LockPlayerSwitch(int slotIndex)
        {
            if (_state != BattleState.PlayerTurn)
            {
                OnBattleMessage?.Invoke("现在不是切换的时机！");
                return;
            }
            var target = GetBeltSprite(slotIndex);
            if (target == null || !target.IsAlive)
            {
                OnBattleMessage?.Invoke("该精灵无法出战！");
                return;
            }
            if (target == _playerSprite)
            {
                OnBattleMessage?.Invoke($"{target.spriteName} 已经在场上了！");
                return;
            }

            _playerAction = PlayerActionType.Switch;
            _switchIndex = slotIndex;
            StartResolve();
        }

        /// <summary>锁定逃跑（敌方必定先行动一次）</summary>
        public void LockPlayerEscape()
        {
            if (_state != BattleState.PlayerTurn || _isBossBattle) return;
            _playerAction = PlayerActionType.Escape;
            StartResolve();
        }

        // ==================== 同步结算 ====================

        private void StartResolve()
        {
            if (_turnCoroutine != null) StopCoroutine(_turnCoroutine);
            _turnCoroutine = StartCoroutine(ResolveTurn());
        }

        private IEnumerator ResolveTurn()
        {
            SetState(BattleState.Resolving);
            yield return new WaitForSeconds(0.3f);

            // 0. 回合初状态结算（灼烧/中毒/沙暴/种子真伤 + 灼烧减伤标记 + 麻痹）
            yield return StartCoroutine(TickStatuses());
            if (IsBattleOver()) yield break;

            // 1. 敌方 AI 决策
            var enemyDec = EnemyAIDecide();

            // 2. 麻痹判定：被麻痹 → 发呆（不耗次数）
            bool playerParalyzed = _playerSprite.IsParalyzedThisTurn();
            bool enemyParalyzed = _enemySprite.IsParalyzedThisTurn();

            if (playerParalyzed)
            {
                _playerAction = PlayerActionType.None;
                OnBattleMessage?.Invoke($"{_playerSprite.spriteName} 麻痹了，无法行动！");
            }
            if (enemyParalyzed)
            {
                enemyDec = null;
                OnBattleMessage?.Invoke($"{_enemySprite.spriteName} 麻痹了，无法行动！");
            }

            // 3. 防御标记（先于攻击结算）
            _playerDefending = IsDefenseAction(_playerAction, _playerSkillIndex);
            _enemyDefending = enemyDec != null && enemyDec.skill != null && enemyDec.skill.skillType == SkillType.Defense;

            // 4. 精灵球必定先手
            if (_playerAction == PlayerActionType.Ball)
            {
                yield return StartCoroutine(ExecutePlayerBall());
                if (_state == BattleState.Victory) yield break;
            }
            else if (_playerAction == PlayerActionType.Escape)
            {
                // 逃跑：与敌方同一轮结算，敌方必定先行动一次（ExecuteEscape 内处理），逃跑必定后手
                yield return StartCoroutine(ExecuteEscape());
                if (IsBattleOver() || _state == BattleState.ForcedSwitch) yield break;
            }
            else
            {
                // 5. 防御类 → 先制类 → 其余按行动值
                bool playerPriority = _playerAction == PlayerActionType.Skill && GetEquippedSkill(_playerSkillIndex) != null
                                      && GetEquippedSkill(_playerSkillIndex).skillType == SkillType.Priority;
                bool enemyPriority = enemyDec != null && enemyDec.skill != null && enemyDec.skill.skillType == SkillType.Priority;

                bool playerFirst = CompareFirstMove(playerPriority, enemyPriority);
                yield return StartCoroutine(ExecuteSide(playerFirst, enemyDec));
                if (_state == BattleState.Victory || _state == BattleState.Defeat) yield break;
            }

            // 6. 回合末结算（状态伤害 + 持续-1）
            yield return new WaitForSeconds(0.4f);
            yield return StartCoroutine(TurnEnd());
        }

        private bool IsDefenseAction(PlayerActionType action, int skillIndex)
        {
            if (action != PlayerActionType.Skill) return false;
            var s = GetEquippedSkill(skillIndex);
            return s != null && s.skillType == SkillType.Defense;
        }

        /// <summary>
        /// 先手判定：先制 > 普通（按行动值±15%）
        /// </summary>
        private bool CompareFirstMove(bool playerPriority, bool enemyPriority)
        {
            if (playerPriority && !enemyPriority) return true;
            if (!playerPriority && enemyPriority) return false;
            // 同优先级：行动值（含速度层）±10% 扰动
            float pv = (_playerSprite.speed + _playerSprite.speedLevel + _playerSprite.speedDebuffLevel) * Random.Range(0.9f, 1.1f);
            float ev = (_enemySprite.speed + _enemySprite.speedLevel + _enemySprite.speedDebuffLevel) * Random.Range(0.9f, 1.1f);
            if (Mathf.Abs(pv - ev) < 0.001f) return Random.value < 0.5f;
            return pv > ev;
        }

        /// <summary>
        /// 执行双方行动（先手方 → 后手方，每步后检查阵亡）
        /// </summary>
        private IEnumerator ExecuteSide(bool playerSide, EnemyDecision enemyDec)
        {
            if (playerSide)
            {
                yield return StartCoroutine(ExecutePlayerAction());
                if (IsBattleOver()) yield break;
                if (_enemySprite != null && !_enemySprite.IsAlive) { yield return StartCoroutine(HandleEnemyFaint()); yield break; }
                if (_playerSprite != null && !_playerSprite.IsAlive)
                {
                    yield return StartCoroutine(HandlePlayerFaint());
                    if (_state == BattleState.Defeat || _state == BattleState.ForcedSwitch) yield break;
                }
                yield return StartCoroutine(ExecuteEnemyAction(enemyDec));
                if (_playerSprite != null && !_playerSprite.IsAlive)
                {
                    yield return StartCoroutine(HandlePlayerFaint());
                }
                if (IsBattleOver()) yield break;
            }
            else
            {
                yield return StartCoroutine(ExecuteEnemyAction(enemyDec));
                if (IsBattleOver()) yield break;
                if (_playerSprite != null && !_playerSprite.IsAlive)
                {
                    yield return StartCoroutine(HandlePlayerFaint());
                    if (_state == BattleState.Defeat || _state == BattleState.ForcedSwitch) yield break;
                }
                yield return StartCoroutine(ExecutePlayerAction());
                if (IsBattleOver()) yield break;
                if (_enemySprite != null && !_enemySprite.IsAlive) { yield return StartCoroutine(HandleEnemyFaint()); }
            }
        }

        private IEnumerator ExecutePlayerAction()
        {
            switch (_playerAction)
            {
                case PlayerActionType.Skill:
                    yield return StartCoroutine(ExecuteSkill(_playerSprite, _enemySprite, GetEquippedSkill(_playerSkillIndex), _enemyDefending));
                    break;
                case PlayerActionType.HealBottle:
                    OnBattleMessage?.Invoke($"{_playerSprite.spriteName} 使用了治疗药水！");
                    yield return new WaitForSeconds(0.8f);
                    float heal = _playerSprite.maxHP * 0.5f;
                    _playerSprite.currentHP = Mathf.Min(_playerSprite.maxHP, _playerSprite.currentHP + heal);
                    OnBattleMessage?.Invoke($"回复了 {heal:F0} 点 HP！");
                    break;
                case PlayerActionType.SkillBottle:
                    OnBattleMessage?.Invoke($"{_playerSprite.spriteName} 使用了能量药剂！");
                    yield return new WaitForSeconds(0.8f);
                    _playerSprite.AddEnergy(10);
                    OnBattleMessage?.Invoke($"{_playerSprite.spriteName} 恢复了 10 点能量！");
                    break;
                case PlayerActionType.Switch:
                    DoSwitch(_switchIndex);
                    yield return new WaitForSeconds(0.8f);
                    break;
                case PlayerActionType.Escape:
                    yield return StartCoroutine(ExecuteEscape());
                    break;
                case PlayerActionType.BasicAttack:
                    OnBattleMessage?.Invoke($"{_playerSprite.spriteName} 使用了基础攻击！");
                    PlayAttackAnimation(_playerSprite);
                    yield return new WaitForSeconds(0.8f);
                    {
                        float dmg = ComputeDamage(_playerSprite, _enemySprite, BasicAttackMultiplier, false, _enemyDefending);
                        _enemySprite.currentHP = Mathf.Max(0f, _enemySprite.currentHP - dmg);
                        _enemySprite.SyncDeathFlag();
                        if (dmg > 0f) PlayHitAnimation(_enemySprite);
                        OnBattleMessage?.Invoke($"造成了 {dmg:F0} 点伤害！");
                        if (!_enemySprite.IsAlive)
                            OnBattleMessage?.Invoke($"{_enemySprite.spriteName} 倒下了！");
                    }
                    break;
            }
            _playerAction = PlayerActionType.None;
        }

        private IEnumerator ExecuteEnemyAction(EnemyDecision dec)
        {
            if (dec == null || (dec.skill == null && !dec.basicAttack))
            {
                OnBattleMessage?.Invoke($"{_enemySprite.spriteName} 发呆了。");
                yield return new WaitForSeconds(0.6f);
                yield break;
            }

            if (dec.basicAttack)
            {
                OnBattleMessage?.Invoke($"{_enemySprite.spriteName} 使用了基础攻击！");
                PlayAttackAnimation(_enemySprite);
                yield return new WaitForSeconds(0.8f);
                float dmg = ComputeDamage(_enemySprite, _playerSprite, BasicAttackMultiplier, false, _playerDefending);
                ApplyDamageToPlayer(dmg);
            }
            else
            {
                yield return StartCoroutine(ExecuteSkill(_enemySprite, _playerSprite, dec.skill, _playerDefending));
            }
        }

        // ==================== 逃跑 ====================

        private IEnumerator ExecuteEscape()
        {
            SetState(BattleState.Fleeing);
            OnBattleMessage?.Invoke($"{_playerSprite.spriteName} 试图逃跑…");
            yield return new WaitForSeconds(0.5f);

            // 敌方必定先行动一次
            var dec = EnemyAIDecide();
            _enemyDefending = dec != null && dec.skill != null && dec.skill.skillType == SkillType.Defense;

            if (dec != null && dec.skill != null && dec.skill.IsAttackClass)
            {
                yield return StartCoroutine(ExecuteSkill(_enemySprite, _playerSprite, dec.skill, false));
            }
            else if (dec != null && dec.basicAttack)
            {
                OnBattleMessage?.Invoke($"{_enemySprite.spriteName} 使用了基础攻击！");
                PlayAttackAnimation(_enemySprite);
                yield return new WaitForSeconds(0.8f);
                float dmg = ComputeDamage(_enemySprite, _playerSprite, BasicAttackMultiplier, false, false);
                ApplyDamageToPlayer(dmg);
            }
            else
            {
                OnBattleMessage?.Invoke($"{_enemySprite.spriteName} 没有攻击！");
                yield return new WaitForSeconds(0.6f);
            }

            if (!_playerSprite.IsAlive)
            {
                // 挨打致死 → 逃跑失败，强制切换
                yield return StartCoroutine(HandlePlayerFaint());
                yield break;
            }

            // 逃跑成功：无惩罚
            OnBattleMessage?.Invoke("逃跑成功！");
            EventBus.Publish(EventBus.ON_BATTLE_END, new BattleResult { playerWon = false, escaped = true });
            EventBus.Publish(EventBus.ON_BATTLE_END);
            SetState(BattleState.Escaped);
            FinishBattle();
        }

        // ==================== 技能执行 ====================

        private IEnumerator ExecuteSkill(SpriteInstance attacker, SpriteInstance defender, SkillData skill, bool defenderDefending)
        {
            if (skill == null) yield break;

            // 混乱：行动时 30% 攻击自己（×0.8，不吃克制，吃自身 protect）
            if (attacker.confusionActive && Random.value < 0.3f)
            {
                OnBattleMessage?.Invoke($"{attacker.spriteName} 混乱了，攻击了自己！");
                float selfDmg = attacker.attack * 0.8f / Mathf.Max(0.5f, attacker.protect);
                if (IsDefensiveRole(attacker)) selfDmg *= 0.92f;
                attacker.currentHP = Mathf.Max(0f, attacker.currentHP - selfDmg);
                attacker.SyncDeathFlag();
                yield return new WaitForSeconds(0.8f);
                yield break;
            }

            OnBattleMessage?.Invoke($"{attacker.spriteName} 使用了 {skill.skillName}！");
            PlayAttackAnimation(attacker);
            // 大招施法特效（挂在攻击方，仅 Ultimate）
            if (skill.skillType == SkillType.Ultimate)
                PlaySkillEffect(ModelOf(attacker), skill.effectName, true);
            // 技能音效(按属性+类型)
            if (AudioManager.Instance != null) AudioManager.Instance.PlaySkillSfx(skill);
            yield return new WaitForSeconds(0.9f);

            switch (skill.skillType)
            {
                case SkillType.Basic:
                case SkillType.Ultimate:
                case SkillType.Priority:
                    ExecuteDamage(attacker, defender, skill, defenderDefending);
                    break;
                case SkillType.Buff:
                    ExecuteBuff(attacker, skill);
                    break;
                case SkillType.Debuff:
                    ExecuteDebuff(defender, skill);
                    break;
                case SkillType.Cleanse:
                    ExecuteCleanse(attacker, defender);
                    break;
                case SkillType.Defense:
                    // 防御标记已在结算前设置；此处仅播报
                    if (defenderDefending)
                    {
                        OnBattleMessage?.Invoke($"{attacker.spriteName} 架起了防御，但对方没有攻击…");
                    }
                    else
                    {
                        OnBattleMessage?.Invoke($"{attacker.spriteName} 架起了防御！");
                    }
                    break;
                case SkillType.Heal:
                    float h = attacker.maxHP * skill.healPercent;
                    attacker.currentHP = Mathf.Min(attacker.maxHP, attacker.currentHP + h);
                    OnBattleMessage?.Invoke($"{attacker.spriteName} 回复了 {h:F0} 点 HP！");
                    break;
                case SkillType.Hazard:
                    // 持续伤害状态技（沙暴：4%×5 回合，不可净化）
                    defender.ApplyStatus(StatusEffect.Sandstorm, skill.duration, 1f);
                    OnBattleMessage?.Invoke($"{defender.spriteName} 被沙暴笼罩了！");
                    break;
            }

            yield return new WaitForSeconds(turnInterval);
        }

        private void ExecuteDamage(SpriteInstance attacker, SpriteInstance defender, SkillData skill, bool defenderDefending)
        {
            bool isUltimate = skill.skillType == SkillType.Ultimate;
            bool isAdv = IsAdvantageous(attacker.elementType, defender.elementType);
            bool isDis = IsDisadvantageous(attacker.elementType, defender.elementType);

            float mult = skill.damageMultiplier;
            // 蓄能冲撞：自身有攻击增益层 → +0.2 倍率
            if (skill.name == "PowerCharge" && attacker.HasStrengthBuff()) mult += 0.2f;

            float damage = attacker.attack * attacker.strength * mult;

            if (isUltimate)
            {
                damage *= isAdv ? 1.8f : (isDis ? 0.8f : 1.0f);   // 克制 ×1.8（v5）
            }

            // 灼烧减伤：攻击者本回合灼烧生效 → 造成伤害 -15%
            if (attacker.burnActiveThisTurn) damage *= 0.85f;

            // 防御指令：40% + 自身防御层×10%（上限 70%）
            if (defenderDefending)
            {
                float defReduce = Mathf.Min(0.7f, 0.4f + defender.PositiveProtectLayers() * 0.10f);
                damage *= (1f - defReduce);
                OnBattleMessage?.Invoke($"{defender.spriteName} 的防御挡下了 {(defReduce * 100f):F0}% 伤害！");
            }

            float finalDamage = Mathf.Max(0f, damage / Mathf.Max(0.5f, defender.protect));

            // 铁壁：防御型被动 -8%（直接攻击伤害）
            if (IsDefensiveRole(defender)) finalDamage *= 0.92f;

            // 等级压制：攻击方比防守方每高 1 级，伤害 +5%（上限 ×2.0）
            finalDamage *= LevelDominanceFactor(attacker, defender);

            float dealt = Mathf.Min(finalDamage, defender.currentHP);
            defender.currentHP = Mathf.Max(0f, defender.currentHP - finalDamage);
            defender.SyncDeathFlag();
            if (dealt > 0f)
            {
                PlayHitAnimation(defender);
                if (AudioManager.Instance != null) AudioManager.Instance.PlaySfx("Audio/SFX/sfx_hit_01");
                // 大招命中特效（挂在防守方，无论是否被防御挡下）
                if (skill.skillType == SkillType.Ultimate)
                    PlaySkillEffect(ModelOf(defender), skill.effectName, false);
            }

            // 吸血
            if (skill.lifestealPercent > 0f && dealt > 0f)
            {
                float leech = dealt * skill.lifestealPercent;
                attacker.currentHP = Mathf.Min(attacker.maxHP, attacker.currentHP + leech);
                OnBattleMessage?.Invoke($"{attacker.spriteName} 吸取了 {leech:F0} 点 HP！");
            }

            OnBattleMessage?.Invoke($"造成了 {finalDamage:F0} 点伤害！");

            // 状态附加（v5：概率参数）
            if (skill.statusEffect != StatusEffect.None && Random.value < skill.statusChance)
            {
                defender.ApplyStatus(skill.statusEffect, skill.duration, skill.statusChance);
                // 寄生种子：记录归属（吸取回血）
                if (skill.statusEffect == StatusEffect.LeechSeed)
                    defender.leechSeedOwner = attacker;
                OnBattleMessage?.Invoke($"{defender.spriteName} 陷入了 {GetStatusName(skill.statusEffect)}！");
            }

            // 潮旋：必定降低对方速度 1 点（本场永久，最多 -3，可被净化解除）
            if (skill.name == "Whirlpool")
            {
                defender.ApplyPermanentSpeedDebuff();
                OnBattleMessage?.Invoke($"{defender.spriteName} 速度永久下降了！({defender.speedDebuffLevel})");
            }

            // 藤鞭：10% 降低对方防御 1 层
            if (skill.name == "VineWhip" && Random.value < 0.1f)
            {
                defender.ApplyLayer(false, -0.5f);
                OnBattleMessage?.Invoke($"{defender.spriteName} 防御下降了！");
            }

            if (!defender.IsAlive)
            {
                OnBattleMessage?.Invoke($"{defender.spriteName} 倒下了！");
            }
        }

        private void ExecuteBuff(SpriteInstance target, SkillData skill)
        {
            if (skill.buffType == BuffType.Strength)
            {
                target.ApplyLayer(true, skill.buffAmount);
                OnBattleMessage?.Invoke($"{target.spriteName} 攻击力提升了！");
            }
            else if (skill.buffType == BuffType.Protect)
            {
                target.ApplyLayer(false, skill.buffAmount);
                OnBattleMessage?.Invoke($"{target.spriteName} 防御力提升了！");
            }
        }

        private void ExecuteDebuff(SpriteInstance defender, SkillData skill)
        {
            if (skill.buffType == BuffType.Strength)
            {
                defender.ApplyLayer(true, -skill.buffAmount);
                OnBattleMessage?.Invoke($"{defender.spriteName} 攻击力下降了！");
            }
            else if (skill.buffType == BuffType.Protect)
            {
                defender.ApplyLayer(false, -skill.buffAmount);
                OnBattleMessage?.Invoke($"{defender.spriteName} 防御力下降了！");
            }
        }

        private void ExecuteCleanse(SpriteInstance attacker, SpriteInstance defender)
        {
            bool hadBuff = defender.CleansePositiveLayers();
            OnBattleMessage?.Invoke(hadBuff
                ? $"{defender.spriteName} 的增益与速度减益被清除了！"
                : $"{defender.spriteName} 没有增益可清除…");

            // 净化附加效果（按使用者查表）
            if (attacker.spriteID == "Beholder")
            {
                float heal = attacker.maxHP * 0.10f;
                attacker.currentHP = Mathf.Min(attacker.maxHP, attacker.currentHP + heal);
                OnBattleMessage?.Invoke($"{attacker.spriteName} 回复了 {heal:F0} 点 HP！");
            }
            else if (attacker.spriteID == "StingRay")
            {
                attacker.ApplySpeedLevel(1);
                OnBattleMessage?.Invoke($"{attacker.spriteName} 速度提升了！");
            }
            else if (attacker.spriteID == "StoneGuy")
            {
                attacker.ApplyLayer(false, 0.5f);
                OnBattleMessage?.Invoke($"{attacker.spriteName} 防御提升了！");
            }
        }

        // ==================== 精灵球 ====================

        private IEnumerator ExecutePlayerBall()
        {
            SetState(BattleState.Catching);
            SetState(BattleState.Catching);
            if (AudioManager.Instance != null) AudioManager.Instance.PlaySfx("Audio/SFX/sfx_throw");
            OnBattleMessage?.Invoke($"投出了 {_ballQuality} PokeBall！");
            yield return new WaitForSeconds(1f);

            // 超级球必定捕捉成功（不论血量/等级）
            bool catchSuccess;
            if (_ballQuality == PokeBallQuality.Ultra)
            {
                catchSuccess = true;
            }
            else
            {
                float rate = ComputeCatchRate(_ballQuality, _enemySprite);
                catchSuccess = Random.value < rate;
            }

            for (int i = 0; i < 3; i++)
            {
                OnBattleMessage?.Invoke($"精灵球晃动了 {i + 1} 次...");
                yield return new WaitForSeconds(0.6f);
            }

            if (catchSuccess)
            {
                // 捕捉成功 → 直接胜利
                OnBattleMessage?.Invoke($"成功捕捉 {_enemySprite.spriteName}！");
                OnBattleMessage?.Invoke($"成功捕捉 {_enemySprite.spriteName}！");
                if (AudioManager.Instance != null) AudioManager.Instance.PlaySfx("Audio/SFX/sfx_catch");
                _enemySprite.caughtWith = _ballQuality;
                _enemySprite.FullHeal();
                _enemySprite.energy = SpriteInstance.MaxEnergy;   // 捕捉入队：能量回满

                // 经验分配：捕捉到的精灵与最后出战精灵各 70%，其余腰间精灵各 30%
                var gm = GameManager.Instance;
                int baseExp = (55 * _enemySprite.level + 30) * (_isBossBattle ? 2 : 1);
                GrantExp(_enemySprite, baseExp, 0.7f);
                GrantExp(_playerSprite, baseExp, 0.7f);
                if (gm != null)
                {
                    var slots = gm.playerData.pokeBallSlots;
                    foreach (var slot in slots)
                    {
                        if (slot != null && slot.isLoaded && slot.sprite != null
                            && slot.sprite != _playerSprite && slot.sprite != _enemySprite && slot.sprite.IsAlive)
                        {
                            GrantExp(slot.sprite, baseExp, 0.3f);
                        }
                    }
                }

                int goldReward = 50 + (_enemySprite.level - 5) * 4;
                var result = new BattleResult
                {
                    playerWon = true,
                    caught = true,
                    caughtSprite = _enemySprite,
                    goldReward = goldReward
                };
                EventBus.Publish(EventBus.ON_BATTLE_END, result);
                EventBus.Publish(EventBus.ON_BATTLE_END);
                SetState(BattleState.Victory);
                FinishBattle();
            }
            else
            {
                // 捕捉失败 → 敌方本回合行动照常执行
                OnBattleMessage?.Invoke("精灵球弹开了！ 捕捉失败！");
                OnBattleMessage?.Invoke("精灵球弹开了！ 捕捉失败！");
                if (AudioManager.Instance != null) AudioManager.Instance.PlaySfx("Audio/SFX/sfx_ui_error");
                yield return new WaitForSeconds(0.6f);
                var dec = EnemyAIDecide();
                _enemyDefending = dec != null && dec.skill != null && dec.skill.skillType == SkillType.Defense;
                yield return StartCoroutine(ExecuteEnemyAction(dec));
                if (_playerSprite != null && !_playerSprite.IsAlive)
                {
                    yield return StartCoroutine(HandlePlayerFaint());
                }
            }
        }

        // ==================== 切换 / 阵亡 ====================

        private void DoSwitch(int slotIndex)
        {
            var newSprite = GetBeltSprite(slotIndex);
            if (newSprite == null || !newSprite.IsAlive || newSprite == _playerSprite) return;

            _playerSprite = newSprite;
            var gm = GameManager.Instance;
            int idx = gm.playerData.spriteBag.IndexOf(newSprite);
            if (idx >= 0) gm.playerData.activeSpriteIndex = idx;
            OnBattleMessage?.Invoke($"切换到了 {_playerSprite.spriteName}！");
            RebuildPlayerModel();
        }

        /// <summary>只重建玩家模型（保留敌方模型，避免 Boss 战切换后敌方消失）</summary>
        private void RebuildPlayerModel()
        {
            if (_playerModel != null) Destroy(_playerModel);
            _playerModel = SpawnModelFor(_playerSprite, playerSpritePosition);
            if (_playerModel != null)
            {
                PlayBattleIdle(_playerModel);
                FaceOpponent(_playerModel, playerSpritePosition, enemySpritePosition);
            }
        }

        /// <summary>敌方阵亡处理：死亡动画 → 队伍有下一只则切换出场，否则胜利结算</summary>


        /// <summary>敌方队伍推进：返回 true = 还有下一只（满血满状态出场），false = 队伍耗尽</summary>


                /// <summary>敌方阵亡处理：死亡动画 → 队伍有下一只则满状态切换出场，否则胜利结算</summary>
        private IEnumerator HandleEnemyFaint()
        {
            if (_enemyModel != null)
                yield return StartCoroutine(PlayDeathAnimation(_enemySprite));
            if (AudioManager.Instance != null) AudioManager.Instance.PlaySfx("Audio/SFX/sfx_faint_01");
            yield return new WaitForSeconds(0.4f);

            _enemyIndex++;
            if (_enemyIndex < _enemyTeam.Count)
            {
                // 下一只满状态登场（重置 buff/状态）
                _enemySprite = _enemyTeam[_enemyIndex];
                ResetBattleModifiers(_enemySprite, false);   // 敌方换人：沙暴保留（场地效果跨换人）
                _enemyDefending = false;

                // 重建敌方模型
                if (_enemyModel != null && (_sourceMonster == null || _enemyModel != _sourceMonster.gameObject))
                    Destroy(_enemyModel);
                _enemyModel = SpawnModelFor(_enemySprite, enemySpritePosition);
                if (_enemyModel != null)
                {
                    PlayBattleIdle(_enemyModel);
                    FaceOpponent(_enemyModel, enemySpritePosition, playerSpritePosition);
                }

                OnBattleMessage?.Invoke($"对方派出了 {_enemySprite.spriteName}！");
                yield return new WaitForSeconds(1f);
                SetState(BattleState.PlayerTurn);
            }
            else
            {
                // 队伍耗尽 → 胜利（死亡动画已播，不重复）
                SetState(BattleState.Victory);
                yield return StartCoroutine(HandleVictory(false));
            }
        }

        private IEnumerator HandlePlayerFaint()
        {
            // 己方死亡动画播完后再处理切换/失败
            if (_playerModel != null)
                yield return StartCoroutine(PlayDeathAnimation(_playerSprite));
            if (AudioManager.Instance != null) AudioManager.Instance.PlaySfx("Audio/SFX/sfx_faint_01");
            yield return new WaitForSeconds(0.4f);

            // 阵亡精灵强制放回背包（从腰间卸下）
            var gm = GameManager.Instance;
            if (gm != null) gm.UnequipSpriteFromBelt(_playerSprite);

            if (TeamHasAlive())
            {
                // 腰间有存活精灵 → 强制切换
                SetState(BattleState.ForcedSwitch);
                OnBattleMessage?.Invoke($"{_playerSprite.spriteName} 倒下了！请选择下一只精灵。");
            }
            else
            {
                // 全灭 → 失败结算
                yield return StartCoroutine(HandleDefeat());
            }
        }

        /// <summary>强制切换（阵亡后）</summary>
        public void ForceSwitch(int slotIndex)
        {
            if (_state != BattleState.ForcedSwitch) return;
            var newSprite = GetBeltSprite(slotIndex);
            if (newSprite == null || !newSprite.IsAlive) return;

            _playerSprite = newSprite;
            var gm = GameManager.Instance;
            int idx = gm.playerData.spriteBag.IndexOf(newSprite);
            if (idx >= 0) gm.playerData.activeSpriteIndex = idx;
            OnBattleMessage?.Invoke($"切换到了 {_playerSprite.spriteName}！");
            RebuildPlayerModel();

            SetState(BattleState.PlayerTurn);
        }

        /// <summary>
        /// 全灭结算：有复活药水或金币≥80 → 回地牢；否则 Game Over
        /// </summary>
        private IEnumerator HandleDefeat()
        {
            SetState(BattleState.Defeat);
            OnBattleMessage?.Invoke("全部精灵倒下了！");
            if (AudioManager.Instance != null) AudioManager.Instance.PlaySfx("Audio/SFX/sfx_ui_error");

            var gm = GameManager.Instance;
            bool canRecover = gm.playerData.reviveBottles > 0 || gm.playerData.gold >= 80;

            yield return new WaitForSeconds(2f);

            EventBus.Publish(EventBus.ON_BATTLE_END, new BattleResult { playerWon = false, isBoss = _isBossBattle });
            EventBus.Publish(EventBus.ON_BATTLE_END);
            if (_isBossBattle)
            {
                // Boss 挑战制：失败整体回滚到战前快照，无惩罚、不 Game Over，可再次挑战
                gm.RestoreBattleSnapshot();
                OnBattleMessage?.Invoke("挑战失败…状态已恢复。");
            }
            else if (!canRecover)
            {
                EventBus.Publish(EventBus.ON_GAME_OVER);
            }
            FinishBattle();
        }

        // ==================== 回合末 / 胜负 ====================

        /// <summary>回合初状态结算：双方状态真伤 + 灼烧减伤标记 + 寄生种子回血</summary>
        private IEnumerator TickStatuses()
        {
            float pdmg = _playerSprite.TickStartOfTurn();
            float edmg = _enemySprite.TickStartOfTurn();

            if (pdmg > 0f)
            {
                OnBattleMessage?.Invoke($"{_playerSprite.spriteName} 受到状态伤害 {pdmg:F0}！");
                _playerSprite.SyncDeathFlag();
            }
            if (edmg > 0f)
            {
                OnBattleMessage?.Invoke($"{_enemySprite.spriteName} 受到状态伤害 {edmg:F0}！");
                _enemySprite.SyncDeathFlag();
            }

            // 寄生种子：种子归属方回血（吸取量 ≤ 归属方 maxHP 10%）
            HandleLeechSeed(_playerSprite);
            HandleLeechSeed(_enemySprite);

            yield return new WaitForSeconds(0.5f);

            // 状态真伤击杀 → 立即胜负
            if (!_enemySprite.IsAlive)
            {
                SetState(BattleState.Victory);
                yield return StartCoroutine(HandleVictory());
                yield break;
            }
            if (!_playerSprite.IsAlive)
            {
                yield return StartCoroutine(HandlePlayerFaint());
            }
        }

        /// <summary>寄生种子吸取回血（伤害已在 TickStartOfTurn 扣除）</summary>
        private void HandleLeechSeed(SpriteInstance victim)
        {
            if (victim == null || victim.leechSeedOwner == null) return;
            var owner = victim.leechSeedOwner;
            if (!owner.IsAlive) return;
            float leech = Mathf.Min(victim.maxHP * 0.05f, owner.maxHP * 0.10f);
            owner.currentHP = Mathf.Min(owner.maxHP, owner.currentHP + leech);
            OnBattleMessage?.Invoke($"{owner.spriteName} 通过寄生种子吸取了 {leech:F0} 点 HP！");
        }

        /// <summary>是否为防御型定位（基础 HP ≥ 60）</summary>
        private static bool IsDefensiveRole(SpriteInstance s)
        {
            return s != null && s.baseHp >= 60f;
        }

        private IEnumerator TurnEnd()
        {
            if (IsBattleOver()) yield break;
            SetState(BattleState.Resolving);
            OnBattleMessage?.Invoke("--- 回合结束 ---");

            // 回合末：攻防层/速度层持续-1（状态真伤已在回合初结算）
            _playerSprite.TickEndOfTurn();
            _enemySprite.TickEndOfTurn();

            yield return new WaitForSeconds(0.5f);

            // 胜负检查：敌方当前精灵死亡 → 队伍推进或胜利
            if (!_enemySprite.IsAlive)
            {
                yield return StartCoroutine(HandleEnemyFaint());
                yield break;
            }
            if (!_playerSprite.IsAlive)
            {
                yield return StartCoroutine(HandlePlayerFaint());
                yield break;
            }

            _playerDefending = false;
            _enemyDefending = false;
            yield return new WaitForSeconds(0.3f);
            SetState(BattleState.PlayerTurn);
        }

        private IEnumerator HandleVictory(bool playDeathAnim = true)
        {
            var gm = GameManager.Instance;
            OnBattleMessage?.Invoke($"击败了 {_enemySprite.spriteName}！");
            if (AudioManager.Instance != null) AudioManager.Instance.PlaySfx("Audio/SFX/sfx_victory");

            // 敌方死亡动画（播完约 1 秒）后再结算与传送（HandleEnemyFaint 已播过时不重复播）
            if (playDeathAnim && _enemyModel != null)
                yield return StartCoroutine(PlayDeathAnimation(_enemySprite));
            yield return new WaitForSeconds(0.4f);

            // 金币奖励（Boss 战按整队等级合计）
            int teamLevel = 0;
            foreach (var e in _enemyTeam)
                if (e != null) teamLevel += e.level;
            int goldReward = _isBossBattle
                ? teamLevel * 8 + 50
                : _enemySprite.level * 3 + 10;
            gm.ChangeGold(goldReward);
            OnBattleMessage?.Invoke($"获得 {goldReward} 金币！");

            // 经验分配：出战 100%，腰间其他（≤5）各 20%（Boss 战按整队合计）
            int baseExp = _isBossBattle
                ? (55 * teamLevel + 30 * _enemyTeam.Count)
                : (55 * _enemySprite.level + 30);
            GrantExp(_playerSprite, baseExp, 1f);

            var slots = gm.playerData.pokeBallSlots;
            int granted = 0;
            foreach (var slot in slots)
            {
                if (granted >= 5) break;
                if (slot != null && slot.isLoaded && slot.sprite != null
                    && slot.sprite != _playerSprite && slot.sprite.IsAlive)
                {
                    GrantExp(slot.sprite, baseExp, 0.2f);
                    granted++;
                }
            }

            yield return new WaitForSeconds(1f);

            EventBus.Publish(EventBus.ON_BATTLE_END, new BattleResult
            {
                playerWon = true,
                goldReward = goldReward,
                isBoss = _isBossBattle
            });
            EventBus.Publish(EventBus.ON_BATTLE_END);
            FinishBattle();
        }

        /// <summary>发放经验并处理升级（升级重置次数池+全回复）</summary>
        private void GrantExp(SpriteInstance sprite, int exp, float ratio)
        {
            int amount = Mathf.CeilToInt(exp * ratio);
            sprite.exp += amount;

            int leveled = 0;
            while (sprite.exp >= GetExpToNext(sprite.level))
            {
                sprite.exp -= GetExpToNext(sprite.level);
                sprite.LevelUp(1);
                leveled++;
            }

            if (leveled > 0)
            {
                OnBattleMessage?.Invoke($"{sprite.spriteName} 升到了 {sprite.level} 级！(经验+{amount})");
                if (AudioManager.Instance != null) AudioManager.Instance.PlaySfx("Audio/SFX/sfx_levelup");
            }
            else
            {
                OnBattleMessage?.Invoke($"{sprite.spriteName} 获得 {amount} 经验。");
            }
        }

        public static int GetExpToNext(int level)
        {
            return 20 * level + 30;
        }

        // ==================== 伤害公式 ====================

        private float ComputeDamage(SpriteInstance attacker, SpriteInstance defender, float multiplier, bool isUltimate, bool defenderDefending)
        {
            bool isAdv = IsAdvantageous(attacker.elementType, defender.elementType);
            bool isDis = IsDisadvantageous(attacker.elementType, defender.elementType);

            float damage = attacker.attack * attacker.strength * multiplier;
            if (isUltimate)
            {
                damage *= isAdv ? 1.8f : (isDis ? 0.8f : 1.0f);   // 克制 ×1.8（v5）
            }
            if (attacker.burnActiveThisTurn) damage *= 0.85f;      // 灼烧减伤 15%
            if (defenderDefending)
            {
                float defReduce = Mathf.Min(0.7f, 0.4f + defender.PositiveProtectLayers() * 0.10f);
                damage *= (1f - defReduce);
            }

            float dmg = Mathf.Max(0f, damage / Mathf.Max(0.5f, defender.protect));
            if (IsDefensiveRole(defender)) dmg *= 0.92f;           // 铁壁 8%
            dmg *= LevelDominanceFactor(attacker, defender);       // 等级压制（每级 +5%，上限 ×2）
            return dmg;
        }

        /// <summary>等级压制系数：攻击方比防守方每高 1 级，伤害 +5%（上限 ×2.0，差 20 级封顶）</summary>
        private static float LevelDominanceFactor(SpriteInstance attacker, SpriteInstance defender)
        {
            int diff = attacker.level - defender.level;
            if (diff <= 0) return 1f;
            return Mathf.Min(2f, 1f + 0.05f * diff);
        }

        private void ApplyDamageToPlayer(float dmg)
        {
            _playerSprite.currentHP = Mathf.Max(0f, _playerSprite.currentHP - dmg);
            _playerSprite.SyncDeathFlag();
            if (dmg > 0f)
            {
                PlayHitAnimation(_playerSprite);
                if (AudioManager.Instance != null) AudioManager.Instance.PlaySfx("Audio/SFX/sfx_hit_01");
            }
            OnBattleMessage?.Invoke($"造成了 {dmg:F0} 点伤害！");
            if (!_playerSprite.IsAlive)
            {
                OnBattleMessage?.Invoke($"{_playerSprite.spriteName} 倒下了！");
            }
        }

        // ==================== 敌方 AI ====================

        private EnemyDecision EnemyAIDecide()
        {
            var skills = _enemySprite.skills;
            var usable = new List<SkillData>();
            foreach (var s in skills)
                if (s != null && _enemySprite.CanUseSkill(s))
                    usable.Add(s);

            var dec = new EnemyDecision();

            // 兜底：全部耗尽 → 基础攻击（×0.7 无限）
            if (usable.Count == 0)
            {
                dec.basicAttack = true;
                return dec;
            }

            bool hasBuffSkill = usable.Exists(s => s.skillType == SkillType.Buff);
            bool hasCleanse = usable.Exists(s => s.skillType == SkillType.Cleanse);
            bool hasDebuff = usable.Exists(s => s.skillType == SkillType.Debuff);
            bool hasDefense = usable.Exists(s => s.skillType == SkillType.Defense);
            bool hasHeal = usable.Exists(s => s.skillType == SkillType.Heal);
            bool hasUltimate = usable.Exists(s => s.skillType == SkillType.Ultimate);

            float hpRatio = _enemySprite.currentHP / _enemySprite.maxHP;
            float playerRatio = _playerSprite.currentHP / _playerSprite.maxHP;

            // 1. 对方带增益 → 净化拆台
            if (hasCleanse && (_playerSprite.strength > 1f || _playerSprite.protect > 1f) && Random.value < 0.45f)
            {
                dec.skill = FindSkill(usable, SkillType.Cleanse);
                _enemySprite.SpendEnergy(dec.skill);
                return dec;
            }
            // 2. 自身被降 → Buff 抵消
            if (hasBuffSkill && (_enemySprite.strength < 1f || _enemySprite.protect < 1f) && Random.value < 0.4f)
            {
                dec.skill = FindSkill(usable, SkillType.Buff);
                _enemySprite.SpendEnergy(dec.skill);
                return dec;
            }
            // 3. 残血预判防御
            if (hasDefense && hpRatio < 0.4f && playerRatio > 0.5f && Random.value < 0.35f)
            {
                dec.skill = FindSkill(usable, SkillType.Defense);
                _enemySprite.SpendEnergy(dec.skill);
                return dec;
            }
            // 4. 残血回复
            if (hasHeal && hpRatio < 0.25f && Random.value < 0.5f)
            {
                dec.skill = FindSkill(usable, SkillType.Heal);
                _enemySprite.SpendEnergy(dec.skill);
                return dec;
            }
            // 5. 自身带增益且剩余≥2 → 趁窗口大招
            if (hasUltimate && _enemySprite.HasStrengthBuff() && Random.value < 0.7f)
            {
                dec.skill = FindSkill(usable, SkillType.Ultimate);
                _enemySprite.SpendEnergy(dec.skill);
                return dec;
            }

            // 6. 默认加权：普攻45% / 大招25% / 减益15% / 增益15%（Boss 狂暴时攻击权重+20%）
            float attackW = 0.45f, ultimateW = 0.25f, debuffW = 0.15f, buffW = 0.15f;
            if (_isBossBattle && hpRatio < 0.5f)
            {
                attackW += 0.2f;
                ultimateW += 0.2f;
                debuffW *= 0.5f;
                buffW *= 0.5f;
            }

            var weighted = new List<System.Tuple<SkillData, float>>();
            foreach (var s in usable)
            {
                float w = s.skillType switch
                {
                    SkillType.Basic => attackW,
                    SkillType.Priority => attackW,
                    SkillType.Ultimate => ultimateW,
                    SkillType.Debuff => debuffW,
                    SkillType.Buff => buffW,
                    _ => 0.05f
                };
                if (w > 0f) weighted.Add(new System.Tuple<SkillData, float>(s, w));
            }

            if (weighted.Count == 0)
            {
                dec.skill = usable[Random.Range(0, usable.Count)];
            }
            else
            {
                float total = 0f;
                foreach (var t in weighted) total += t.Item2;
                float pick = Random.value * total;
                float acc = 0f;
                dec.skill = weighted[weighted.Count - 1].Item1;
                foreach (var t in weighted)
                {
                    acc += t.Item2;
                    if (pick <= acc) { dec.skill = t.Item1; break; }
                }
            }

            _enemySprite.SpendEnergy(dec.skill);
            return dec;
        }

        private SkillData FindSkill(List<SkillData> list, SkillType type)
        {
            foreach (var s in list)
                if (s.skillType == type) return s;
            return null;
        }

        // ==================== 工具 ====================

        /// <summary>获取腰间槽位精灵</summary>
        private SpriteInstance GetBeltSprite(int slotIndex)
        {
            var gm = GameManager.Instance;
            if (gm == null) return null;
            var slots = gm.playerData.pokeBallSlots;
            if (slotIndex < 0 || slotIndex >= slots.Count) return null;
            var slot = slots[slotIndex];
            return (slot != null && slot.isLoaded) ? slot.sprite : null;
        }

        /// <summary>腰间（战斗队伍）是否有存活精灵</summary>
        private bool TeamHasAlive()
        {
            var gm = GameManager.Instance;
            if (gm == null) return false;
            foreach (var slot in gm.playerData.pokeBallSlots)
            {
                if (slot != null && slot.isLoaded && slot.sprite != null && slot.sprite.IsAlive)
                    return true;
            }
            return false;
        }

        /// <summary>腰间第一个装载精灵（默认出战）</summary>
        private SpriteInstance FirstBeltSprite()
        {
            var gm = GameManager.Instance;
            if (gm == null) return null;
            foreach (var slot in gm.playerData.pokeBallSlots)
            {
                if (slot != null && slot.isLoaded && slot.sprite != null)
                    return slot.sprite;
            }
            return null;
        }

        /// <summary>
        /// 捕捉率计算（Chip/Normal/Great）：
        /// 球基础率 × 血量档位(≥60%×0.35 / ≥30%×1.0 / <30%×1.6) × 等级惩罚
        /// 等级惩罚按球品质区分（Chip −2%/级、Normal −1.5%/级、Great −1%/级），
        /// 等级越高不同球的差距越大；Ultra 必定成功不走此函数
        /// </summary>
        private static float ComputeCatchRate(PokeBallQuality q, SpriteInstance target)
        {
            float baseRate = q switch
            {
                PokeBallQuality.Chip => 0.28f,
                PokeBallQuality.Normal => 0.46f,
                PokeBallQuality.Great => 0.64f,
                _ => 0.46f
            };

            // 血量档位
            float hpRatio = target.currentHP / Mathf.Max(1f, target.maxHP);
            float hpFactor = hpRatio >= 0.6f ? 0.35f : (hpRatio >= 0.3f ? 1.0f : 1.6f);

            // 等级惩罚（按球品质，等级越高惩罚越重）
            float perLevel = q switch
            {
                PokeBallQuality.Chip => 0.020f,
                PokeBallQuality.Normal => 0.015f,
                PokeBallQuality.Great => 0.010f,
                _ => 0f
            };
            float levelFactor = Mathf.Max(0f, 1f - (target.level - 5) * perLevel);

            float rate = baseRate * hpFactor * levelFactor;
            return Mathf.Clamp(rate, 0.03f, 0.90f);
        }

        private bool IsBattleOver()
        {
            return _state == BattleState.Escaped || _state == BattleState.Victory
                || _state == BattleState.Defeat;
        }

        /// <summary>获取装备的第 index 个技能（0-2）</summary>
        public SkillData GetEquippedSkill(int index)
        {
            if (_playerSprite == null || index < 0 || index >= 3) return null;
            return _playerSprite.skills[index];
        }

        private string GetStatusName(StatusEffect e)
        {
            return e switch
            {
                StatusEffect.Burn => "灼烧",
                StatusEffect.Poison => "中毒",
                StatusEffect.Paralyze => "麻痹",
                StatusEffect.Sandstorm => "沙暴",
                StatusEffect.LeechSeed => "寄生种子",
                StatusEffect.Confusion => "混乱",
                _ => ""
            };
        }

        /// <summary>战斗结束收尾：清理模型、解除战斗状态、自动存档</summary>
        private void FinishBattle()
        {
            // 战斗结束:恢复地牢 BGM
            if (AudioManager.Instance != null) AudioManager.Instance.PlayBgm(BgmType.Dungeon);

            // 玩家传送回原位（最优先，防止后续异常导致玩家滞留竞技场）
            RestorePlayerPosition();

            bool removed = _state == BattleState.Victory;
            if (_sourceMonster != null)
            {
                // 未胜利(逃跑/全灭) → 怪物传回原位
                if (!removed && _hasMonsterOrigin)
                {
                    _sourceMonster.transform.position = _monsterOriginPos;
                }
                // 停止战斗动画驱动；非胜利（逃跑/全灭）解除死亡定格恢复探索动画，胜利保持死亡姿势直到销毁
                var drv = _sourceMonster.GetComponentInChildren<BattleAnimDriver>();
                if (drv != null)
                {
                    drv.SetActive(false);
                    if (!removed) drv.Unfreeze();
                }
                _sourceMonster.OnBattleEnded(removed);
            }

            ClearBattleModels();
            _sourceMonster = null;
            _bossTrainer = null;
            _enemyTeam.Clear();
            _playerAction = PlayerActionType.None;

            var gm = GameManager.Instance;
            if (gm != null) gm.SetBattleState(false);

        }

        /// <summary>战斗结束恢复玩家位置：xrOrigin 丢失时重新查找，仍失败延迟一帧重试并报错</summary>
        private void RestorePlayerPosition()
        {
            _battleAnchor = null;
            if (xrOrigin == null)
                xrOrigin = FindFirstObjectByType<XROrigin>();
            if (xrOrigin != null)
            {
                xrOrigin.transform.position = _playerOriginPos;
                xrOrigin.transform.rotation = _playerOriginRot;
                // 传送后短暂锁定原位，抵消无头显追踪漂移把玩家拉离
                if (_originLockCoroutine != null) StopCoroutine(_originLockCoroutine);
                _originLockCoroutine = StartCoroutine(LockOriginAt(_playerOriginPos, _playerOriginRot, 0.8f));
                return;
            }
            Debug.LogError("[Battle] XR Origin 未找到，玩家未能传回原位！延迟一帧重试…");
            StartCoroutine(RestorePlayerPositionDelayed());
        }

        private IEnumerator RestorePlayerPositionDelayed()
        {
            yield return null;
            RestorePlayerPosition();
        }

        /// <summary>短暂锁定 origin 在指定位置（战斗结束回原位后防追踪漂移拉离）</summary>
        private IEnumerator LockOriginAt(Vector3 pos, Quaternion rot, float duration)
        {
            float t = 0f;
            while (t < duration)
            {
                if (xrOrigin != null)
                {
                    xrOrigin.transform.position = pos;
                    xrOrigin.transform.rotation = rot;
                }
                t += Time.deltaTime;
                yield return null;
            }
            _originLockCoroutine = null;
        }

        private void Update()
        {
            // 战斗期间锁定 origin 位置，抵消无头显环境下的追踪漂移
            if (_battleAnchor.HasValue && xrOrigin != null)
            {
                xrOrigin.transform.position = _battleAnchor.Value;
            }
        }

        private void SetState(BattleState newState)
        {
            _state = newState;
            OnStateChanged?.Invoke(newState);
        }

        public BattleState CurrentState => _state;
        public SpriteInstance PlayerSprite => _playerSprite;
        public SpriteInstance EnemySprite => _enemySprite;
        public List<SpriteInstance> EnemyTeam => _enemyTeam;
        public int EnemyIndex => _enemyIndex;
        public bool IsBossBattle => _isBossBattle;
        public bool CanCatch => _canCatch;

        #region 属性克制判定
        private bool IsAdvantageous(ElementType attacker, ElementType defender)
        {
            return (attacker, defender) switch
            {
                (ElementType.Fire, ElementType.Grass) => true,
                (ElementType.Grass, ElementType.Rock) => true,
                (ElementType.Rock, ElementType.Electric) => true,
                (ElementType.Electric, ElementType.Water) => true,
                (ElementType.Water, ElementType.Fire) => true,
                _ => false
            };
        }

        private bool IsDisadvantageous(ElementType attacker, ElementType defender)
        {
            return (attacker, defender) switch
            {
                (ElementType.Grass, ElementType.Fire) => true,
                (ElementType.Rock, ElementType.Grass) => true,
                (ElementType.Electric, ElementType.Rock) => true,
                (ElementType.Water, ElementType.Electric) => true,
                (ElementType.Fire, ElementType.Water) => true,
                _ => false
            };
        }
        #endregion
    }
}
