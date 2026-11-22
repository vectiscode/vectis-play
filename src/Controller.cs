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
	private const string ExpectedExeSha256 = "61c0525ff9ac80a290a9534d04dbe1e9b85177523826d4ad7cbb5c580642390d";

	[DllImport("dwmapi.dll")]
	private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

	private readonly Window _win;
	private readonly string _backendExe;
	private readonly string _backendDir;

	private readonly TextBlock _title;
	private readonly TextBlock _detail;
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
	private readonly UniformGrid _keyMapGrid;
	private readonly AimPad _aimPad;
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
	private string _runningNotice;
	private int? _lastExitCode;
	private ChildKind _lastExitKind;

	public Controller(Window win)
	{
		_win = win;
		SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(Dispatcher.CurrentDispatcher));
		_backendDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "runtime", "chiaki-ng-Win");
		_backendExe = Path.Combine(_backendDir, "chiaki.exe");

		TabControl pages = Part<TabControl>("Pages");
		if (pages.Items.Count != 5)
			throw new InvalidOperationException("Workbench.xaml 'Pages' must hold 5 tabs, found " + pages.Items.Count.ToString(CultureInfo.InvariantCulture) + ".");

		_title = Part<TextBlock>("SessionTitle");
		_detail = Part<TextBlock>("SessionDetail");
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
		_keyMapGrid = Part<UniformGrid>("KeyMapGrid");
		_aimPad = new AimPad(_win,
			Part<Canvas>("ResponseGraph"), Part<Canvas>("AimPad"),
			Part<TextBlock>("AimSpeed"), Part<TextBlock>("AimStick"));

		_settingControls.AddRange(new Control[] { _vsync, _resolution, _rate, _codec, _decoder, _renderer, _scaling, _mixer, _windowMode, _bitrate, _volume, _audioBuffer, _keyboardEnabled, _walkKey, _walkScale, _mouseAim, _colorSat, _colorVib, _colorCon, _colorBri, _colorTemp, _colorTint });
		_aimControls.AddRange(new Control[] { _aimSensitivity, _aimYRatio, _aimDeadzone, _aimCurve, _aimWindow, _aimTau, _aimRelease });
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
		_fieldControls["colorsat"] = _colorSat;
		_fieldControls["colorvib"] = _colorVib;
		_fieldControls["colorcon"] = _colorCon;
		_fieldControls["colorbri"] = _colorBri;
		_fieldControls["colortemp"] = _colorTemp;
		_fieldControls["colortint"] = _colorTint;

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

		ComboBox[] combos = { _resolution, _rate, _codec, _decoder, _renderer, _scaling, _mixer, _windowMode, _walkKey, _aimRelease };
		foreach (ComboBox combo in combos)
			combo.SelectionChanged += delegate { OnDraftChanged(); };
		TextBox[] boxes = { _bitrate, _volume, _audioBuffer, _walkScale, _aimSensitivity, _aimYRatio, _aimDeadzone, _aimCurve, _aimWindow, _aimTau, _colorSat, _colorVib, _colorCon, _colorBri, _colorTemp, _colorTint };
		foreach (TextBox box in boxes)
			box.TextChanged += delegate { OnDraftChanged(); };
		_vsync.Checked += delegate { OnDraftChanged(); };
		_vsync.Unchecked += delegate { OnDraftChanged(); };
		_keyboardEnabled.Checked += delegate { OnDraftChanged(); };
		_keyboardEnabled.Unchecked += delegate { OnDraftChanged(); };
		_mouseAim.Checked += delegate { OnDraftChanged(); };
		_mouseAim.Unchecked += delegate { OnDraftChanged(); };

		_watch.Interval = TimeSpan.FromMilliseconds(400);
		_watch.Tick += delegate
		{
			if (_child != null && _child.HasExited)
				RefreshState();
		};

		_notice.Text = string.Empty;
		_win.Loaded += delegate
		{
			_loading = false;
			RefreshState();
		};
	}

	public void Dispose()
	{
		_watch.Stop();
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
				_primary.Content = "Disconnect";
				_detail.Text = "The player window is open. Ctrl+Q also disconnects in the player.";
			}
			else if (_childKind == ChildKind.Pair)
			{
				_title.Text = "Pairing window open";
				_primary.Content = "Close setup";
				_detail.Text = "Finish linking on the PS5 in the chiaki-ng window, then close it to come back here.";
			}
			else
			{
				_title.Text = "Native settings open";
				_primary.Content = "Close setup";
				_detail.Text = "chiaki-ng is open without auto-connect. Use its settings icon for controller mapping and other native options, then close it to come back here.";
			}
			if (_runningNotice != null)
				SetNotice(_runningNotice);
			else if (_closeRequested)
				SetNotice("Waiting for the player to close.");
		}
		else if (_readFailed)
		{
			_title.Text = "Cannot load consoles";
			_detail.Text = _readError ?? "The saved console list could not be read.";
			_primary.Content = "Retry";
			_manage.Visibility = Visibility.Collapsed;
			_native.Visibility = Visibility.Collapsed;
			_consoleList.Visibility = Visibility.Collapsed;
		}
		else if (_consoles.Count == 0)
		{
			_title.Text = "Pair your PS5";
			_detail.Text = "On the PS5, open Settings > System > Remote Play > Link Device. Register in chiaki-ng, then close its window.";
			_primary.Content = "Pair PS5";
			_manage.Visibility = Visibility.Collapsed;
			_native.Visibility = Visibility.Visible;
			_consoleList.Visibility = Visibility.Collapsed;
		}
		else
		{
			ConsoleEntry console = SelectedConsole();
			_title.Text = console != null ? console.Name : "PS5";
			_detail.Text = "Ready to connect. Saved stream settings apply from the next session.";
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
			SetNotice(string.Format(CultureInfo.InvariantCulture, "The chiaki-ng window exited with code {0}. Try again.", code));
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
			SetNotice("Stream settings could not be loaded. Use Revert to retry before starting a session.");
			return true;
		}
		if (_readFailed)
		{
			SetNotice("The console list could not be read. Use Retry before starting a session.");
			return true;
		}
		if (!IsDirty())
			return false;
		SetNotice("You have unsaved stream settings. Apply or Revert them first.");
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
			MessageBoxResult answer = MessageBox.Show(_win, "You have unsaved stream settings. Discard them and close?", "Vectis Play", MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No);
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
			StreamSettings.SetIfAbsent(settings, "placebo_preset", "fast");
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
		_linkCheck = new CancellationTokenSource();
		_linkPanel.BeginCheck();
		RunLinkCheck(_linkCheck.Token);
	}

	private async void RunLinkCheck(CancellationToken token)
	{
		try
		{
			LinkProbe.Result result = await LinkProbe.RunAsync(new Progress<int?>(_linkPanel.AddSample), token);
			_linkPanel.Finish(result);
		}
		catch (OperationCanceledException)
		{
			_linkPanel.Cancelled();
		}
		catch (Exception ex)
		{
			_linkPanel.Fail(ex.Message);
		}
	}

	private void ApplyLinkSuggestion()
	{
		string kbps = _linkPanel.SuggestedKbps;
		if (kbps != null)
			_bitrate.Text = kbps;
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
			SetNotice("Stream settings could not be loaded: " + ex.Message + " Use Revert to retry.");
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
			Grid cell = new Grid();
			cell.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
			cell.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
			TextBlock button = new TextBlock
			{
				Text = row.Button.ToUpperInvariant(),
				FontFamily = new FontFamily("Consolas"),
				FontSize = 11,
				Foreground = FindBrush("Muted"),
				VerticalAlignment = VerticalAlignment.Center
			};
			TextBlock key = new TextBlock
			{
				Text = row.Key,
				FontFamily = new FontFamily("Consolas"),
				FontSize = 12,
				FontWeight = FontWeights.SemiBold,
				Foreground = FindBrush("Paper"),
				HorizontalAlignment = HorizontalAlignment.Right,
				VerticalAlignment = VerticalAlignment.Center,
				Margin = new Thickness(10, 0, 0, 0)
			};
			Grid.SetColumn(key, 1);
			cell.Children.Add(button);
			cell.Children.Add(key);
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
		bool aimOn = _mouseAim.IsChecked == true;
		foreach (Control control in _aimControls)
			control.IsEnabled = !running && !_settingsFailed && aimOn;
		foreach (KeyValuePair<string, Control> pair in _fieldControls)
			pair.Value.Tag = changedIds.Contains(pair.Key) ? "changed" : null;
		_apply.IsEnabled = !running && !_settingsFailed && count > 0;
		_revert.IsEnabled = !running && (count > 0 || _settingsFailed);
		_apply.Content = count > 0 ? string.Format(CultureInfo.InvariantCulture, "Apply {0} change{1}", count, count == 1 ? string.Empty : "s") : "Apply";
		_revert.Content = "Revert";
		_linkPanel.SetChildRunning(running);
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
			SetNotice("No changes to apply.");
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
			SetNotice("Not saved. " + ex.Message + " Some values may have been written. Use Revert to see what is saved.");
			return;
		}
		LoadSettings();
		if (_settingsFailed)
			return;
		SetNotice(string.Format(CultureInfo.InvariantCulture, "Saved {0} change{1}. They apply from the next session.", changed.Count, changed.Count == 1 ? string.Empty : "s"));
	}

	private void OnRevert()
	{
		if (ChildRunning())
			return;
		LoadSettings();
		if (!_settingsFailed)
			SetNotice("Showing the saved stream settings.");
	}
}
