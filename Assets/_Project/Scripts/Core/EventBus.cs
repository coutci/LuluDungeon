using System;
using System.Collections.Generic;

namespace LuluDungeon
{
    /// <summary>
    /// 简易事件总线，解耦各系统
    /// </summary>
    public static class EventBus
    {
        private static Dictionary<string, Delegate> _events = new Dictionary<string, Delegate>();

        // 事件键按订阅类型区分，避免同 key 无参/泛型委托冲突
        private static string Key(string eventName, System.Type t)
        {
            return eventName + "|" + t.FullName;
        }

        public static void Subscribe<T>(string eventName, Action<T> callback)
        {
            string key = Key(eventName, typeof(T));
            if (_events.ContainsKey(key))
                _events[key] = Delegate.Combine(_events[key], callback);
            else
                _events[key] = callback;
        }

        public static void Subscribe(string eventName, Action callback)
        {
            string key = Key(eventName, typeof(Action));
            if (_events.ContainsKey(key))
                _events[key] = Delegate.Combine(_events[key], callback);
            else
                _events[key] = callback;
        }

        public static void Unsubscribe<T>(string eventName, Action<T> callback)
        {
            string key = Key(eventName, typeof(T));
            if (!_events.ContainsKey(key)) return;
            _events[key] = Delegate.Remove(_events[key], callback);
            if (_events[key] == null)
                _events.Remove(key);
        }

        public static void Unsubscribe(string eventName, Action callback)
        {
            string key = Key(eventName, typeof(Action));
            if (!_events.ContainsKey(key)) return;
            _events[key] = Delegate.Remove(_events[key], callback);
            if (_events[key] == null)
                _events.Remove(key);
        }

        public static void Publish<T>(string eventName, T args)
        {
            string key = Key(eventName, typeof(T));
            if (_events.TryGetValue(key, out var del) && del is Action<T> typed)
                typed.Invoke(args);
        }

        public static void Publish(string eventName)
        {
            string key = Key(eventName, typeof(Action));
            if (_events.TryGetValue(key, out var del) && del is Action typed)
                typed.Invoke();
        }

        public static void Clear()
        {
            _events.Clear();
        }

        // --- 预定义事件名 ---
        public const string ON_BATTLE_START = "OnBattleStart";
        public const string ON_BATTLE_END = "OnBattleEnd";
        public const string ON_SPRITE_CAUGHT = "OnSpriteCaught";
        public const string ON_LEVEL_CHANGE = "OnLevelChange";
        public const string ON_GOLD_CHANGED = "OnGoldChanged";
        public const string ON_SPRITE_FAINTED = "OnSpriteFainted";
        public const string ON_SHOP_ENTER = "OnShopEnter";
        public const string ON_SHOP_EXIT = "OnShopExit";
        public const string ON_GAME_OVER = "OnGameOver";
        public const string ON_GAME_WIN = "OnGameWin";
        public const string ON_PLAYER_DIED = "OnPlayerDied";
    }
}
