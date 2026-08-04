using System.Collections;
using UnityEngine;

namespace LuluDungeon
{
    /// <summary>
    /// 战斗动画驱动：每帧用 Animator.Play(clip, 0, normalizedTime) 强制覆盖
    /// RPGMonsterBundlePBR 演示状态机的无条件自动过渡（否则会自己播到 Die）。
    /// 挂在 Animator 同物体上。
    ///
    /// 状态模型：
    /// - 循环模式（待机）：SetClip + SetActive(true)，时间自推进，每帧强制覆盖。
    /// - 一次性模式（攻击/受击/死亡）：PlayOnce，播放令牌互斥，新播放取代旧播放。
    /// - 恢复待机的条件只看"当前是否处于循环模式"，与协程启动时的状态无关——
    ///   避免连续动画（攻击未完又受击）后循环 clip 无人接管而无限循环。
    /// - 死亡（returnToIdle=false）：播完定格在倒地姿势并禁用 Animator，
    ///   防止循环的 Die clip 重复"起身再倒地"；Unfreeze() 可恢复（怪物回地牢继续探索）。
    /// </summary>
    public class BattleAnimDriver : MonoBehaviour
    {
        private Animator _anim;
        private string _loopClip = "";    // 循环（待机）clip
        private bool _loopActive;         // 是否处于循环播放模式
        private float _loopTime;          // 循环进度（normalized 0~1，自推进）
        private float _loopLength = 1f;   // 循环 clip 时长
        private bool _playingOnce;        // 一次性动画播放中（暂停循环覆盖）
        private int _onceToken;           // 一次性动画播放令牌

        private void Awake()
        {
            _anim = GetComponent<Animator>();
        }

        /// <summary>获取当前 Animator（用于查询 clip 列表）</summary>
        public Animator AnimatorComp => _anim;

        /// <summary>设置循环播放的 clip（如 IdleBattle），进度从头开始</summary>
        public void SetClip(string clipName)
        {
            _loopClip = clipName ?? "";
            _loopTime = 0f;
            _loopLength = Mathf.Max(0.05f, GetClipLength(_loopClip));
        }

        public void SetActive(bool on)
        {
            _loopActive = on;
            if (!on) _loopClip = "";
        }

        /// <summary>解除死亡定格（怪物离开战斗恢复探索时调用）</summary>
        public void Unfreeze()
        {
            if (_anim != null) _anim.enabled = true;
        }

        /// <summary>
        /// 解析 clip 名：精确名优先，否则按关键字模糊匹配（如 Attack01 → BattleBee_Attack01）
        /// </summary>
        public string ResolveClip(string exactName, string fallbackKeyword)
        {
            if (_anim == null || _anim.runtimeAnimatorController == null) return null;
            var clips = _anim.runtimeAnimatorController.animationClips;
            foreach (var c in clips)
                if (c.name == exactName) return c.name;
            string kw = (fallbackKeyword ?? "").ToLower();
            foreach (var c in clips)
                if (c.name.ToLower().Contains(kw)) return c.name;
            return null;
        }

        /// <summary>
        /// 获取 clip 时长（找不到返回默认 1 秒）
        /// </summary>
        private float GetClipLength(string clipName)
        {
            if (_anim == null || _anim.runtimeAnimatorController == null) return 1f;
            if (string.IsNullOrEmpty(clipName)) return 1f;
            foreach (var c in _anim.runtimeAnimatorController.animationClips)
                if (c.name == clipName) return c.length;
            return 1f;
        }

        /// <summary>
        /// 播放一次性动画（Attack/GetHit/Die 等），期间每帧强制覆盖以防状态机自动过渡。
        /// returnToIdle = true：播完恢复循环待机；false：播完定格（死亡姿势）。
        /// 若播放期间被更新的 PlayOnce 取代，本协程直接退出，循环模式状态由新协程接管。
        /// </summary>
        public IEnumerator PlayOnce(string clipName, bool returnToIdle, System.Action onComplete = null)
        {
            if (_anim == null)
            {
                if (onComplete != null) onComplete();
                yield break;
            }

            string target = ResolveClip(clipName, clipName);
            if (string.IsNullOrEmpty(target))
            {
                if (onComplete != null) onComplete();
                yield break;
            }

            int token = ++_onceToken;   // 新播放令牌：取代所有旧播放
            _playingOnce = true;        // 暂停循环覆盖
            _anim.enabled = true;       // 防呆：若此前被定格则先恢复

            float length = Mathf.Max(0.05f, GetClipLength(target));
            float elapsed = 0f;
            _anim.Play(target, 0, 0f);
            while (elapsed < length)
            {
                if (token != _onceToken) yield break;   // 被更新的播放取代 → 放弃，交由新协程接管
                _anim.Play(target, 0, elapsed / length);
                elapsed += Time.deltaTime;
                yield return null;
            }

            if (token != _onceToken) yield break;
            _playingOnce = false;

            if (returnToIdle && _loopActive && !string.IsNullOrEmpty(_loopClip))
            {
                // 恢复循环待机（从头开始）
                _loopTime = 0f;
            }
            else if (!returnToIdle)
            {
                // 死亡：定格在接近播放结束的位置，禁用 Animator 防止循环 Die 重复播放
                _loopActive = false;
                _anim.Play(target, 0, Mathf.Min(0.98f, Mathf.Max(0f, (length - 0.01f) / length)));
                _anim.enabled = false;
            }
            if (onComplete != null) onComplete();
        }

        private void Update()
        {
            if (_playingOnce || !_loopActive || _anim == null || string.IsNullOrEmpty(_loopClip)) return;
            // 自推进循环进度，每帧强制播放指定 clip 的精确位置，防自动过渡打断
            _anim.Play(_loopClip, 0, _loopTime);
            _loopTime += Time.deltaTime / _loopLength;
            if (_loopTime >= 1f) _loopTime -= Mathf.Floor(_loopTime);
        }
    }
}
