using Avalonia.Collections;
using Avalonia.Controls;
using Avalonia.Svg.Skia;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using Gommon;
using Ryujinx.Ava.Common.Locale;
using Ryujinx.Ava.Input;
using Ryujinx.Ava.Systems.Configuration;
using Ryujinx.Ava.UI.Helpers;
using Ryujinx.Ava.UI.Models;
using Ryujinx.Ava.UI.Models.Input;
using Ryujinx.Ava.UI.Windows;
using Ryujinx.Common;
using Ryujinx.Common.Configuration;
using Ryujinx.Common.Configuration.Hid;
using Ryujinx.Common.Configuration.Hid.Controller;
using Ryujinx.Common.Configuration.Hid.Keyboard;
using Ryujinx.Common.Logging;
using Ryujinx.Common.Utilities;
using Ryujinx.Input;
using Ryujinx.Input.SDL3;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;

namespace Ryujinx.Ava.UI.ViewModels.Input
{
    public partial class InputViewModel : BaseModel, IDisposable
    {
        private const string Disabled = "disabled";
        private const string ProControllerResource = "Ryujinx/Assets/Icons/Controller_ProCon.svg";
        private const string JoyConPairResource = "Ryujinx/Assets/Icons/Controller_JoyConPair.svg";
        private const string JoyConLeftResource = "Ryujinx/Assets/Icons/Controller_JoyConLeft.svg";
        private const string JoyConRightResource = "Ryujinx/Assets/Icons/Controller_JoyConRight.svg";
        private const string KeyboardString = "keyboard";
        private const string ControllerString = "controller";
        private readonly MainWindow _mainWindow;
        private Control _keyboardDriverControl;

        private PlayerIndex _playerId;
        private PlayerIndex _playerIdChoose;
        private int _controller;
        private string _controllerImage;
        private int _device;
        private bool _isChangeTrackingActive;
        [ObservableProperty]
        public partial bool IsModified { get; set; }

        [ObservableProperty]
        public partial string ProfileName { get; set; }

        [ObservableProperty]
        public partial bool NotificationIsVisible { get; set; } // Automatically call the NotificationView property with OnPropertyChanged()

        [ObservableProperty]
        public partial string NotificationText { get; set; } // Automatically call the NotificationText property with OnPropertyChanged()

        private bool _isLoaded;

        private static readonly InputConfigJsonSerializerContext _serializerContext = new(JsonHelper.GetDefaultSerializerOptions());

        public IGamepadDriver AvaloniaKeyboardDriver { get; private set; }

        public IGamepad SelectedGamepad
        {
            get;
            private set
            {
                if (!ReferenceEquals(field, value))
                {
                    field?.Dispose();
                }

                Rainbow.Reset();

                field = value;

                if ((field?.Features & GamepadFeaturesFlag.Led) != 0 &&
                    ConfigViewModel is ControllerInputViewModel { Config.UseRainbowLed: true })
                    Rainbow.Updated += (ref Color color) => field.SetLed((uint)color.ToArgb());

                OnPropertiesChanged(nameof(HasLed), nameof(CanClearLed));
            }
        }

        public StickVisualizer VisualStick { get; private set; }

        public ObservableCollection<PlayerModel> PlayerIndexes { get; set; }
        public ObservableCollection<(DeviceType Type, string Id, string Name)> Devices { get; set; }
        internal ObservableCollection<ControllerModel> Controllers { get; set; }
        public AvaloniaList<string> ProfilesList { get; set; }

        public bool UseGlobalConfig;

        // XAML Flags
        public bool ShowSettings => _device > 0;
        public bool IsController => _device > 1;
        public bool IsKeyboard => !IsController;
        public bool IsRight { get; set; }
        public bool IsLeft { get; set; }
        public bool HasLed => (SelectedGamepad.Features & GamepadFeaturesFlag.Led) != 0;
        public bool CanClearLed => SelectedGamepad.Name.ContainsIgnoreCase("DualSense");

        public event Action NotifyChangesEvent;

        public string ChosenProfile
        {
            get;
            set
            {
                // When you select a profile, the settings from the profile will be applied.
                // To save the settings, you still need to click the apply button
                field = value;
                LoadProfile();
                OnPropertyChanged();
            }
        }

        public object ConfigViewModel
        {
            get;
            set
            {
                field = value;

                VisualStick.UpdateConfig(value);

                OnPropertyChanged();
            }
        }

        public PlayerIndex PlayerIdChoose
        {
            get => _playerIdChoose;
            set { }
        }

        public PlayerIndex PlayerId
        {
            get => _playerId;
            set
            {
                if (IsModified)
                {
                    _playerIdChoose = value;
                    return;
                }

                IsModified = false;
                _playerId = value;
                _isChangeTrackingActive = false;

                if (!Enum.IsDefined<PlayerIndex>(_playerId))
                {
                    _playerId = PlayerIndex.Player1;

                }

                _isLoaded = false;
                LoadConfiguration();
                LoadDevice();
                LoadProfiles();

                _isLoaded = true;
                _isChangeTrackingActive = true;
                OnPropertyChanged();
            }
        }

