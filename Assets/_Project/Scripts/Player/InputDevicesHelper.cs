using UnityEngine;
using UnityEngine.XR;

namespace LuluDungeon
{
    /// <summary>
    /// 手柄输入辅助类：直接读取 XR 设备状态（不依赖 InputSystem 动作资产，
    /// 兼容 Pico/PXR 运行时）。含边缘检测（Down / Released）。
    /// 注意：设备引用每帧检查有效性，无效则重新获取——Pico Link 在编辑器
    /// 中可能延迟连接，缓存死设备会导致按键永远读不到。
    /// </summary>
    public static class InputDevicesHelper
    {
        private static InputDevice _rightHand;
        private static InputDevice _leftHand;

        private static int _lastFrame = -1;
        private static bool _grip, _trigger, _menu, _secondary;
        private static bool _prevGrip, _prevTrigger, _prevMenu, _prevSecondary;

        private static void Update()
        {
            RefreshDevice(ref _rightHand, XRNode.RightHand);
            RefreshDevice(ref _leftHand, XRNode.LeftHand);

            // 每帧只采样一次
            if (Time.frameCount == _lastFrame) return;
            _lastFrame = Time.frameCount;

            _prevGrip = _grip;
            _prevTrigger = _trigger;
            _prevMenu = _menu;
            _prevSecondary = _secondary;

            _grip = ReadGrip(_rightHand);
            _trigger = ReadTrigger(_rightHand);
            _menu = ReadButton(_leftHand, CommonUsages.menuButton);
            _secondary = ReadButton(_rightHand, CommonUsages.secondaryButton);
        }

        private static void RefreshDevice(ref InputDevice device, XRNode node)
        {
            if (device.isValid) return;
            device = InputDevices.GetDeviceAtXRNode(node);
        }

        private static bool ReadButton(InputDevice device, InputFeatureUsage<bool> usage)
        {
            if (device.isValid && device.TryGetFeatureValue(usage, out bool val))
                return val;
            return false;
        }

        private static bool ReadGrip(InputDevice device)
        {
            // 优先布尔侧键，兜底浮点挤压值（部分运行时只上报浮点）
            if (ReadButton(device, CommonUsages.gripButton)) return true;
            if (device.isValid && device.TryGetFeatureValue(CommonUsages.grip, out float squeeze))
                return squeeze > 0.5f;
            return false;
        }

        private static bool ReadTrigger(InputDevice device)
        {
            if (ReadButton(device, CommonUsages.triggerButton)) return true;
            if (device.isValid && device.TryGetFeatureValue(CommonUsages.trigger, out float value))
                return value > 0.5f;
            return false;
        }

        public static bool IsRightTriggerPressed() { Update(); return _trigger; }
        public static bool IsRightTriggerDown() { Update(); return _trigger && !_prevTrigger; }
        public static bool IsRightGripPressed() { Update(); return _grip; }
        public static bool IsRightGripDown() { Update(); return _grip && !_prevGrip; }
        public static bool IsRightGripReleased() { Update(); return !_grip && _prevGrip; }
        public static bool IsLeftMenuDown() { Update(); return _menu && !_prevMenu; }

        public static bool IsRightSecondaryDown() { Update(); return _secondary && !_prevSecondary; }

        /// <summary>
        /// 诊断信息（供抓取失败时输出，定位 Play 模式输入问题）
        /// </summary>
        public static string GetRightHandDiagnostic()
        {
            Update();
            return $"rightValid={_rightHand.isValid} grip={_grip} trigger={_trigger}";
        }
    }
}
