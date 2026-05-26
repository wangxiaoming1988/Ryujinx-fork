using Ryujinx.Common;
using Ryujinx.Common.Configuration.Hid;
using Ryujinx.Common.Configuration.Hid.Controller;
using Ryujinx.Common.Configuration.Hid.Keyboard;
using Ryujinx.HLE.HOS.Services.Hid;
using System;
using System.Buffers;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Threading;
using CemuHookClient = Ryujinx.Input.Motion.CemuHook.Client;
using ControllerType = Ryujinx.Common.Configuration.Hid.ControllerType;
using PlayerIndex = Ryujinx.HLE.HOS.Services.Hid.PlayerIndex;
using Switch = Ryujinx.HLE.Switch;

namespace Ryujinx.Input.HLE
{
    public class NpadManager : IDisposable
    {
        private readonly CemuHookClient _cemuHookClient;

        private readonly Lock _lock = new();

        private int _inputUpdateBlockCount;

        private const int MaxControllers = 9;

        private readonly NpadController[] _controllers;

        private readonly IGamepadDriver _keyboardDriver;
        private readonly IGamepadDriver _gamepadDriver;
        private readonly IGamepadDriver _mouseDriver;
        private bool _isDisposed;

        private List<InputConfig> _inputConfig;
        private List<InputConfig> _requestedInputConfig;
        private bool _enableKeyboard;
        private bool _enableMouse;
        private Switch _device;
        
        private readonly List<GamepadInput> _hleInputStates = [];
        private readonly List<SixAxisInput> _hleMotionStates = new(NpadDevices.MaxControllers);

        public NpadManager(IGamepadDriver keyboardDriver, IGamepadDriver gamepadDriver, IGamepadDriver mouseDriver)
        {
            _controllers = new NpadController[MaxControllers];
            _cemuHookClient = new CemuHookClient(this);

            _keyboardDriver = keyboardDriver;
            _gamepadDriver = gamepadDriver;
            _mouseDriver = mouseDriver;
            _inputConfig = [];
            _requestedInputConfig = [];

            _gamepadDriver.OnGamepadConnected += HandleOnGamepadConnected;
            _gamepadDriver.OnGamepadDisconnected += HandleOnGamepadDisconnected;
        }

        private void RefreshInputConfigForHLE()
        {
            lock (_lock)
            {
                List<InputConfig> validInputs = [];
                foreach (InputConfig inputConfigEntry in _inputConfig)
                {
                    if (_controllers[(int)inputConfigEntry.PlayerIndex] != null)
                    {
                        validInputs.Add(inputConfigEntry);
                    }
                }

                _device.Hid.RefreshInputConfig(validInputs);
            }
        }

        private void HandleOnGamepadDisconnected(string obj)
        {
            // Force input reload
            lock (_lock)
            {
                // Forcibly disconnect any controllers with this ID.
                for (int i = 0; i < _controllers.Length; i++)
                {
                    if (_controllers[i]?.Id == obj)
                    {
                        _controllers[i]?.Dispose();
                        _controllers[i] = null;
                    }
                }

                ReloadConfiguration(_requestedInputConfig, _enableKeyboard, _enableMouse);
            }
        }