        public int Controller
        {
            get => _controller;
            set
            {
                int controllerIndex = value < 0 ? 0 : value;

                if (controllerIndex == _controller)
                {
                    return;
                }

                ApplyControllerSelection(controllerIndex);
                RefreshModifiedState();
            }
        }

        private void ApplyControllerSelection(int controllerIndex)
        {
            _controller = controllerIndex;

            if (Controllers.Count > 0 && _controller < Controllers.Count && _controller > -1)
            {
                ControllerType controller = Controllers[_controller].Type;

                IsLeft = true;
                IsRight = true;

                switch (controller)
                {
                    case ControllerType.Handheld:
                        ControllerImage = JoyConPairResource;
                        break;
                    case ControllerType.ProController:
                        ControllerImage = ProControllerResource;
                        break;
                    case ControllerType.JoyconPair:
                        ControllerImage = JoyConPairResource;
                        break;
                    case ControllerType.JoyconLeft:
                        ControllerImage = JoyConLeftResource;
                        IsRight = false;
                        break;
                    case ControllerType.JoyconRight:
                        ControllerImage = JoyConRightResource;
                        IsLeft = false;
                        break;
                }

                LoadInputDriver();
                LoadProfiles();
            }

            OnPropertyChanged(nameof(Controller));
            NotifyChanges();
        }

        public string ControllerImage
        {
            get => _controllerImage;
            set
            {
                _controllerImage = value;

                OnPropertyChanged();
                OnPropertyChanged(nameof(Image));
            }
        }

        public SvgImage Image
        {
            get
            {
                SvgImage image = new();

                if (!string.IsNullOrWhiteSpace(_controllerImage))
                {
                    SvgSource source = SvgSource.LoadFromStream(EmbeddedResources.GetStream(_controllerImage));

                    image.Source = source;
                }

                return image;
            }
        }

        public int Device
        {
            get => _device;
            set
            {
                if (value < 0 || value >= Devices.Count)
                {
                    return;
                }

                _device = value;

                DeviceType selected = Devices[_device].Type;

                if (selected != DeviceType.None)
                {
                    if (_isLoaded)
                    {
                        LoadSelectedDeviceDefaults();
                    }
                    else if (_device < Devices.Count)
                    {
                        LoadControllers();
                    }
                }

                RefreshModifiedState();
                FindPairedDeviceInConfigFile();
                OnPropertyChanged();
                OnPropertyChanged(nameof(SelectedDeviceItem));
                NotifyChanges();
            }
        }

        public bool NeedsResetCurrentDeviceToDefaultsConfirmation()
        {
            if (_device <= 0 || _device >= Devices.Count || Devices[_device].Type == DeviceType.None)
            {
                return false;
            }

            InputConfig defaultConfig = LoadDefaultConfiguration();
            InputConfig currentConfig = GetSelectedDeviceConfig();

            return !ConfigsMatch(currentConfig, defaultConfig);
        }

        public void ResetCurrentDeviceToDefaults()
        {
            RefreshAvailableDevices();

            if (_device <= 0 || _device >= Devices.Count || Devices[_device].Type == DeviceType.None)
            {
                return;
            }

            LoadSelectedDeviceDefaults();
            RefreshModifiedState();
            FindPairedDeviceInConfigFile();
            NotifyChanges();
        }

        public void RefreshInputDevices()
        {
            RefreshAvailableDevices();
        }

        public object SelectedDeviceItem
        {
            get => _device >= 0 && _device < Devices.Count ? Devices[_device] : null;
            set
            {
                if (value is not ValueTuple<DeviceType, string, string> selectedDevice)
                {
                    return;
                }

                int deviceIndex = -1;
                for (int i = 0; i < Devices.Count; i++)
                {
                    (DeviceType Type, string Id, string Name) device = Devices[i];
                    if (device.Type == selectedDevice.Item1 && device.Id == selectedDevice.Item2)
                    {
                        deviceIndex = i;
                        break;
                    }
                }

                if (deviceIndex < 0)
                {
                    return;
                }

                if (deviceIndex == _device)
                {
                    return;
                }

                Device = deviceIndex;
            }
        }

        public InputConfig Config { get; set; }

        public InputViewModel(UserControl owner, bool useGlobal = false) : this()
        {
            if (Program.PreviewerDetached)
            {
                _mainWindow = RyujinxApp.MainWindow;

                ReplaceKeyboardDriver(owner);
                PhysicalKeyLabelHelper.LabelsChanged += OnPhysicalKeyLabelsChanged;

                _mainWindow.InputManager.GamepadDriver.OnGamepadConnected += HandleOnGamepadConnected;
                _mainWindow.InputManager.GamepadDriver.OnGamepadDisconnected += HandleOnGamepadDisconnected;

                UseGlobalConfig = useGlobal;

                _isLoaded = false;

                RefreshAvailableDevices();

                PlayerId = PlayerIndex.Player1;
            }

            _isChangeTrackingActive = true;
        }

        public void RetargetKeyboardDriver(Control owner)
        {
            if (!Program.PreviewerDetached)
            {
                return;
            }

            ReplaceKeyboardDriver(owner);
        }

