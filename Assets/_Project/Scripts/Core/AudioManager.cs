using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace LuluDungeon
{
    /// <summary>
    /// 背景音乐类型
    /// </summary>
    public enum BgmType
    {
        None,       // 静音
        Dungeon,    // 地牢探索
        Battle,     // 战斗
        BattleEpic, // 备用史诗战斗(可切换)
        DungeonDark // 备用黑暗地牢
    }

    /// <summary>
    /// 全局音频管理器(单例):
    /// - BGM 循环播放 + 淡入淡出切换
    /// - SFX 一次性音效(PlayOneShot)
    /// - 技能音效映射:按属性(火/电/水/草/岩)+ 技能类型(普攻/大招/增益/减益/净化/防御/回复/持续)
    /// 音频资产存放于 Assets/_Project/Resources/Audio/{BGM,SFX},按路径 Resources.Load 加载。
    /// </summary>
    public class AudioManager : MonoBehaviour
    {
        public static AudioManager Instance { get; private set; }
        /// <summary>确保全局 AudioManager 存在(自动创建,GameManager 启动时调用)</summary>
        public static void EnsureInstance()
        {
            if (Instance != null) return;
            var go = new GameObject("[AudioManager]");
            go.AddComponent<AudioManager>();
        }


        [Header("音源")]
        public AudioSource musicSource;   // BGM 循环源
        public AudioSource sfxSource;     // 音效源(PlayOneShot)

        [Header("音量")]
        [Range(0f, 1f)] public float bgmVolume = 0.6f;
        [Range(0f, 1f)] public float sfxVolume = 0.8f;

        [Header("BGM 路径")]
        [Tooltip("键为 BgmType,值为 Resources 相对路径(不含扩展名)")]
        public string[] bgmPaths = new string[]
        {
            "",                             // None
            "Audio/BGM/BGM_Dungeon",        // Dungeon
            "Audio/BGM/BGM_Battle",         // Battle
            "Audio/BGM/BGM_Battle_Epic",    // BattleEpic
            "Audio/BGM/BGM_Dungeon_Dark"    // DungeonDark
        };

        [Header("淡入淡出")]
        [Range(0.1f, 5f)] public float fadeDuration = 1.2f;

        private BgmType _currentBgm = BgmType.None;
        private Coroutine _fadeCoroutine;

        // 元素音效池(Resources 路径数组,播放时随机取一个)
        private static readonly Dictionary<ElementType, string[]> ElementSfx = new()
        {
            { ElementType.Fire,     new[] { "Audio/SFX/sfx_fire_01", "Audio/SFX/sfx_fire_02", "Audio/SFX/sfx_fire_03", "Audio/SFX/sfx_fire_04", "Audio/SFX/sfx_fire_05" } },
            { ElementType.Electric, new[] { "Audio/SFX/sfx_electric_01", "Audio/SFX/sfx_electric_02", "Audio/SFX/sfx_electric_03" } },
            { ElementType.Water,    new[] { "Audio/SFX/sfx_water_01", "Audio/SFX/sfx_water_02", "Audio/SFX/sfx_water_03" } },
            { ElementType.Grass,    new[] { "Audio/SFX/sfx_grass_01", "Audio/SFX/sfx_grass_02", "Audio/SFX/sfx_grass_03" } },
            { ElementType.Rock,     new[] { "Audio/SFX/sfx_rock_01", "Audio/SFX/sfx_rock_02", "Audio/SFX/sfx_rock_03" } }
        };

        private readonly Dictionary<string, AudioClip> _cache = new();
        private readonly System.Random _rng = new();

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
            DontDestroyOnLoad(gameObject);

            if (musicSource == null) musicSource = gameObject.AddComponent<AudioSource>();
            if (sfxSource == null) sfxSource = gameObject.AddComponent<AudioSource>();
            musicSource.loop = true;
            musicSource.playOnAwake = false;
            musicSource.spatialBlend = 0f;
            sfxSource.playOnAwake = false;
            sfxSource.spatialBlend = 0f;
        }

        private void Start()
        {
            ApplyVolumes();
        }

        /// <summary>设置 BGM(自动淡出旧曲、淡入新曲;同曲不重启)</summary>
        public void PlayBgm(BgmType type)
        {
            if (type == _currentBgm) return;
            _currentBgm = type;

            AudioClip clip = null;
            if (type != BgmType.None)
            {
                int idx = (int)type;
                if (idx >= 0 && idx < bgmPaths.Length && !string.IsNullOrEmpty(bgmPaths[idx]))
                    clip = Load(bgmPaths[idx]);
            }

            if (_fadeCoroutine != null) StopCoroutine(_fadeCoroutine);
            _fadeCoroutine = StartCoroutine(FadeTo(clip));
        }

        public BgmType CurrentBgm => _currentBgm;

        /// <summary>播放一次性音效(路径相对 Resources,不含扩展名)</summary>
        public void PlaySfx(string path)
        {
            if (string.IsNullOrEmpty(path)) return;
            AudioClip clip = Load(path);
            if (clip != null) sfxSource.PlayOneShot(clip, sfxVolume);
        }

        /// <summary>播放技能音效:按元素属性 + 技能类型映射</summary>
        public void PlaySkillSfx(SkillData skill)
        {
            if (skill == null) return;

            switch (skill.skillType)
            {
                case SkillType.Basic:
                case SkillType.Priority:
                    PlayElementAttack(skill.elementType, heavy: false);
                    break;
                case SkillType.Ultimate:
                    // 大招:元素音效 + 重击音效叠加,更有爆发感
                    PlayElementAttack(skill.elementType, heavy: true);
                    if (Random.value < 0.7f) PlaySfx("Audio/SFX/sfx_hit_01");
                    break;
                case SkillType.Buff:
                    PlayRandom(ElementSfx.TryGetValue(skill.elementType, out var pool) ? pool : null, "Audio/SFX/sfx_buff_01", "Audio/SFX/sfx_buff_02", "Audio/SFX/sfx_buff_03");
                    break;
                case SkillType.Debuff:
                    PlaySfx("Audio/SFX/sfx_roar_01");
                    break;
                case SkillType.Cleanse:
                    PlaySfx("Audio/SFX/sfx_magic_04");
                    break;
                case SkillType.Defense:
                    PlaySfx(Random.value < 0.5f ? "Audio/SFX/sfx_defend_01" : "Audio/SFX/sfx_defend_02");
                    break;
                case SkillType.Heal:
                    PlaySfx("Audio/SFX/sfx_magic_07");
                    break;
                case SkillType.Hazard:
                    PlaySfx("Audio/SFX/sfx_rock_01");
                    break;
                default:
                    PlayElementAttack(skill.elementType, heavy: false);
                    break;
            }
        }

        /// <summary>播放元素攻击音效(heavy = 大招加重视觉,取更长的火系音效)</summary>
        private void PlayElementAttack(ElementType element, bool heavy)
        {
            if (!ElementSfx.TryGetValue(element, out var pool)) return;
            string path = pool[_rng.Next(pool.Length)];
            if (heavy && element == ElementType.Fire && pool.Length > 4)
                path = "Audio/SFX/sfx_fire_07";   // 火系大招用最猛的一个
            PlaySfx(path);
        }

        private void PlayRandom(string[] pool, params string[] fallbacks)
        {
            if (pool != null && pool.Length > 0)
            {
                PlaySfx(pool[_rng.Next(pool.Length)]);
                return;
            }
            if (fallbacks.Length > 0)
                PlaySfx(fallbacks[_rng.Next(fallbacks.Length)]);
        }

        private AudioClip Load(string path)
        {
            if (_cache.TryGetValue(path, out var cached)) return cached;
            var clip = Resources.Load<AudioClip>(path);
            if (clip == null)
                Debug.LogWarning($"[AudioManager] 找不到音频: {path}");
            else
                _cache[path] = clip;
            return clip;
        }

        private IEnumerator FadeTo(AudioClip newClip)
        {
            float t = 0f;
            float startVol = musicSource.volume;
            if (musicSource.isPlaying)
            {
                // 淡出旧曲
                while (t < fadeDuration)
                {
                    t += Time.deltaTime;
                    musicSource.volume = Mathf.Lerp(startVol, 0f, t / fadeDuration);
                    yield return null;
                }
                musicSource.Stop();
            }

            if (newClip == null) yield break;

            musicSource.clip = newClip;
            musicSource.volume = 0f;
            musicSource.Play();

            // 淡入新曲
            t = 0f;
            while (t < fadeDuration)
            {
                t += Time.deltaTime;
                musicSource.volume = Mathf.Lerp(0f, bgmVolume, t / fadeDuration);
                yield return null;
            }
            musicSource.volume = bgmVolume;
            _fadeCoroutine = null;
        }

        private void ApplyVolumes()
        {
            musicSource.volume = bgmVolume;
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }
    }
}