        private void HandleOnGamepadConnected(string _)
        {
            // Force input reload
            ReloadConfiguration(_requestedInputConfig, _enableKeyboard, _enableMouse);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private bool DriverConfigurationUpdate(ref NpadController controller, InputConfig config)
        {
            IGamepadDriver targetDriver =
                config is StandardKeyboardInputConfig
                    ? _keyboardDriver
                    : _gamepadDriver;

            Debug.Assert(targetDriver != null, "Unknown input configuration!");

            if (controller.GamepadDriver != targetDriver || controller.Id != config.Id)
            {
                return controller.UpdateDriverConfiguration(targetDriver, config);
            }

            return controller.GamepadDriver != null;
        }

        public void ReloadConfiguration(List<InputConfig> inputConfig, bool enableKeyboard, bool enableMouse)
        {
            lock (_lock)
            {
                _requestedInputConfig = inputConfig?.ToList() ?? [];

                NpadController[] oldControllers = _controllers.ToArray();

                List<InputConfig> validInputs = [];

                foreach (InputConfig inputConfigEntry in _requestedInputConfig)
                {
                    NpadController controller;
                    int index = (int)inputConfigEntry.PlayerIndex;

                    if (oldControllers[index] != null)
                    {
                        // Try reuse the existing controller.
                        controller = oldControllers[index];
                        oldControllers[index] = null;
                    }
                    else
                    {
                        controller = new(_cemuHookClient);
                    }

                    InputConfig activeConfig = inputConfigEntry;
                    bool isValid = DriverConfigurationUpdate(ref controller, activeConfig);

                    if (!isValid &&
                        inputConfigEntry is StandardControllerInputConfig &&
                        TryGetKeyboardFallback(inputConfigEntry, out StandardKeyboardInputConfig fallbackConfig))
                    {
                        activeConfig = fallbackConfig;
                        isValid = DriverConfigurationUpdate(ref controller, activeConfig);
                    }

                    if (!isValid)
                    {
                        _controllers[index] = null;
                        controller.Dispose();
                    }
                    else
                    {
                        _controllers[index] = controller;
                        validInputs.Add(activeConfig);
                    }
                }

                for (int i = 0; i < oldControllers.Length; i++)
                {
                    // Disconnect any controllers that weren't reused by the new configuration.

                    oldControllers[i]?.Dispose();
                    oldControllers[i] = null;
                }

                _inputConfig = validInputs;
                _enableKeyboard = enableKeyboard;
                _enableMouse = enableMouse;

                _device.Hid.RefreshInputConfig(validInputs);
            }
        }

        private bool TryGetKeyboardFallback(InputConfig inputConfig, out StandardKeyboardInputConfig fallbackConfig)
        {
            fallbackConfig = null;

            ReadOnlySpan<string> keyboardIds = _keyboardDriver.GamepadsIds;

            if (keyboardIds.IsEmpty)
            {
                return false;
            }

            string keyboardId = keyboardIds[0];

            using IGamepad keyboard = _keyboardDriver.GetGamepad(keyboardId);

            if (keyboard == null)
            {
                return false;
            }

            fallbackConfig = InputConfigDefaults.CreateDefaultKeyboardConfiguration(
                keyboardId,
                keyboard.Name,
                inputConfig.ControllerType,
                inputConfig.PlayerIndex);

            return true;
        }

        private void ClearInputDriverStates()
        {
            foreach (InputConfig inputConfig in _inputConfig)
            {
                _controllers[(int)inputConfig.PlayerIndex]?.GamepadDriver?.Clear();
            }
        }

        public void UnblockInputUpdates()
        {
            lock (_lock)
            {
                if (_inputUpdateBlockCount == 0)
                {
                    return;
                }

                _inputUpdateBlockCount--;

                if (_inputUpdateBlockCount == 0)
                {
                    ClearInputDriverStates();
                }
            }
        }

        public bool InputUpdatesBlocked
        {
            get
            {
                lock (_lock)
                    return _inputUpdateBlockCount > 0;
            }
        }

        public void BlockInputUpdates()
        {
            lock (_lock)
            {
                _inputUpdateBlockCount++;
            }
        }

        public void Initialize(Switch device, List<InputConfig> inputConfig, bool enableKeyboard, bool enableMouse)
        {
            _device = device;
            _device.Configuration.RefreshInputConfig = RefreshInputConfigForHLE;

            ReloadConfiguration(inputConfig, enableKeyboard, enableMouse);
        }

        public void Update(float aspectRatio = 1)
        {
            lock (_lock)
            {
                _hleInputStates.Clear();
                _hleMotionStates.Clear();

                KeyboardInput? hleKeyboardInput = null;

                foreach (InputConfig inputConfig in _inputConfig)
                {
                    GamepadInput inputState = default;
                    (SixAxisInput, SixAxisInput) motionState = default;

                    NpadController controller = _controllers[(int)inputConfig.PlayerIndex];
                    PlayerIndex playerIndex = (PlayerIndex)inputConfig.PlayerIndex;

                    bool isJoyconPair = false;

                    // Do we allow input updates and is a controller connected?
                    if (_inputUpdateBlockCount == 0 && controller != null)
                    {
                        DriverConfigurationUpdate(ref controller, inputConfig);

                        controller.UpdateUserConfiguration(inputConfig);
                        controller.Update();
                        controller.UpdateRumble(_device.Hid.Npads.GetRumbleQueue(playerIndex));

                        inputState = controller.GetHLEInputState();

                        inputState.Buttons |= _device.Hid.UpdateStickButtons(inputState.LStick, inputState.RStick);

                        isJoyconPair = inputConfig.ControllerType == ControllerType.JoyconPair;

                        SixAxisInput altMotionState = isJoyconPair ? controller.GetHLEMotionState(true) : default;

                        motionState = (controller.GetHLEMotionState(), altMotionState);
                    }
                    else
                    {
                        // Ensure that orientation isn't null
                        motionState.Item1.Orientation = new float[9];
                    }

                    inputState.PlayerId = playerIndex;
                    motionState.Item1.PlayerId = playerIndex;

                    _hleInputStates.Add(inputState);
                    _hleMotionStates.Add(motionState.Item1);

                    if (isJoyconPair && !motionState.Item2.Equals(default))
                    {
                        motionState.Item2.PlayerId = playerIndex;

                        _hleMotionStates.Add(motionState.Item2);
                    }
                }

                if (_inputUpdateBlockCount == 0 && _enableKeyboard)
                {
                    hleKeyboardInput = NpadController.GetHLEKeyboardInput(_keyboardDriver);
                }

                _device.Hid.Npads.Update(_hleInputStates);
                _device.Hid.Npads.UpdateSixAxis(_hleMotionStates);

                if (hleKeyboardInput.HasValue)
                {
                    _device.Hid.Keyboard.Update(hleKeyboardInput.Value);
                }

                if (_enableMouse)
                {
                    IMouse mouse = _mouseDriver.GetGamepad("0") as IMouse;

                    MouseStateSnapshot mouseInput = IMouse.GetMouseStateSnapshot(mouse);

                    uint buttons = 0;

                    if (mouseInput.IsPressed(MouseButton.Button1))
                    {
                        buttons |= 1 << 0;
                    }

                    if (mouseInput.IsPressed(MouseButton.Button2))
                    {
                        buttons |= 1 << 1;
                    }

                    if (mouseInput.IsPressed(MouseButton.Button3))
                    {
                        buttons |= 1 << 2;
                    }

                    if (mouseInput.IsPressed(MouseButton.Button4))
                    {
                        buttons |= 1 << 3;
                    }

                    if (mouseInput.IsPressed(MouseButton.Button5))
                    {
                        buttons |= 1 << 4;
                    }

                    Vector2 position = IMouse.GetScreenPosition(mouseInput.Position, mouse.ClientSize, aspectRatio);

                    _device.Hid.Mouse.Update((int)position.X, (int)position.Y, buttons, (int)mouseInput.Scroll.X, (int)mouseInput.Scroll.Y, true);
                    
                    ArrayPool<bool>.Shared.Return(mouseInput.ButtonState);
                }
                else
                {
                    _device.Hid.Mouse.Update(0, 0);
                }

                _device.TamperMachine.UpdateInput(_hleInputStates);
            }
        }

        public InputConfig GetPlayerInputConfigByIndex(int index)
        {
            lock (_lock)
            {
                return _inputConfig.FirstOrDefault(x => x.PlayerIndex == (Common.Configuration.Hid.PlayerIndex)index);
            }
        }

        protected virtual void Dispose(bool disposing)
        {
            if (disposing)
            {
                lock (_lock)
                {
                    if (!_isDisposed)
                    {
                        _cemuHookClient.Dispose();

                        _gamepadDriver.OnGamepadConnected -= HandleOnGamepadConnected;
                        _gamepadDriver.OnGamepadDisconnected -= HandleOnGamepadDisconnected;

                        for (int i = 0; i < _controllers.Length; i++)
                        {
                            _controllers[i]?.Dispose();
                        }

                        _isDisposed = true;
                    }
                }
            }
        }

        public void Dispose()
        {
            GC.SuppressFinalize(this);
            Dispose(true);
        }
    }
}