        public InputViewModel()
        {
            PlayerIndexes = [];
            Controllers = [];
            Devices = [];
            ProfilesList = [];
            VisualStick = new StickVisualizer(this);

            ControllerImage = ProControllerResource;

            PlayerIndexes.Add(new(PlayerIndex.Player1, LocaleManager.Instance[LocaleKeys.ControllerSettingsPlayer1]));
            PlayerIndexes.Add(new(PlayerIndex.Player2, LocaleManager.Instance[LocaleKeys.ControllerSettingsPlayer2]));
            PlayerIndexes.Add(new(PlayerIndex.Player3, LocaleManager.Instance[LocaleKeys.ControllerSettingsPlayer3]));
            PlayerIndexes.Add(new(PlayerIndex.Player4, LocaleManager.Instance[LocaleKeys.ControllerSettingsPlayer4]));
            PlayerIndexes.Add(new(PlayerIndex.Player5, LocaleManager.Instance[LocaleKeys.ControllerSettingsPlayer5]));
            PlayerIndexes.Add(new(PlayerIndex.Player6, LocaleManager.Instance[LocaleKeys.ControllerSettingsPlayer6]));
            PlayerIndexes.Add(new(PlayerIndex.Player7, LocaleManager.Instance[LocaleKeys.ControllerSettingsPlayer7]));
            PlayerIndexes.Add(new(PlayerIndex.Player8, LocaleManager.Instance[LocaleKeys.ControllerSettingsPlayer8]));
            PlayerIndexes.Add(new(PlayerIndex.Handheld, LocaleManager.Instance[LocaleKeys.ControllerSettingsHandheld]));
        }



        private InputConfig GetPersistedInputConfig()
        {
            if (UseGlobalConfig && Program.UseExtraConfig)
            {
                return ConfigurationState.InstanceExtra.Hid.InputConfig.Value.FirstOrDefault(inputConfig => inputConfig.PlayerIndex == _playerId);
            }

            return ConfigurationState.Instance.Hid.InputConfig.Value.FirstOrDefault(inputConfig => inputConfig.PlayerIndex == _playerId);
        }

        private void LoadConfiguration(InputConfig inputConfig = null)
        {
            Config = inputConfig ?? GetDisplayedInputConfig(GetPersistedInputConfig());
            ConfigViewModel = null;

            if (Config is StandardKeyboardInputConfig keyboardInputConfig)
            {
                ConfigViewModel = new KeyboardInputViewModel(this, new KeyboardInputConfig(keyboardInputConfig), VisualStick);
            }

            if (Config is StandardControllerInputConfig controllerInputConfig)
            {
                ConfigViewModel = new ControllerInputViewModel(this, new GamepadInputConfig(controllerInputConfig), VisualStick);
            }
        }

        private InputConfig GetDisplayedInputConfig(InputConfig persistedConfig)
        {
            if (persistedConfig is not StandardControllerInputConfig controllerConfig)
            {
                return persistedConfig;
            }

            // If runtime has already fallen back to keyboard, reflect that active config in settings
            // instead of showing the stale persisted controller config.
            InputConfig activeConfig = _mainWindow?.ViewModel.AppHost?.NpadManager?.GetPlayerInputConfigByIndex((int)_playerId);

            if (activeConfig is StandardKeyboardInputConfig)
            {
                return activeConfig;
            }

            // When no game is running (NpadManager unavailable) and the persisted controller
            // device isn't currently connected, fall back to keyboard so the user isn't
            // stuck on "Disabled".
            if (activeConfig == null &&
                !Devices.Any(device => device.Type == DeviceType.Controller && device.Id == controllerConfig.Id) &&
                TryCreateKeyboardFallbackConfig(persistedConfig, out StandardKeyboardInputConfig fallbackConfig))
            {
                return fallbackConfig;
            }

            return persistedConfig;
        }

        private void FindPairedDeviceInConfigFile()
        {
            // This function allows you to output a message about the device configuration found in the file
            // NOTE: if the configuration is found, we display the message "Waiting for controller connection",
            // but only if the id gamepad belongs to the selected player

            NotificationIsVisible =
                Config != null &&
                !Devices.Any(device => device.Id == Config.Id) &&
                Config.PlayerIndex == PlayerId;
            if (NotificationIsVisible)
            {
                if (string.IsNullOrEmpty(Config.Name))
                {
                    NotificationText = $"{LocaleManager.Instance[LocaleKeys.ControllerSettingsWaitingConnectDevice].Format("No information", Config.Id)}";
                }
                else
                {
                    NotificationText = $"{LocaleManager.Instance[LocaleKeys.ControllerSettingsWaitingConnectDevice].Format(Config.Name, Config.Id)}";
                }
            }
        }

        public void UnlinkDevice()
        {
            // "Disabled" mode is available after unbinding the device
            // NOTE: the IsModified flag to be able to apply the settings.
            NotificationIsVisible = false;
            IsModified = true;
        }

