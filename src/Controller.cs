using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Win32;

internal enum ChildKind
{
	Player,
	Pair,
	Native
}

internal sealed class Controller : IDisposable
{
	private const string ExpectedExeSha256 = "19054310d0cb4de3a014b0df1eea98ec0dc7f3b3e4e04a3ae7d4235d36a80261";

	private static readonly Choice[] PerformancePresetChoices =
	{
		new Choice("lowlatency", "Low latency"),
		new Choice("balanced", "Balanced"),
		new Choice("cinema", "Cinema")
	};
	private static readonly Choice[] SimpleQualityChoices =
	{
		new Choice("1080p", "1080p, sharpest"),
		new Choice("720p", "720p, balanced"),
		new Choice("540p", "540p, lower bandwidth"),
		new Choice("360p", "360p, lowest bandwidth")
	};

	[DllImport("dwmapi.dll")]
	private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

	private readonly Window _win;
	private readonly string _backendExe;
	private readonly string _backendDir;

	private readonly TabControl _pages;
	private readonly TabItem _sessionTab;
	private readonly TabItem _streamTab;
	private readonly TabItem _pictureTab;
	private readonly TabItem _soundTab;
	private readonly TabItem _controlsTab;
	private readonly Border _quickSetupCard;
	private readonly Border _advancedSummary;
	private readonly Button _modeToggle;
	private readonly TextBlock _modeCaption;

	private readonly TextBlock _title;
	private readonly TextBlock _detail;
	private readonly TextBlock _sessionState;
	private readonly TextBlock _notice;
	private readonly ComboBox _consoleList;
	private readonly Button _primary;
	private readonly Button _manage;
	private readonly Button _native;
	private readonly CheckBox _vsync;
	private readonly ComboBox _resolution;
	private readonly ComboBox _rate;
	private readonly ComboBox _codec;
	private readonly ComboBox _decoder;
	private readonly ComboBox _renderer;
	private readonly ComboBox _scaling;
	private readonly ComboBox _mixer;
	private readonly ComboBox _windowMode;
	private readonly TextBox _bitrate;
	private readonly TextBox _volume;
	private readonly TextBox _audioBuffer;
	private readonly Button _apply;
	private readonly Button _revert;
	private readonly TextBlock _sumResolution;
	private readonly TextBlock _sumRate;
	private readonly TextBlock _sumBitrate;
	private readonly TextBlock _sumDecoder;
	private readonly TextBlock _sumScaling;
	private readonly TextBlock _sumBuffer;
	private readonly LinkPanel _linkPanel;
	private readonly CheckBox _keyboardEnabled;
	private readonly ComboBox _walkKey;
	private readonly TextBox _walkScale;
	private readonly CheckBox _mouseAim;
	private readonly TextBox _aimSensitivity;
	private readonly TextBox _aimYRatio;
	private readonly TextBox _aimDeadzone;
	private readonly TextBox _aimCurve;
	private readonly TextBox _aimWindow;
	private readonly TextBox _aimTau;
	private readonly ComboBox _aimRelease;
	private readonly TextBox _colorSat;
	private readonly TextBox _colorVib;
	private readonly TextBox _colorCon;
	private readonly TextBox _colorBri;
	private readonly TextBox _colorTemp;
	private readonly TextBox _colorTint;
	private readonly TextBox _hapticOverride;
	private readonly ComboBox _rumbleIntensity;
	private readonly CheckBox _padBackground;
	private readonly ComboBox _performancePreset;
	private readonly ComboBox _simpleQuality;
	private readonly CheckBox _simpleMouseAim;
	private readonly TextBox _simpleAimSensitivity;
	private readonly TextBox _simpleVolume;
	private readonly Button _aimAdvancedToggle;
	private readonly StackPanel _aimAdvancedPanel;
	private readonly UniformGrid _keyMapGrid;
	private readonly AimPad _aimPad;
	private readonly GamepadNav _nav;
	private readonly FrameworkElement _linkDetails;
	private readonly FrameworkElement _draftBadge;
	private readonly TextBlock _draftCount;
	private readonly TextBlock _padHint;
	private readonly List<Control> _settingControls = new List<Control>();
	private readonly List<Control> _aimControls = new List<Control>();
	private readonly Dictionary<string, Control> _fieldControls = new Dictionary<string, Control>();
	private readonly DispatcherTimer _watch = new DispatcherTimer();
	private CancellationTokenSource _linkCheck;

	private readonly List<ConsoleEntry> _consoles = new List<ConsoleEntry>();
	private Dictionary<string, string> _saved;
	private int _selectedIndex;
	private Process _child;
	private ChildKind _childKind;
	private bool _closeRequested;
	private bool _closing;
	private bool _minimizedForChild;
	private bool _everConnected;
	private bool _readFailed;
	private bool _settingsFailed;
	private bool _loading = true;
	private bool _syncingConsoles;
	private bool _advancedMode;
	private bool _syncingSimple;
	private bool _performanceHasCustom;
	private bool _qualityHasCustom;
	private string _runningNotice;
	private int? _lastExitCode;
	private ChildKind _lastExitKind;