        public void LoadDevice()
        {
            int deviceIndex = 0;

            if (Config == null || Config.Backend == InputBackendType.Invalid)
            {
                ApplyLoadedDevice(deviceIndex);
                return;
            }

            DeviceType type = DeviceType.None;

            if (Config is StandardKeyboardInputConfig)
            {
                type = DeviceType.Keyboard;
            }

            if (Config is StandardControllerInputConfig)
            {
                type = DeviceType.Controller;
            }

            for (int i = 0; i < Devices.Count; i++)
            {
                if (Devices[i].Type == type && Devices[i].Id == Config.Id)
                {
                    deviceIndex = i;
                    break;
                }
            }

            ApplyLoadedDevice(deviceIndex);
        }

        private void ApplyLoadedDevice(int deviceIndex)
        {
            _device = deviceIndex is >= 0 and < int.MaxValue ? deviceIndex : 0;

            if (_device >= Devices.Count)
            {
                _device = 0;
            }

            if (_device > 0 && Devices[_device].Type != DeviceType.None)
            {
                LoadControllers();
            }

            FindPairedDeviceInConfigFile();
            OnPropertyChanged(nameof(Device));
            OnPropertyChanged(nameof(SelectedDeviceItem));
            NotifyChanges();
        }

        private void LoadSelectedDeviceDefaults()
        {
            if (_device > 0 && _device < Devices.Count && Devices[_device].Type != DeviceType.None)
            {
                LoadControllers();
            }

            LoadConfiguration(LoadDefaultConfiguration());
        }

        public void RefreshModifiedState()
        {
            if (!_isChangeTrackingActive)
            {
                return;
            }

            IsModified = !ConfigsMatch(GetSelectedDeviceConfig(), GetDisplayedInputConfig(GetPersistedInputConfig()));
        }

        private static bool ConfigsMatch(InputConfig currentConfig, InputConfig otherConfig)
        {
            if (currentConfig == null || otherConfig == null)
            {
                return currentConfig == otherConfig;
            }

            return SerializeComparableConfig(currentConfig) ==
                   SerializeComparableConfig(otherConfig);
        }

        private static string SerializeComparableConfig(InputConfig config)
        {
            InputConfig comparableConfig =
                JsonHelper.Deserialize(
                    JsonHelper.Serialize(config, _serializerContext.InputConfig),
                    _serializerContext.InputConfig);

            comparableConfig.Name = string.Empty;

            if (comparableConfig is StandardControllerInputConfig controllerConfig &&
                controllerConfig.Led is { EnableLed: false, TurnOffLed: false, UseRainbow: false, LedColor: 0 })
            {
                controllerConfig.Led = null;
            }

            return JsonHelper.Serialize(comparableConfig, _serializerContext.InputConfig);
        }

        private InputConfig GetSelectedDeviceConfig()
        {
            if (!TryGetCurrentDevice(out (DeviceType Type, string Id, string Name) device) || device.Type == DeviceType.None)
            {
                return null;
            }

            InputConfig config = device.Type switch
            {
                DeviceType.Keyboard => (ConfigViewModel as KeyboardInputViewModel)?.Config.GetConfig(),
                DeviceType.Controller => (ConfigViewModel as ControllerInputViewModel)?.Config.GetConfig(),
                _ => null,
            };

            if (config == null)
            {
                return null;
            }

            config.Id = GetConfigDeviceId(device);
            config.Name = device.Name;
            config.PlayerIndex = _playerId;
            config.ControllerType = Controllers[_controller].Type;

            return config;
        }

        private void LoadInputDriver()
        {
            if (!TryGetCurrentDevice(out (DeviceType Type, string Id, string Name) device))
            {
                SelectedGamepad = null;
                return;
            }

            string id = GetGamepadId(device);
            DeviceType type = device.Type;

            if (type == DeviceType.None)
            {
                SelectedGamepad = null;
                return;
            }

            if (type == DeviceType.Keyboard)
            {
                if (_mainWindow.InputManager.KeyboardDriver is AvaloniaKeyboardDriver)
                {
                    // NOTE: To get input in this window, we need to bind a custom keyboard driver instead of using the InputManager one as the main window isn't focused...
                    SelectedGamepad = AvaloniaKeyboardDriver.GetGamepad(id);
                }
                else
                {
                    SelectedGamepad = _mainWindow.InputManager.KeyboardDriver.GetGamepad(id);
                }
            }
            else
            {
                SelectedGamepad = _mainWindow.InputManager.GamepadDriver.GetGamepad(id);
            }
        }

        private void HandleOnGamepadDisconnected(string id)
        {
            _isChangeTrackingActive = false; // Disable configuration change tracking

            bool selectedControllerDisconnected =
                TryGetCurrentDevice(out (DeviceType Type, string Id, string Name) currentDevice) &&
                currentDevice.Type == DeviceType.Controller &&
                string.Equals(GetGamepadId(currentDevice), id, StringComparison.Ordinal);

            RefreshAvailableDevices();

            InputConfig persistedConfig = GetPersistedInputConfig();
            InputConfig displayedConfig = GetDisplayedInputConfig(persistedConfig);
            bool shouldApplyKeyboardFallback =
                selectedControllerDisconnected ||
                displayedConfig is StandardKeyboardInputConfig;

            if (shouldApplyKeyboardFallback)
            {
                if (selectedControllerDisconnected &&
                    displayedConfig is not StandardKeyboardInputConfig &&
                    TryCreateKeyboardFallbackConfig(persistedConfig, out StandardKeyboardInputConfig fallbackConfig))
                {
                    displayedConfig = fallbackConfig;
                }

                LoadConfiguration(displayedConfig);
                LoadDevice();
                LoadProfiles();
                FindPairedDeviceInConfigFile();
                IsModified = false;
                NotifyChanges();
            }
            else
            {
                IsModified = true;
                RevertChanges();
                FindPairedDeviceInConfigFile();
            }
            
            _isChangeTrackingActive = true; // Enable configuration change tracking

        }

        private async void HandleOnGamepadConnected(string id)
        {
            _isChangeTrackingActive = false; // Disable configuration change tracking

            try
            {
                InputConfig persistedConfig = GetPersistedInputConfig();
                bool shouldRestoreControllerAfterFallback =
                    Config is StandardKeyboardInputConfig &&
                    persistedConfig is StandardControllerInputConfig;

                if (shouldRestoreControllerAfterFallback)
                {
                    const int reconnectRestoreAttempts = 20;
                    const int reconnectRestoreDelayMs = 250;

                    string controllerId = persistedConfig.Id;

                    for (int attempt = 0; attempt < reconnectRestoreAttempts; attempt++)
                    {
                        RefreshAvailableDevices();

                        if (Devices.Any(device => device.Type == DeviceType.Controller && device.Id == controllerId))
                        {
                            IsModified = true;
                            RevertChanges();
                            return;
                        }

                        await Task.Delay(reconnectRestoreDelayMs);
                    }
                }

                RefreshAvailableDevices();

                IsModified = true;
                RevertChanges();
            }
            finally
            {
                _isChangeTrackingActive = true;// Enable configuration change tracking
            }
        }

        private bool TryGetCurrentDevice(out (DeviceType Type, string Id, string Name) device)
        {
            if (_device < 0 || _device >= Devices.Count)
            {
                device = default;
                return false;
            }

            device = Devices[_device];
            return true;
        }

        private static string GetGamepadId((DeviceType Type, string Id, string Name) device)
        {
            return device.Type == DeviceType.None ? null : device.Id.Split(" ")[0];
        }

        // Keyboard configs keep the full ID, while displayed controller entries include
        // the user-facing numbered suffix and need normalization before persistence/lookup.
        private static string GetConfigDeviceId((DeviceType Type, string Id, string Name) device)
        {
            return device.Type switch
            {
                DeviceType.Keyboard => device.Id,
                DeviceType.Controller => GetGamepadId(device),
                _ => null,
            };
        }

        public void LoadControllers()
        {
            Controllers.Clear();

            if (_playerId == PlayerIndex.Handheld)
            {
                Controllers.Add(new(ControllerType.Handheld, LocaleManager.Instance[LocaleKeys.ControllerSettingsControllerTypeHandheld]));

                ApplyControllerSelection(0);
            }
            else
            {
                Controllers.Add(new(ControllerType.ProController, LocaleManager.Instance[LocaleKeys.ControllerSettingsControllerTypeProController]));
                Controllers.Add(new(ControllerType.JoyconPair, LocaleManager.Instance[LocaleKeys.ControllerSettingsControllerTypeJoyConPair]));
                Controllers.Add(new(ControllerType.JoyconLeft, LocaleManager.Instance[LocaleKeys.ControllerSettingsControllerTypeJoyConLeft]));
                Controllers.Add(new(ControllerType.JoyconRight, LocaleManager.Instance[LocaleKeys.ControllerSettingsControllerTypeJoyConRight]));

                if (Config != null)
                {
                    int controllerIndex = -1;
                    for (int i = 0; i < Controllers.Count; i++)
                    {
                        if (Controllers[i].Type == Config.ControllerType)
                        {
                            controllerIndex = i;
                            break;
                        }
                    }

                    if (controllerIndex != -1)
                    {
                        // Avalonia bug: setting a newly instanced ComboBox to 0
                        // causes the selected item to show up blank.
                        // Workaround: set the box to 1 and then 0.
                        // See: https://github.com/AvaloniaUI/Avalonia/issues/4610
                        //      https://github.com/AvaloniaUI/Avalonia/discussions/18834
                        if (controllerIndex == 0)
                        {
                            ApplyControllerSelection(1);
                        }

                        ApplyControllerSelection(controllerIndex);
                    }
                }
                else
                {
                    ApplyControllerSelection(0);
                }
            }
        }

        private static string GetShortGamepadName(string str)
        {
            const string Ellipsis = "...";
            const int MaxSize = 50;

            if (str.Length > MaxSize)
            {
                return $"{str.AsSpan(0, MaxSize - Ellipsis.Length)}{Ellipsis}";
            }

            return str;
        }