	public Controller(Window win)
	{
		_win = win;
		SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(Dispatcher.CurrentDispatcher));
		_backendDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "runtime", "chiaki-ng-Win");
		_backendExe = Path.Combine(_backendDir, "chiaki.exe");

		_pages = Part<TabControl>("Pages");
		if (_pages.Items.Count != 5)
			throw new InvalidOperationException("Workbench.xaml 'Pages' must hold 5 tabs, found " + _pages.Items.Count.ToString(CultureInfo.InvariantCulture) + ".");
		_sessionTab = (TabItem)_pages.Items[0];
		_streamTab = (TabItem)_pages.Items[1];
		_pictureTab = (TabItem)_pages.Items[2];
		_soundTab = (TabItem)_pages.Items[3];
		_controlsTab = (TabItem)_pages.Items[4];
		_quickSetupCard = Part<Border>("QuickSetupCard");
		_advancedSummary = Part<Border>("AdvancedSummary");
		_modeToggle = Part<Button>("ModeToggle");
		_modeCaption = Part<TextBlock>("ModeCaption");

		_title = Part<TextBlock>("SessionTitle");
		_detail = Part<TextBlock>("SessionDetail");
		_sessionState = Part<TextBlock>("SessionState");
		_notice = Part<TextBlock>("Notice");
		_consoleList = Part<ComboBox>("ConsoleList");
		_primary = Part<Button>("Primary");
		_manage = Part<Button>("Manage");
		_native = Part<Button>("NativeSettings");
		_vsync = Part<CheckBox>("Vsync");
		_resolution = Part<ComboBox>("Resolution");
		_rate = Part<ComboBox>("Rate");
		_codec = Part<ComboBox>("Codec");
		_decoder = Part<ComboBox>("Decoder");
		_renderer = Part<ComboBox>("Renderer");
		_scaling = Part<ComboBox>("Scaling");
		_mixer = Part<ComboBox>("Mixer");
		_windowMode = Part<ComboBox>("WindowMode");
		_bitrate = Part<TextBox>("Bitrate");
		_volume = Part<TextBox>("Volume");
		_audioBuffer = Part<TextBox>("AudioBuffer");
		_apply = Part<Button>("Apply");
		_revert = Part<Button>("Revert");
		_sumResolution = Part<TextBlock>("SumResolution");
		_sumRate = Part<TextBlock>("SumRate");
		_sumBitrate = Part<TextBlock>("SumBitrate");
		_sumDecoder = Part<TextBlock>("SumDecoder");
		_sumScaling = Part<TextBlock>("SumScaling");
		_sumBuffer = Part<TextBlock>("SumBuffer");
		_linkPanel = new LinkPanel(
			Part<Canvas>("LinkTrace"),
			Part<TextBlock>("LinkKind"), Part<TextBlock>("LinkSignal"), Part<TextBlock>("LinkReceive"),
			Part<TextBlock>("LinkMedian"), Part<TextBlock>("LinkP95"), Part<TextBlock>("LinkSpikes"),
			Part<TextBlock>("LinkNotes"),
			Part<Button>("CheckLink"), Part<Button>("UseLinkBitrate"));
		_keyboardEnabled = Part<CheckBox>("KeyboardEnabled");
		_walkKey = Part<ComboBox>("WalkKey");
		_walkScale = Part<TextBox>("WalkScale");
		_mouseAim = Part<CheckBox>("MouseAim");
		_aimSensitivity = Part<TextBox>("AimSensitivity");
		_aimYRatio = Part<TextBox>("AimYRatio");
		_aimDeadzone = Part<TextBox>("AimDeadzone");
		_aimCurve = Part<TextBox>("AimCurve");
		_aimWindow = Part<TextBox>("AimWindow");
		_aimTau = Part<TextBox>("AimTau");
		_aimRelease = Part<ComboBox>("AimRelease");
		_colorSat = Part<TextBox>("ColorSat");
		_colorVib = Part<TextBox>("ColorVib");
		_colorCon = Part<TextBox>("ColorCon");
		_colorBri = Part<TextBox>("ColorBri");
		_colorTemp = Part<TextBox>("ColorTemp");
		_colorTint = Part<TextBox>("ColorTint");
		_hapticOverride = Part<TextBox>("HapticOverride");
		_rumbleIntensity = Part<ComboBox>("RumbleIntensity");
		_padBackground = Part<CheckBox>("PadBackground");
		_performancePreset = Part<ComboBox>("PerformancePreset");
		_simpleQuality = Part<ComboBox>("SimpleQuality");
		_simpleMouseAim = Part<CheckBox>("SimpleMouseAim");
		_simpleAimSensitivity = Part<TextBox>("SimpleAimSensitivity");
		_simpleVolume = Part<TextBox>("SimpleVolume");
		_aimAdvancedToggle = Part<Button>("AimAdvancedToggle");
		_aimAdvancedPanel = Part<StackPanel>("AimAdvancedPanel");
		_keyMapGrid = Part<UniformGrid>("KeyMapGrid");
		_aimPad = new AimPad(_win,
			Part<Canvas>("ResponseGraph"), Part<Canvas>("AimPad"),
			Part<TextBlock>("AimSpeed"), Part<TextBlock>("AimStick"));
		_linkDetails = Part<FrameworkElement>("LinkDetails");
		_draftBadge = Part<FrameworkElement>("DraftBadge");
		_draftCount = Part<TextBlock>("DraftCount");
		_padHint = Part<TextBlock>("PadHint");

		_settingControls.AddRange(new Control[] { _vsync, _resolution, _rate, _codec, _decoder, _renderer, _scaling, _mixer, _windowMode, _bitrate, _volume, _audioBuffer, _keyboardEnabled, _walkKey, _walkScale, _mouseAim, _colorSat, _colorVib, _colorCon, _colorBri, _colorTemp, _colorTint, _hapticOverride, _rumbleIntensity, _padBackground, _performancePreset, _simpleQuality, _simpleMouseAim, _simpleVolume });
		_aimControls.AddRange(new Control[] { _aimSensitivity, _aimYRatio, _aimDeadzone, _aimCurve, _aimWindow, _aimTau, _aimRelease, _simpleAimSensitivity });
		_fieldControls["resolution"] = _resolution;
		_fieldControls["rate"] = _rate;
		_fieldControls["bitrate"] = _bitrate;
		_fieldControls["codec"] = _codec;
		_fieldControls["decoder"] = _decoder;
		_fieldControls["renderer"] = _renderer;
		_fieldControls["window"] = _windowMode;
		_fieldControls["vsync"] = _vsync;
		_fieldControls["volume"] = _volume;
		_fieldControls["buffer"] = _audioBuffer;
		_fieldControls["scaling"] = _scaling;
		_fieldControls["mixer"] = _mixer;
		_fieldControls["keyboard"] = _keyboardEnabled;
		_fieldControls["walkkey"] = _walkKey;
		_fieldControls["walkscale"] = _walkScale;
		_fieldControls["mouseaim"] = _mouseAim;
		_fieldControls["aimsens"] = _aimSensitivity;
		_fieldControls["aimyratio"] = _aimYRatio;
		_fieldControls["aimdeadzone"] = _aimDeadzone;
		_fieldControls["aimcurve"] = _aimCurve;
		_fieldControls["aimwindow"] = _aimWindow;
		_fieldControls["aimtau"] = _aimTau;
		_fieldControls["aimrelease"] = _aimRelease;
		_fieldControls["haptics"] = _hapticOverride;
		_fieldControls["rumble"] = _rumbleIntensity;
		_fieldControls["padbackground"] = _padBackground;
		_fieldControls["colorsat"] = _colorSat;
		_fieldControls["colorvib"] = _colorVib;
		_fieldControls["colorcon"] = _colorCon;
		_fieldControls["colorbri"] = _colorBri;
		_fieldControls["colortemp"] = _colorTemp;
		_fieldControls["colortint"] = _colorTint;

		ShowCombo(_performancePreset, PerformancePresetChoices, "balanced");
		_performanceHasCustom = false;
		ShowCombo(_simpleQuality, SimpleQualityChoices, "1080p");
		_qualityHasCustom = false;
		_advancedMode = ReadAdvancedModePreference();
		ApplyPresentationMode();

		_win.ResizeMode = ResizeMode.CanResize;
		_win.Closing += OnWindowClosing;
		_win.SourceInitialized += delegate
		{
			IntPtr hwnd = new WindowInteropHelper(_win).Handle;
			int dark = 1;
			DwmSetWindowAttribute(hwnd, 20, ref dark, 4);
			int caption = 0x00101113;
			DwmSetWindowAttribute(hwnd, 35, ref caption, 4);
		};

		_primary.Click += delegate { OnPrimaryClick(); };
		_manage.Click += delegate { StartSetup(ChildKind.Pair); };
		_native.Click += delegate { StartSetup(ChildKind.Native); };
		_apply.Click += delegate { OnApply(); };
		_revert.Click += delegate { OnRevert(); };
		_modeToggle.Click += delegate { ToggleAdvancedMode(); };
		_aimAdvancedToggle.Click += delegate { ToggleAimAdvanced(); };
		Part<Button>("CheckLink").Click += delegate { OnCheckLink(); };
		Part<Button>("UseLinkBitrate").Click += delegate { ApplyLinkSuggestion(); };
		Part<Button>("EditKeyMap").Click += delegate { StartSetup(ChildKind.Native); };
		_consoleList.SelectionChanged += delegate
		{
			if (_syncingConsoles || _consoleList.SelectedIndex < 0)
				return;
			_selectedIndex = _consoleList.SelectedIndex;
			ConsoleEntry selected = SelectedConsole();
			if (selected != null && !ChildRunning() && !_readFailed)
				_title.Text = selected.Name;
		};

		ComboBox[] combos = { _resolution, _rate, _codec, _decoder, _renderer, _scaling, _mixer, _windowMode, _walkKey, _aimRelease, _rumbleIntensity };
		foreach (ComboBox combo in combos)
			combo.SelectionChanged += delegate { OnDraftChanged(); };
		TextBox[] boxes = { _bitrate, _volume, _audioBuffer, _walkScale, _aimSensitivity, _aimYRatio, _aimDeadzone, _aimCurve, _aimWindow, _aimTau, _colorSat, _colorVib, _colorCon, _colorBri, _colorTemp, _colorTint, _hapticOverride };
		foreach (TextBox box in boxes)
			box.TextChanged += delegate { OnDraftChanged(); };
		_vsync.Checked += delegate { OnDraftChanged(); };
		_vsync.Unchecked += delegate { OnDraftChanged(); };
		_keyboardEnabled.Checked += delegate { OnDraftChanged(); };
		_keyboardEnabled.Unchecked += delegate { OnDraftChanged(); };
		_mouseAim.Checked += delegate { OnDraftChanged(); };
		_mouseAim.Unchecked += delegate { OnDraftChanged(); };
		_padBackground.Checked += delegate { OnDraftChanged(); };
		_padBackground.Unchecked += delegate { OnDraftChanged(); };

		_performancePreset.SelectionChanged += delegate { OnSimplePresetChanged(); };
		_simpleQuality.SelectionChanged += delegate { OnSimpleQualityChanged(); };
		_simpleMouseAim.Checked += delegate { OnSimpleMouseAimChanged(); };
		_simpleMouseAim.Unchecked += delegate { OnSimpleMouseAimChanged(); };
		_simpleAimSensitivity.TextChanged += delegate { CopySimpleText(_simpleAimSensitivity, _aimSensitivity); };
		_simpleVolume.TextChanged += delegate { CopySimpleText(_simpleVolume, _volume); };

		_nav = new GamepadNav(_win, _pages,
			delegate { return _primary; },
			delegate { return _aimPad.IsCapturing; },
			delegate(bool connected) { _padHint.Visibility = connected ? Visibility.Visible : Visibility.Collapsed; });

		_watch.Interval = TimeSpan.FromMilliseconds(400);
		_watch.Tick += delegate
		{
			if (_child != null && _child.HasExited)
				RefreshState();
		};

		_notice.Text = "Changes stay a draft until you save. Saved settings are used the next time you connect.";
		_win.Loaded += delegate
		{
			_loading = false;
			RefreshState();
			_primary.Focus();
		};
	}

	public void Dispose()
	{
		_watch.Stop();
		_nav.Dispose();
		if (_linkCheck != null)
			_linkCheck.Cancel();
		if (_child != null)
			_child.Dispose();
		if (_aimPad != null)
			_aimPad.Dispose();
	}

	private T Part<T>(string name) where T : class
	{
		object found = _win.FindName(name);
		if (found == null)
			throw new InvalidOperationException("Workbench.xaml is missing the element '" + name + "' (" + typeof(T).Name + ").");
		T typed = found as T;
		if (typed == null)
			throw new InvalidOperationException("Workbench.xaml element '" + name + "' must be a " + typeof(T).Name + " but is a " + found.GetType().Name + ".");
		return typed;
	}

	private static bool ReadAdvancedModePreference()
	{
		try
		{
			using (RegistryKey root = Registry.CurrentUser.OpenSubKey(StreamSettings.RegistryPath, false))
			{
				if (root == null)
					return false;
				string value = StreamSettings.Str(root.GetValue("ui_advanced_mode", null));
				return value == "true" || value == "1";
			}
		}
		catch (Exception)
		{
			return false;
		}
	}

	private static void WriteAdvancedModePreference(bool advanced)
	{
		using (RegistryKey root = Registry.CurrentUser.CreateSubKey(StreamSettings.RegistryPath, RegistryKeyPermissionCheck.ReadWriteSubTree))
			root.SetValue("ui_advanced_mode", advanced ? "true" : "false", RegistryValueKind.String);
	}

	private void ToggleAdvancedMode()
	{
		_advancedMode = !_advancedMode;
		ApplyPresentationMode();
		try
		{
			WriteAdvancedModePreference(_advancedMode);
		}
		catch (Exception)
		{
			SetNotice("The view changed, but the preference could not be saved for the next launch.");
		}
	}

	private void ApplyPresentationMode()
	{
		if (_advancedMode)
		{
			EnsureTab(1, _streamTab);
			EnsureTab(2, _pictureTab);
			EnsureTab(3, _soundTab);
			EnsureTab(4, _controlsTab);
		}
		else
		{
			RemoveTab(_controlsTab);
			RemoveTab(_soundTab);
			RemoveTab(_pictureTab);
			RemoveTab(_streamTab);
			if (!ReferenceEquals(_pages.SelectedItem, _sessionTab))
				_pages.SelectedItem = _sessionTab;
		}
		_quickSetupCard.Visibility = _advancedMode ? Visibility.Collapsed : Visibility.Visible;
		_advancedSummary.Visibility = _advancedMode ? Visibility.Visible : Visibility.Collapsed;
		_modeToggle.Content = _advancedMode ? "Simple view" : "All settings";
		_modeCaption.Text = _advancedMode ? "All settings" : "Essentials";
	}

	private void EnsureTab(int index, TabItem item)
	{
		if (_pages.Items.Contains(item))
			return;
		if (index > _pages.Items.Count)
			index = _pages.Items.Count;
		_pages.Items.Insert(index, item);
	}

	private void RemoveTab(TabItem item)
	{
		if (_pages.Items.Contains(item))
			_pages.Items.Remove(item);
	}

	private static void SelectComboValue(ComboBox combo, string value)
	{
		foreach (object item in combo.Items)
		{
			Choice choice = item as Choice;
			if (choice != null && choice.Value == value)
			{
				if (!ReferenceEquals(combo.SelectedItem, item))
					combo.SelectedItem = item;
				return;
			}
		}
	}

	private void EnsurePerformanceItems(bool includeCustom)
	{
		if (_performanceHasCustom == includeCustom)
			return;
		_performanceHasCustom = includeCustom;
		List<Choice> items = new List<Choice>();
		if (includeCustom)
			items.Add(new Choice("custom", "Custom"));
		items.AddRange(PerformancePresetChoices);
		_performancePreset.ItemsSource = items;
	}

	private void EnsureQualityItems(bool includeCustom)
	{
		if (_qualityHasCustom == includeCustom)
			return;
		_qualityHasCustom = includeCustom;
		List<Choice> items = new List<Choice>();
		if (includeCustom)
			items.Add(new Choice("custom", "Custom resolution"));
		items.AddRange(SimpleQualityChoices);
		_simpleQuality.ItemsSource = items;
	}

	private bool IsSimpleQuality(string resolution)
	{
		foreach (Choice choice in SimpleQualityChoices)
			if (choice.Value == resolution)
				return true;
		return false;
	}

	private string DetectPerformancePreset()
	{
		string codec = ComboValue(_codec);
		if (ComboValue(_rate) != "60" || (codec != "h265" && codec != "h265_hdr") || ComboValue(_mixer) != "direct"
			|| ComboValue(_decoder) != "vulkan" || ComboValue(_renderer) != "vulkan")
			return "custom";
		string scaling = ComboValue(_scaling);
		string bitrate = _bitrate.Text.Trim();
		string buffer = _audioBuffer.Text.Trim();
		if (scaling == "preset_high_quality" && _vsync.IsChecked != true && buffer == "20" && bitrate == StreamSettings.DefaultBitrateForResolution(ComboValue(_resolution)))
			return "lowlatency";
		if (scaling == "ewa_lanczossharp" && _vsync.IsChecked != true && buffer == "20" && bitrate == "32000")
			return "balanced";
		if (scaling == "preset_high_quality_advanced_spatial" && _vsync.IsChecked != true && buffer == "30" && bitrate == "45000")
			return "cinema";
		return "custom";
	}

	private void SyncSimpleFromCanonical()
	{
		if (_syncingSimple)
			return;
		_syncingSimple = true;
		try
		{
			string preset = DetectPerformancePreset();
			EnsurePerformanceItems(preset == "custom");
			SelectComboValue(_performancePreset, preset);

			string resolution = ComboValue(_resolution);
			bool knownQuality = IsSimpleQuality(resolution);
			EnsureQualityItems(!knownQuality);
			SelectComboValue(_simpleQuality, knownQuality ? resolution : "custom");

			if (_simpleMouseAim.IsChecked != _mouseAim.IsChecked)
				_simpleMouseAim.IsChecked = _mouseAim.IsChecked;
			if (_simpleAimSensitivity.Text != _aimSensitivity.Text)
				_simpleAimSensitivity.Text = _aimSensitivity.Text;
			if (_simpleVolume.Text != _volume.Text)
				_simpleVolume.Text = _volume.Text;
		}
		finally
		{
			_syncingSimple = false;
		}
	}

	private void OnSimplePresetChanged()
	{
		if (_loading || _syncingSimple)
			return;
		string preset = ComboValue(_performancePreset);
		if (preset == "custom" || preset.Length == 0)
			return;
		_syncingSimple = true;
		try
		{
			SelectComboValue(_rate, "60");
			string codec = ComboValue(_codec);
			if (codec != "h265" && codec != "h265_hdr")
				SelectComboValue(_codec, "h265");
			SelectComboValue(_decoder, "vulkan");
			SelectComboValue(_renderer, "vulkan");
			SelectComboValue(_mixer, "direct");
			if (preset == "lowlatency")
			{
				SelectComboValue(_scaling, "preset_high_quality");
				_vsync.IsChecked = false;
				_audioBuffer.Text = "20";
				string bitrate = StreamSettings.DefaultBitrateForResolution(ComboValue(_resolution));
				if (bitrate != null)
					_bitrate.Text = bitrate;
			}
			else if (preset == "balanced")
			{
				SelectComboValue(_scaling, "ewa_lanczossharp");
				_vsync.IsChecked = false;
				_audioBuffer.Text = "20";
				_bitrate.Text = "32000";
			}
			else
			{
				SelectComboValue(_scaling, "preset_high_quality_advanced_spatial");
				_vsync.IsChecked = false;
				_audioBuffer.Text = "30";
				_bitrate.Text = "45000";
			}
		}
		finally
		{
			_syncingSimple = false;
		}
		SyncSimpleFromCanonical();
	}

	private void OnSimpleQualityChanged()
	{
		if (_loading || _syncingSimple)
			return;
		string quality = ComboValue(_simpleQuality);
		if (quality == "custom" || quality.Length == 0)
			return;
		_syncingSimple = true;
		try
		{
			SelectComboValue(_resolution, quality);
			if (ComboValue(_performancePreset) == "lowlatency")
			{
				string bitrate = StreamSettings.DefaultBitrateForResolution(quality);
				if (bitrate != null)
					_bitrate.Text = bitrate;
			}
		}
		finally
		{
			_syncingSimple = false;
		}
		SyncSimpleFromCanonical();
	}

	private void OnSimpleMouseAimChanged()
	{
		if (_loading || _syncingSimple)
			return;
		_syncingSimple = true;
		try
		{
			_mouseAim.IsChecked = _simpleMouseAim.IsChecked;
		}
		finally
		{
			_syncingSimple = false;
		}
	}

	private void CopySimpleText(TextBox source, TextBox target)
	{
		if (_loading || _syncingSimple)
			return;
		_syncingSimple = true;
		try
		{
			target.Text = source.Text;
		}
		finally
		{
			_syncingSimple = false;
		}
	}

	private void ToggleAimAdvanced()
	{
		bool show = _aimAdvancedPanel.Visibility != Visibility.Visible;
		_aimAdvancedPanel.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
		_aimAdvancedToggle.Content = show ? "Hide tuning" : "Show tuning";
	}

	private bool ChildRunning()
	{
		return _child != null && !_child.HasExited;
	}

	private void SetNotice(string text)
	{
		_notice.Text = text ?? string.Empty;
	}

	private void RefreshState()
	{
		bool afterExit = false;
		if (_child != null)
		{
			if (ChildRunning())
			{
				Render();
				return;
			}
			if (_childKind == ChildKind.Player)
				_everConnected = true;
			try
			{
				_lastExitCode = _child.ExitCode;
			}
			catch (Exception)
			{
				_lastExitCode = null;
			}
			_lastExitKind = _childKind;
			_runningNotice = null;
			_child.Dispose();
			_child = null;
			_watch.Stop();
			afterExit = true;
			if (_minimizedForChild)
			{
				_minimizedForChild = false;
				_win.WindowState = WindowState.Normal;
				_win.Activate();
			}
			if (_closeRequested)
			{
				_closeRequested = false;
				_closing = true;
				_win.Close();
				return;
			}
		}

		try
		{
			List<ConsoleEntry> consoles = StreamSettings.ReadConsoles();
			_consoles.Clear();
			_consoles.AddRange(consoles);
			_readFailed = false;
			_readError = null;
		}
		catch (Exception ex)
		{
			_readFailed = true;
			_readError = ex.Message;
			_consoles.Clear();
			_selectedIndex = 0;
		}
		if (_selectedIndex < 0 || _selectedIndex >= _consoles.Count)
			_selectedIndex = 0;

		if (afterExit || _saved == null)
			LoadSettings();
		Render();
		ShowPendingExitNote();
	}

	private string _readError;

	private void Render()
	{
		bool running = ChildRunning();
		if (running)
		{
			_watch.Start();
			_manage.Visibility = Visibility.Collapsed;
			_native.Visibility = Visibility.Collapsed;
			_consoleList.Visibility = Visibility.Collapsed;
			if (_childKind == ChildKind.Player)
			{
				_title.Text = "Player open";
				_sessionState.Text = "PLAYER OPEN";
				_primary.Content = "Disconnect";
				_detail.Text = "Close the player or choose Disconnect. Ctrl+Q also ends the stream.";
			}
			else if (_childKind == ChildKind.Pair)
			{
				_title.Text = "Pairing window open";
				_sessionState.Text = "SETUP OPEN";
				_primary.Content = "Close setup";
				_detail.Text = "Finish pairing in the setup window, then close it to come back.";
			}
			else
			{
				_title.Text = "Controller settings open";
				_sessionState.Text = "SETUP OPEN";
				_primary.Content = "Close setup";
				_detail.Text = "Change controller mapping and extra options there, then close it to come back.";
			}
			if (_runningNotice != null)
				SetNotice(_runningNotice);
			else if (_closeRequested)
				SetNotice("Waiting for the player to close.");
		}
		else if (_readFailed)
		{
			_title.Text = "Cannot load consoles";
			_sessionState.Text = "NEEDS ATTENTION";
			_detail.Text = _readError ?? "The saved console list could not be read.";
			_primary.Content = "Retry";
			_manage.Visibility = Visibility.Collapsed;
			_native.Visibility = Visibility.Collapsed;
			_consoleList.Visibility = Visibility.Collapsed;
		}
		else if (_consoles.Count == 0)
		{
			_title.Text = "Pair your PS5";
			_sessionState.Text = "PAIRING NEEDED";
			_detail.Text = "On the PS5 open Settings > System > Remote Play > Link Device, then choose Pair PS5 and enter the code.";
			_primary.Content = "Pair PS5";
			_manage.Visibility = Visibility.Collapsed;
			_native.Visibility = Visibility.Visible;
			_consoleList.Visibility = Visibility.Collapsed;
		}
		else
		{
			ConsoleEntry console = SelectedConsole();
			_title.Text = console != null ? console.Name : "PS5";
			_sessionState.Text = "READY";
			_detail.Text = "Console paired. Keep it awake on the same network.";
			_primary.Content = _everConnected ? "Reconnect" : "Connect";
			_manage.Visibility = Visibility.Visible;
			_native.Visibility = Visibility.Visible;
			bool multiple = _consoles.Count > 1;
			_consoleList.Visibility = multiple ? Visibility.Visible : Visibility.Collapsed;
			if (multiple)
			{
				_syncingConsoles = true;
				List<string> names = new List<string>();
				foreach (ConsoleEntry entry in _consoles)
					names.Add(entry.Name);
				_consoleList.ItemsSource = names;
				_consoleList.SelectedIndex = _selectedIndex;
				_syncingConsoles = false;
			}
		}
		UpdateDraftState();
	}

	private void ShowPendingExitNote()
	{
		if (!_lastExitCode.HasValue)
			return;
		int code = _lastExitCode.Value;
		_lastExitCode = null;
		if (code == 0)
			return;
		if (_lastExitKind == ChildKind.Player)
			SetNotice(string.Format(CultureInfo.InvariantCulture, "Player exited with code {0}. Check the player window or try again.", code));
		else
			SetNotice(string.Format(CultureInfo.InvariantCulture, "The setup window exited with code {0}. Try again.", code));
	}

	private ConsoleEntry SelectedConsole()
	{
		if (_consoles.Count == 0)
			return null;
		int index = _selectedIndex;
		if (index < 0 || index >= _consoles.Count)
			index = 0;
		return _consoles[index];
	}

	private void OnPrimaryClick()
	{
		if (_child != null)
		{
			if (ChildRunning())
				TryCloseChild();
			else
				RefreshState();
			return;
		}
		if (_readFailed)
		{
			RefreshState();
			return;
		}
		if (_consoles.Count == 0)
		{
			StartSetup(ChildKind.Pair);
			return;
		}
		ConsoleEntry console = SelectedConsole();
		if (console == null)
			StartSetup(ChildKind.Pair);
		else
			StartConnect(console);
	}

	private bool BlockedByDraft()
	{
		if (_settingsFailed)
		{
			SetNotice("Settings could not be loaded. Use Reset to saved before starting a session.");
			return true;
		}
		if (_readFailed)
		{
			SetNotice("The console list could not be read. Use Retry before starting a session.");
			return true;
		}
		if (!IsDirty())
			return false;
		SetNotice("You have unsaved settings. Save or undo them before connecting.");
		return true;
	}

	private void StartSetup(ChildKind kind)
	{
		if (_closing || ChildRunning())
			return;
		if (BlockedByDraft())
			return;
		SetNotice(string.Empty);
		try
		{
			EnsureBackendVerified();
			EnsurePrivateSettings(null);
			LaunchChild(string.Empty, kind);
		}
		catch (Exception ex)
		{
			SetNotice(ex.Message);
		}
	}

	private void StartConnect(ConsoleEntry console)
	{
		if (_closing || ChildRunning() || console == null)
			return;
		if (BlockedByDraft())
			return;
		SetNotice(string.Empty);
		try
		{
			EnsureBackendVerified();
			EnsurePrivateSettings(console);
			LaunchChild("--exit-app-on-stream-exit", ChildKind.Player);
			if (_child != null)
			{
				_minimizedForChild = true;
				_win.WindowState = WindowState.Minimized;
			}
		}
		catch (Exception ex)
		{
			SetNotice(ex.Message);
		}
	}

	private void LaunchChild(string arguments, ChildKind kind)
	{
		if (_linkCheck != null)
		{
			_linkCheck.Cancel();
			_linkCheck = null;
		}
		ProcessStartInfo startInfo = new ProcessStartInfo
		{
			FileName = _backendExe,
			Arguments = arguments,
			WorkingDirectory = _backendDir,
			UseShellExecute = false
		};
		if (_child != null)
			_child.Dispose();
		_child = Process.Start(startInfo);
		if (_child == null)
			throw new InvalidOperationException("Failed to start the player.");
		_childKind = kind;
		_runningNotice = null;
		RefreshState();
	}

	private void TryCloseChild()
	{
		if (_child == null)
			return;
		_runningNotice = null;
		SetNotice(string.Empty);
		try
		{
			_child.Refresh();
			if (_child.HasExited)
			{
				RefreshState();
				return;
			}
			if (!_child.CloseMainWindow())
				_runningNotice = "The player is still opening or has a dialog. Close its window or try again.";
		}
		catch (Win32Exception ex)
		{
			_runningNotice = ex.Message;
		}
		catch (InvalidOperationException)
		{
			RefreshState();
			return;
		}
		RefreshState();
	}

	private void OnWindowClosing(object sender, CancelEventArgs e)
	{
		if (_closing)
			return;
		if (_aimPad != null)
			_aimPad.Release();
		if (_linkCheck != null)
		{
			_linkCheck.Cancel();
			_linkCheck = null;
		}
		if (_child != null && !_child.HasExited)
		{
			e.Cancel = true;
			_closeRequested = true;
			SetNotice("Waiting for the player to close.");
			TryCloseChild();
			return;
		}
		if (IsDirty())
		{
			MessageBoxResult answer = MessageBox.Show(_win, "You have unsaved settings. Discard them and close?", "Vectis Play", MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No);
			if (answer != MessageBoxResult.Yes)
				e.Cancel = true;
		}
	}

	private void EnsureBackendVerified()
	{
		if (!File.Exists(_backendExe))
			throw new InvalidOperationException("Runtime missing. Run build.py first.");
		string actual;
		using (SHA256 sha = SHA256.Create())
		using (FileStream stream = File.OpenRead(_backendExe))
			actual = BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", string.Empty).ToLowerInvariant();
		if (actual != ExpectedExeSha256)
			throw new InvalidOperationException("Runtime integrity check failed. Rebuild with build.py.");
	}

	private void EnsurePrivateSettings(ConsoleEntry console)
	{
		using (RegistryKey root = Registry.CurrentUser.CreateSubKey(StreamSettings.RegistryPath, RegistryKeyPermissionCheck.ReadWriteSubTree))
		using (RegistryKey settings = root.CreateSubKey("settings", RegistryKeyPermissionCheck.ReadWriteSubTree))
		{
			StreamSettings.SetIfAbsent(settings, "placebo_preset", "high_quality");
			StreamSettings.SetIfAbsent(settings, "placebo_frame_mixer", "none");
			StreamSettings.SetIfAbsent(settings, "disconnect_action", "nothing");
			StreamSettings.SetIfAbsent(settings, "remote_play_ask", "false");
			StreamSettings.SetIfAbsent(settings, "add_steam_shortcut_ask", "false");
			StreamSettings.WriteDefaultKeyMap(root);
			if (console != null)
				settings.SetValue("auto_connect_mac", console.Mac, console.MacKind);
			else
				settings.SetValue("auto_connect_mac", string.Empty, RegistryValueKind.String);
		}
	}

	private void OnCheckLink()
	{
		if (_closing || ChildRunning())
			return;
		if (_linkCheck != null)
			_linkCheck.Cancel();
		CancellationTokenSource check = new CancellationTokenSource();
		_linkCheck = check;
		_linkDetails.Visibility = Visibility.Visible;
		_linkPanel.BeginCheck();
		_win.Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(delegate { _linkDetails.BringIntoView(); }));
		RunLinkCheck(check);
	}

	// A superseded check must not touch the panel: its late Cancelled() would
	// wipe the samples of the check that replaced it.
	private async void RunLinkCheck(CancellationTokenSource check)
	{
		try
		{
			LinkProbe.Result result = await LinkProbe.RunAsync(new Progress<int?>(delegate(int? sample)
			{
				if (!check.IsCancellationRequested)
					_linkPanel.AddSample(sample);
			}), check.Token);
			if (!check.IsCancellationRequested)
				_linkPanel.Finish(result);
			else if (OwnsLinkPanel(check))
				_linkPanel.Cancelled();
		}
		catch (OperationCanceledException)
		{
			if (OwnsLinkPanel(check))
				_linkPanel.Cancelled();
		}
		catch (Exception ex)
		{
			if (OwnsLinkPanel(check))
				_linkPanel.Fail(ex.Message);
		}
		finally
		{
			if (ReferenceEquals(_linkCheck, check))
				_linkCheck = null;
			check.Dispose();
		}
	}

	private bool OwnsLinkPanel(CancellationTokenSource check)
	{
		return _linkCheck == null || ReferenceEquals(_linkCheck, check);
	}

	private void ApplyLinkSuggestion()
	{
		string kbps = _linkPanel.SuggestedKbps;
		if (kbps == null)
			return;
		_bitrate.Text = kbps;
		SetNotice("Bitrate " + kbps + " kbps is now in the draft. Choose Save settings to use it on the next connection.");
	}

	private void LoadSettings()
	{
		_loading = true;
		try
		{
			Dictionary<string, string> saved = StreamSettings.ReadSavedValues();
			_saved = saved;
			_settingsFailed = false;
			ShowCombo(_resolution, StreamSettings.ResolutionChoices, saved["resolution"]);
			ShowCombo(_rate, StreamSettings.RateChoices, saved["rate"]);
			ShowCombo(_codec, StreamSettings.CodecChoices, saved["codec"]);
			ShowCombo(_decoder, StreamSettings.DecoderChoices, saved["decoder"]);
			ShowCombo(_renderer, StreamSettings.RendererChoices, saved["renderer"]);
			ShowCombo(_windowMode, StreamSettings.WindowChoices, saved["window"]);
			ShowCombo(_scaling, StreamSettings.ScalingChoices, saved["scaling"]);
			ShowCombo(_mixer, StreamSettings.MixerChoices, saved["mixer"]);
			_bitrate.Text = saved["bitrate"];
			_volume.Text = saved["volume"];
			_audioBuffer.Text = saved["buffer"];
			_vsync.IsChecked = saved["vsync"] == "true";
			ShowCombo(_walkKey, StreamSettings.WalkKeyChoices, saved["walkkey"]);
			ShowCombo(_aimRelease, StreamSettings.AimReleaseChoices, saved["aimrelease"]);
			_keyboardEnabled.IsChecked = saved["keyboard"] == "true";
			_walkScale.Text = saved["walkscale"];
			_mouseAim.IsChecked = saved["mouseaim"] == "true";
			_aimSensitivity.Text = saved["aimsens"];
			_aimYRatio.Text = saved["aimyratio"];
			_aimDeadzone.Text = saved["aimdeadzone"];
			_aimCurve.Text = saved["aimcurve"];
			_aimWindow.Text = saved["aimwindow"];
			_aimTau.Text = saved["aimtau"];
			_colorSat.Text = saved["colorsat"];
			_colorVib.Text = saved["colorvib"];
			_colorCon.Text = saved["colorcon"];
			_colorBri.Text = saved["colorbri"];
			_colorTemp.Text = saved["colortemp"];
			_colorTint.Text = saved["colortint"];
			_hapticOverride.Text = saved["haptics"];
			ShowCombo(_rumbleIntensity, StreamSettings.RumbleChoices, saved["rumble"]);
			_padBackground.IsChecked = saved["padbackground"] == "true";
			LoadKeyMap();
			_aimPad.SyncFrom(saved);
			_sumResolution.Text = StreamSettings.SummaryChoice(StreamSettings.ResolutionChoices, saved["resolution"]);
			_sumRate.Text = StreamSettings.SummaryChoice(StreamSettings.RateChoices, saved["rate"]);
			_sumBitrate.Text = StreamSettings.SummaryNumber(saved["bitrate"], "kbps");
			_sumDecoder.Text = StreamSettings.SummaryChoice(StreamSettings.DecoderChoices, saved["decoder"]);
			_sumScaling.Text = StreamSettings.SummaryChoice(StreamSettings.ScalingChoices, saved["scaling"]);
			_sumBuffer.Text = StreamSettings.SummaryNumber(saved["buffer"], "ms");
		}
		catch (Exception ex)
		{
			_saved = new Dictionary<string, string>();
			_settingsFailed = true;
			_sumResolution.Text = string.Empty;
			_sumRate.Text = string.Empty;
			_sumBitrate.Text = string.Empty;
			_sumDecoder.Text = string.Empty;
			_sumScaling.Text = string.Empty;
			_sumBuffer.Text = string.Empty;
			_keyMapGrid.Children.Clear();
			SetNotice("Settings could not be loaded: " + ex.Message + " Use Reset to saved to retry.");
		}
		finally
		{
			_loading = false;
		}
		UpdateDraftState();
	}

	private void LoadKeyMap()
	{
		_keyMapGrid.Children.Clear();
		foreach (KeyMapEntry row in StreamSettings.ReadKeyMap())
		{
			Grid cell = new Grid { Margin = new Thickness(0, 0, 16, 8), MinHeight = 34 };
			cell.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
			cell.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
			TextBlock button = new TextBlock
			{
				Text = row.Button,
				FontSize = 13,
				Foreground = FindBrush("Muted"),
				VerticalAlignment = VerticalAlignment.Center,
				TextTrimming = TextTrimming.CharacterEllipsis
			};
			Border cap = new Border
			{
				Background = FindBrush("Raised"),
				BorderBrush = FindBrush("Line2"),
				BorderThickness = new Thickness(1, 1, 1, 2),
				CornerRadius = new CornerRadius(5),
				Padding = new Thickness(10, 3, 10, 3),
				MinWidth = 34,
				Margin = new Thickness(10, 0, 0, 0),
				VerticalAlignment = VerticalAlignment.Center,
				Child = new TextBlock
				{
					Text = row.Key,
					FontFamily = new FontFamily("Consolas"),
					FontSize = 13,
					FontWeight = FontWeights.SemiBold,
					Foreground = FindBrush("Paper"),
					HorizontalAlignment = HorizontalAlignment.Center
				}
			};
			Grid.SetColumn(cap, 1);
			cell.Children.Add(button);
			cell.Children.Add(cap);
			_keyMapGrid.Children.Add(cell);
		}
	}

	private Brush FindBrush(string key)
	{
		return (Brush)_win.FindResource(key);
	}

	private static void ShowCombo(ComboBox combo, Choice[] choices, string value)
	{
		List<Choice> items = new List<Choice>();
		if (value.StartsWith("~", StringComparison.Ordinal))
			items.Add(new Choice(value, StreamSettings.NativeDisplay(value.Substring(1))));
		items.AddRange(choices);
		combo.ItemsSource = items;
		foreach (Choice item in items)
			if (item.Value == value)
			{
				combo.SelectedItem = item;
				return;
			}
		combo.SelectedIndex = -1;
	}

	private static string ComboValue(ComboBox combo)
	{
		Choice choice = combo.SelectedItem as Choice;
		return choice == null ? string.Empty : choice.Value;
	}

	private Dictionary<string, string> ReadUi()
	{
		Dictionary<string, string> ui = new Dictionary<string, string>();
		ui["resolution"] = ComboValue(_resolution);
		ui["rate"] = ComboValue(_rate);
		ui["bitrate"] = (_bitrate.Text ?? string.Empty).Trim();
		ui["codec"] = ComboValue(_codec);
		ui["decoder"] = ComboValue(_decoder);
		ui["renderer"] = ComboValue(_renderer);
		ui["window"] = ComboValue(_windowMode);
		ui["vsync"] = _vsync.IsChecked == true ? "true" : "false";
		ui["volume"] = (_volume.Text ?? string.Empty).Trim();
		ui["buffer"] = (_audioBuffer.Text ?? string.Empty).Trim();
		ui["scaling"] = ComboValue(_scaling);
		ui["mixer"] = ComboValue(_mixer);
		ui["keyboard"] = _keyboardEnabled.IsChecked == true ? "true" : "false";
		ui["walkkey"] = ComboValue(_walkKey);
		ui["walkscale"] = (_walkScale.Text ?? string.Empty).Trim();
		ui["mouseaim"] = _mouseAim.IsChecked == true ? "true" : "false";
		ui["aimsens"] = (_aimSensitivity.Text ?? string.Empty).Trim();
		ui["aimyratio"] = (_aimYRatio.Text ?? string.Empty).Trim();
		ui["aimdeadzone"] = (_aimDeadzone.Text ?? string.Empty).Trim();
		ui["aimcurve"] = (_aimCurve.Text ?? string.Empty).Trim();
		ui["aimwindow"] = (_aimWindow.Text ?? string.Empty).Trim();
		ui["aimtau"] = (_aimTau.Text ?? string.Empty).Trim();
		ui["aimrelease"] = ComboValue(_aimRelease);
		ui["haptics"] = (_hapticOverride.Text ?? string.Empty).Trim();
		ui["rumble"] = ComboValue(_rumbleIntensity);
		ui["padbackground"] = _padBackground.IsChecked == true ? "true" : "false";
		ui["colorsat"] = (_colorSat.Text ?? string.Empty).Trim();
		ui["colorvib"] = (_colorVib.Text ?? string.Empty).Trim();
		ui["colorcon"] = (_colorCon.Text ?? string.Empty).Trim();
		ui["colorbri"] = (_colorBri.Text ?? string.Empty).Trim();
		ui["colortemp"] = (_colorTemp.Text ?? string.Empty).Trim();
		ui["colortint"] = (_colorTint.Text ?? string.Empty).Trim();
		return ui;
	}

	private List<string> Changed(Dictionary<string, string> ui)
	{
		List<string> changed = new List<string>();
		if (_saved == null || _saved.Count == 0)
			return changed;
		foreach (string id in StreamSettings.FieldOrder)
			if (!string.Equals(ui[id], _saved[id], StringComparison.Ordinal))
				changed.Add(id);
		return changed;
	}

	private bool IsDirty()
	{
		return !_settingsFailed && Changed(ReadUi()).Count > 0;
	}

	private void OnDraftChanged()
	{
		if (_loading)
			return;
		UpdateDraftState();
	}

	private void UpdateDraftState()
	{
		bool running = ChildRunning();
		Dictionary<string, string> ui = ReadUi();
		List<string> changedIds = _settingsFailed || _saved == null ? new List<string>() : Changed(ui);
		int count = changedIds.Count;
		foreach (Control control in _settingControls)
			control.IsEnabled = !running && !_settingsFailed;
		bool keyboardOn = _keyboardEnabled.IsChecked == true;
		_walkKey.IsEnabled = !running && !_settingsFailed && keyboardOn;
		_walkScale.IsEnabled = !running && !_settingsFailed && keyboardOn;
		bool aimOn = _mouseAim.IsChecked == true;
		foreach (Control control in _aimControls)
			control.IsEnabled = !running && !_settingsFailed && aimOn;
		foreach (KeyValuePair<string, Control> pair in _fieldControls)
			pair.Value.Tag = changedIds.Contains(pair.Key) ? "changed" : null;
		bool presetChanged = changedIds.Contains("rate") || changedIds.Contains("codec") || changedIds.Contains("decoder") || changedIds.Contains("renderer") || changedIds.Contains("scaling") || changedIds.Contains("mixer") || changedIds.Contains("bitrate") || changedIds.Contains("vsync") || changedIds.Contains("buffer");
		_performancePreset.Tag = presetChanged ? "changed" : null;
		_simpleQuality.Tag = changedIds.Contains("resolution") ? "changed" : null;
		_simpleMouseAim.Tag = changedIds.Contains("mouseaim") ? "changed" : null;
		_simpleAimSensitivity.Tag = changedIds.Contains("aimsens") ? "changed" : null;
		_simpleVolume.Tag = changedIds.Contains("volume") ? "changed" : null;
		_apply.IsEnabled = !running && !_settingsFailed && count > 0;
		_revert.IsEnabled = !running && (count > 0 || _settingsFailed);
		_apply.Content = "Save settings";
		_revert.Content = "Reset to saved";
		_draftBadge.Visibility = count > 0 ? Visibility.Visible : Visibility.Collapsed;
		_draftCount.Text = count == 1 ? "1 unsaved change" : count.ToString(CultureInfo.InvariantCulture) + " unsaved changes";
		_linkPanel.SetChildRunning(running);
		_linkPanel.UpdateUseState(_bitrate.Text);
		SyncSimpleFromCanonical();
		_aimPad.SyncFrom(ui);
	}

	private void OnApply()
	{
		if (ChildRunning() || _settingsFailed)
			return;
		Dictionary<string, string> ui = ReadUi();
		List<string> changed = Changed(ui);
		if (changed.Count == 0)
		{
			SetNotice("No changes to save.");
			return;
		}
		string error = StreamSettings.Validate(ui, changed);
		if (error != null)
		{
			SetNotice("Not saved. " + error);
			return;
		}
		try
		{
			EnsureBackendVerified();
			StreamSettings.WriteChanges(ui, changed);
		}
		catch (Exception ex)
		{
			SetNotice("Not saved. " + ex.Message + " Some values may have been written. Use Reset to saved to see what is saved.");
			return;
		}
		LoadSettings();
		if (_settingsFailed)
			return;
		SetNotice("Settings saved. They will be used the next time you connect.");
	}

	private void OnRevert()
	{
		if (ChildRunning())
			return;
		LoadSettings();
		if (!_settingsFailed)
			SetNotice("Draft reset to the saved settings.");
	}
}