        private void RefreshAvailableDevices()
        {
            int selectedDeviceIndex = 0;
            (DeviceType Type, string Id, string Name) selectedDevice = default;

            if (_device >= 0 && _device < Devices.Count)
            {
                selectedDevice = Devices[_device];
            }

            string GetGamepadName(IGamepad gamepad, int controllerNumber)
            {
                return $"{GetShortGamepadName(gamepad.Name)} ({controllerNumber})";
            }

            string GetUniqueGamepadName(IGamepad gamepad, ref int controllerNumber)
            {
                string name = GetGamepadName(gamepad, controllerNumber);
                if (Devices.Any(controller => controller.Name == name))
                {
                    controllerNumber++;
                    name = GetUniqueGamepadName(gamepad, ref controllerNumber);
                }

                return name;
            }

            lock (Devices)
            {
                Devices.Clear();
                Devices.Add((DeviceType.None, Disabled, LocaleManager.Instance[LocaleKeys.ControllerSettingsDeviceDisabled]));

                
                foreach (string id in _mainWindow.InputManager.KeyboardDriver.GamepadsIds)
                {
                    using IGamepad gamepad = _mainWindow.InputManager.KeyboardDriver.GetGamepad(id);

                    if (gamepad != null)
                    {
                        Devices.Add((DeviceType.Keyboard, id, $"{GetShortGamepadName(gamepad.Name)}"));
                    }
                }

                foreach (string id in _mainWindow.InputManager.GamepadDriver.GamepadsIds)
                {
                    using IGamepad gamepad = _mainWindow.InputManager.GamepadDriver.GetGamepad(id);

                    if (gamepad != null)
                    {
                        int controllerNumber = 0;
                        string name = GetUniqueGamepadName(gamepad, ref controllerNumber);
                        Devices.Add((DeviceType.Controller, id, name));
                    }
                }

                if (selectedDevice != default)
                {
                    selectedDeviceIndex = -1;
                    for (int i = 0; i < Devices.Count; i++)
                    {
                        (DeviceType Type, string Id, string Name) device = Devices[i];
                        if (device.Type == selectedDevice.Type && device.Id == selectedDevice.Id)
                        {
                            selectedDeviceIndex = i;
                            break;
                        }
                    }
                }

                if (selectedDeviceIndex < 0)
                {
                    selectedDeviceIndex = Math.Clamp(_device, 0, Devices.Count - 1);
                }
            }

            ApplyLoadedDevice(selectedDeviceIndex);
        }

        private string GetProfileBasePath()
        {
            string path = AppDataManager.ProfilesDirPath;
            DeviceType type = Devices[Device == -1 ? 0 : Device].Type;

            if (type == DeviceType.Keyboard)
            {
                path = Path.Combine(path, KeyboardString);
            }
            else if (type == DeviceType.Controller)
            {
                path = Path.Combine(path, ControllerString);
            }

            return path;
        }

        private void LoadProfiles()
        {
            ProfilesList.Clear();

            string basePath = GetProfileBasePath();

            if (!Directory.Exists(basePath))
            {
                Directory.CreateDirectory(basePath);
            }

            ProfilesList.Add((LocaleManager.Instance[LocaleKeys.ControllerSettingsProfileDefault]));

            foreach (string profile in Directory.GetFiles(basePath, "*.json", SearchOption.AllDirectories))
            {
                ProfilesList.Add(Path.GetFileNameWithoutExtension(profile));
            }

            if (string.IsNullOrWhiteSpace(ProfileName))
            {
                ProfileName = LocaleManager.Instance[LocaleKeys.ControllerSettingsProfileDefault];
            }
        }

        public InputConfig LoadDefaultConfiguration()
        {
            (DeviceType Type, string Id, string Name) activeDevice = Devices.FirstOrDefault();

            if (Devices.Count > 0 && Device < Devices.Count && Device >= 0)
            {
                activeDevice = Devices[Device];
            }

            InputConfig config;
            if (activeDevice.Type == DeviceType.Keyboard)
            {
                config = InputConfigDefaults.CreateDefaultKeyboardConfiguration(
                    activeDevice.Id,
                    activeDevice.Name,
                    ControllerType.ProController,
                    _playerId);
            }
            else if (activeDevice.Type == DeviceType.Controller)
            {
                string id = activeDevice.Id.Split(" ")[0];
                string name = activeDevice.Name;

                bool isNintendoStyle = false;

                try
                {
                    IGamepad gp = _mainWindow?.InputManager?.GamepadDriver?.GetGamepad(id);

                    if (gp is SDL3Gamepad sdlGp)
                    {
                        // Nintendo vendor ID is 0x057E
                        isNintendoStyle = sdlGp.VendorId == 0x057E;
                    }
                    else
                    {
                        // Fallback to name-based detection
                        isNintendoStyle = name.Contains("Nintendo", StringComparison.OrdinalIgnoreCase);
                    }
                }
                catch (Exception ex)
                {
                    Logger.Debug?.Print(LogClass.UI, $"Controller vendor detection failed for '{name}': {ex.Message}");
                    isNintendoStyle = name.Contains("Nintendo", StringComparison.OrdinalIgnoreCase);
                }

                config = InputConfigDefaults.CreateDefaultControllerConfiguration(
                    id,
                    name,
                    ControllerType.ProController,
                    _playerId,
                    isNintendoStyle);
            }
            else
            {
                config = new InputConfig();
            }

            config.PlayerIndex = _playerId;

            return config;
        }

        private bool TryCreateKeyboardFallbackConfig(InputConfig sourceConfig, out StandardKeyboardInputConfig fallbackConfig)
        {
            fallbackConfig = null;

            (DeviceType Type, string Id, string Name) keyboardDevice =
                Devices.FirstOrDefault(device => device.Type == DeviceType.Keyboard);

            if (keyboardDevice == default)
            {
                return false;
            }

            ControllerType controllerType = sourceConfig?.ControllerType ?? ControllerType.ProController;
            PlayerIndex playerIndex = sourceConfig?.PlayerIndex ?? _playerId;

            fallbackConfig = InputConfigDefaults.CreateDefaultKeyboardConfiguration(
                keyboardDevice.Id,
                keyboardDevice.Name,
                controllerType,
                playerIndex);
            return true;
        }

        public void LoadProfileButton()
        {
            LoadProfile();
        }

        public async void LoadProfile()
        {
            if (Device == 0)
            {
                return;
            }

            InputConfig config = null;

            if (string.IsNullOrWhiteSpace(ProfileName))
            {
                return;
            }

            if (ProfileName == LocaleManager.Instance[LocaleKeys.ControllerSettingsProfileDefault])
            {
                config = LoadDefaultConfiguration();
            }
            else
            {
                string path = Path.Combine(GetProfileBasePath(), ProfileName + ".json");

                if (!File.Exists(path))
                {
                    int index = ProfilesList.IndexOf(ProfileName);
                    if (index != -1)
                    {
                        ProfilesList.RemoveAt(index);
                    }

                    return;
                }

                try
                {
                    config = JsonHelper.DeserializeFromFile(path, _serializerContext.InputConfig);
                }
                catch (JsonException) { }
                catch (InvalidOperationException)
                {
                    Logger.Error?.Print(LogClass.Configuration, $"Profile {ProfileName} is incompatible with the current input configuration system.");

                    await ContentDialogHelper.CreateErrorDialog(LocaleManager.Instance.UpdateAndGetDynamicValue(LocaleKeys.DialogProfileInvalidProfileErrorMessage, ProfileName));

                    return;
                }
            }

            if (config != null)
            {
                _isLoaded = false;

                string currentDeviceId = Config?.Id ??
                    (TryGetCurrentDevice(out (DeviceType Type, string Id, string Name) currentDevice)
                        ? GetConfigDeviceId(currentDevice)
                        : null);
                if (string.IsNullOrEmpty(currentDeviceId))
                {
                    Logger.Warning?.Print(LogClass.Configuration, $"Ignoring profile load for {ProfileName} because no active input device is selected.");
                    return;
                }

                config.Id = currentDeviceId; // Set current device id instead of changing device(independent profiles)

                LoadConfiguration(config);

                //LoadDevice();  This line of code hard-links profiles to controllers, the commented line allows profiles to be applied to all controllers 

                _isLoaded = true;

                RefreshModifiedState();
                NotifyChanges();
            }
        }

        public async void SaveProfile()
        {

            if (Device == 0)
            {
                return;
            }

            if (ConfigViewModel == null)
            {
                return;
            }

            if (ProfileName == LocaleManager.Instance[LocaleKeys.ControllerSettingsProfileDefault])
            {
                await ContentDialogHelper.CreateErrorDialog(LocaleManager.Instance[LocaleKeys.DialogProfileDefaultProfileOverwriteErrorMessage]);

                return;
            }
            else
            {
                bool validFileName = ProfileName.IndexOfAny(Path.GetInvalidFileNameChars()) == -1;

                if (validFileName)
                {
                    string path = Path.Combine(GetProfileBasePath(), ProfileName + ".json");

                    InputConfig config = null;

                    if (IsKeyboard)
                    {
                        config = (ConfigViewModel as KeyboardInputViewModel).Config.GetConfig();
                    }
                    else if (IsController)
                    {
                        config = (ConfigViewModel as ControllerInputViewModel).Config.GetConfig();
                    }

                    config.ControllerType = Controllers[_controller].Type;

                    string jsonString = JsonHelper.Serialize(config, _serializerContext.InputConfig);

                    await File.WriteAllTextAsync(path, jsonString);

                    LoadProfiles();

                    ChosenProfile = ProfileName; // Show new profile
                }
                else
                {
                    await ContentDialogHelper.CreateErrorDialog(LocaleManager.Instance[LocaleKeys.DialogProfileInvalidProfileNameErrorMessage]);
                }
            }
        }

        public async void RemoveProfile()
        {
            if (Device == 0 || ProfileName == LocaleManager.Instance[LocaleKeys.ControllerSettingsProfileDefault] || ProfilesList.IndexOf(ProfileName) == -1)
            {
                return;
            }

            UserResult result = await ContentDialogHelper.CreateConfirmationDialog(
                LocaleManager.Instance[LocaleKeys.DialogProfileDeleteProfileTitle],
                LocaleManager.Instance[LocaleKeys.DialogProfileDeleteProfileMessage],
                LocaleManager.Instance[LocaleKeys.InputDialogYes],
                LocaleManager.Instance[LocaleKeys.InputDialogNo],
                LocaleManager.Instance[LocaleKeys.RyujinxConfirm]);

            if (result == UserResult.Yes)
            {
                string path = Path.Combine(GetProfileBasePath(), ProfileName + ".json");

                if (File.Exists(path))
                {
                    File.Delete(path);
                }

                LoadProfiles();

                ChosenProfile = ProfilesList[0].ToString(); // Show default profile
            }
        }

        public void RevertChanges()
        {
            _isLoaded = false;
            LoadConfiguration();
            LoadDevice();
            _isLoaded = true;

            OnPropertyChanged();
            IsModified = false;
        }

        public void Save()
        {

            if (!IsModified)
            {
                return; //If the input settings were not touched, then do nothing
            }

            IsModified = false;

            List<InputConfig> newConfig = [];

            if (UseGlobalConfig && Program.UseExtraConfig)
            {
                newConfig.AddRange(ConfigurationState.InstanceExtra.Hid.InputConfig.Value);
            }
            else
            {
                newConfig.AddRange(ConfigurationState.Instance.Hid.InputConfig.Value);
            }

            newConfig.RemoveAll(static inputConfig => inputConfig == null);

            if (Device == 0)
            {
                newConfig.RemoveAll(inputConfig => inputConfig.PlayerIndex == PlayerId);
            }
            else
            {
                InputConfig config = GetSelectedDeviceConfig();

                int i = newConfig.FindIndex(x => x.PlayerIndex == PlayerId);
                if (i == -1)
                {
                    newConfig.Add(config);
                }
                else
                {
                    newConfig[i] = config;
                }
            }

            // Atomically replace and signal input change.
            // NOTE: Do not modify InputConfig.Value directly as other code depends on the on-change event.
            _mainWindow.ViewModel.AppHost?.NpadManager.ReloadConfiguration(newConfig, ConfigurationState.Instance.Hid.EnableKeyboard, ConfigurationState.Instance.Hid.EnableMouse);

            if (UseGlobalConfig && Program.UseExtraConfig)
            {
                // In User Settings when "Use Global Input" is enabled, it saves global input to global setting
                ConfigurationState.InstanceExtra.Hid.InputConfig.Value = newConfig;
                ConfigurationState.InstanceExtra.ToFileFormat().SaveConfig(Program.GlobalConfigurationPath);
            }
            else
            {
                ConfigurationState.Instance.Hid.InputConfig.Value = newConfig;
                ConfigurationState.Instance.ToFileFormat().SaveConfig(Program.ConfigurationPath);
            }
        }

        public void NotifyChanges()
        {
            OnPropertyChanged(nameof(ConfigViewModel));
            OnPropertyChanged(nameof(IsController));
            OnPropertyChanged(nameof(ShowSettings));
            OnPropertyChanged(nameof(IsKeyboard));
            OnPropertyChanged(nameof(IsRight));
            OnPropertyChanged(nameof(IsLeft));
            NotifyChangesEvent?.Invoke();
        }

        private void OnPhysicalKeyLabelsChanged()
        {
            if (ConfigViewModel is KeyboardInputViewModel keyboardInputViewModel)
            {
                Dispatcher.UIThread.Post(keyboardInputViewModel.Config.NotifyKeyLabelsChanged);
            }
        }

        private void ReplaceKeyboardDriver(Control owner)
        {
            Control target = TopLevel.GetTopLevel(owner) as Control ?? owner;

            if (ReferenceEquals(_keyboardDriverControl, target))
            {
                return;
            }

            if (AvaloniaKeyboardDriver is AvaloniaKeyboardDriver oldKeyboardDriver)
            {
                oldKeyboardDriver.KeyPressed -= PhysicalKeyLabelHelper.ObserveKeyPress;
                oldKeyboardDriver.Dispose();
            }

            _keyboardDriverControl = target;

            AvaloniaKeyboardDriver keyboardDriver = new(target, KeyboardInputMode.Physical);
            keyboardDriver.KeyPressed += PhysicalKeyLabelHelper.ObserveKeyPress;
            AvaloniaKeyboardDriver = keyboardDriver;

            if (_isLoaded && Device > 0 && Device < Devices.Count && Devices[Device].Type == DeviceType.Keyboard)
            {
                SelectedGamepad?.Dispose();
                LoadInputDriver();
            }
        }

        public void Dispose()
        {
            GC.SuppressFinalize(this);
            PhysicalKeyLabelHelper.LabelsChanged -= OnPhysicalKeyLabelsChanged;

            _mainWindow.InputManager.GamepadDriver.OnGamepadConnected -= HandleOnGamepadConnected;
            _mainWindow.InputManager.GamepadDriver.OnGamepadDisconnected -= HandleOnGamepadDisconnected;

            VisualStick.Dispose();

            SelectedGamepad?.Dispose();

            AvaloniaKeyboardDriver?.Dispose();
        }
    }
}
