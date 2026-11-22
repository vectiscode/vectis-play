using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Markup;
using System.Windows.Threading;
using Microsoft.Win32;

internal static class Program
{
	private const string MutexName = @"Local\VectisPlay.SingleInstance";

	private static Mutex _instanceMutex;

	[STAThread]
	private static int Main()
	{
		bool createdNew;
		_instanceMutex = new Mutex(true, MutexName, out createdNew);
		if (!createdNew)
			return 0;

		Application app = new Application();
		Window win;
		Controller controller;
		try
		{
			using (Stream stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("Vectis.Workbench.xaml"))
			{
				if (stream == null)
					throw new InvalidOperationException("Embedded Vectis.Workbench.xaml is missing from this build.");
				win = (Window)XamlReader.Load(stream);
			}
			controller = new Controller(win);
		}
		catch (Exception ex)
		{
			MessageBox.Show(ex.Message, "Vectis Play", MessageBoxButton.OK, MessageBoxImage.Error);
			return 1;
		}
		app.Run(win);
		controller.Dispose();
		return 0;
	}
}

internal sealed class ConsoleEntry
{
	public string Name;
	public object Mac;
	public RegistryValueKind MacKind;
}

internal sealed class Choice
{
	public string Label { get; set; }
	public string Value { get; set; }

	public Choice(string value, string label)
	{
		Value = value;
		Label = label;
	}

	public override string ToString()
	{
		return Label;
	}
}

internal enum ChildKind
{
	Player,
	Pair,
	Native
}

internal sealed class Controller : IDisposable
{
	private const string RegistryPath = @"Software\Vectis\Vectis";
	private const string PlaceboPath = @"Software\Vectis\pl_render_params\placebo_settings";
	private const string ExpectedExeSha256 = "834e7f6c971183664dc9367eae054fbfb4e964e596d37e0aa43f69406bb70805";
	private const int Ps5TargetMinimum = 1000000;
	private const int BytesPerMs = 192;
	private const int NativeVolumeMax = 128;

	[DllImport("dwmapi.dll")]
	private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

	private static readonly string[] FieldOrder = new[] { "resolution", "rate", "bitrate", "codec", "decoder", "renderer", "window", "vsync", "volume", "buffer", "scaling", "mixer" };

	private static readonly Dictionary<string, string> FieldLabels = new Dictionary<string, string>
	{
		{ "resolution", "Resolution" }, { "rate", "Frame rate" }, { "bitrate", "Bitrate" }, { "codec", "Codec" },
		{ "decoder", "Decoder" }, { "renderer", "Renderer" }, { "window", "Window mode" }, { "vsync", "VSync" },
		{ "volume", "Volume" }, { "buffer", "Audio buffer" }, { "scaling", "Upscaling" }, { "mixer", "Frame mixer" }
	};

	private static readonly Choice[] ResolutionChoices = { new Choice("360p", "360p"), new Choice("540p", "540p"), new Choice("720p", "720p"), new Choice("1080p", "1080p") };
	private static readonly Choice[] RateChoices = { new Choice("30", "30 fps"), new Choice("60", "60 fps") };
	private static readonly Choice[] CodecChoices = { new Choice("h264", "H.264"), new Choice("h265", "H.265"), new Choice("h265_hdr", "H.265 HDR") };
	private static readonly Choice[] DecoderChoices = { new Choice("auto", "Automatic"), new Choice("vulkan", "Vulkan Video"), new Choice("d3d11va", "D3D11VA"), new Choice("cuda", "CUDA"), new Choice("none", "Software") };
	private static readonly Choice[] RendererChoices = { new Choice("vulkan", "Vulkan"), new Choice("opengl", "OpenGL") };
	private static readonly Choice[] WindowChoices =
	{
		new Choice("Selected Resolution", "Selected resolution"), new Choice("Adjust Manually", "Adjust manually"),
		new Choice("Fullscreen", "Fullscreen"), new Choice("Zoom", "Zoom"), new Choice("Stretch", "Stretch")
	};
	private static readonly Choice[] ScalingChoices =
	{
		new Choice("direct", "Direct (fast)"), new Choice("nearest", "Nearest"), new Choice("bilinear", "Bilinear"),
		new Choice("bicubic", "Bicubic"), new Choice("gaussian", "Gaussian"), new Choice("catmull_rom", "Catmull-Rom"),
		new Choice("lanczos", "Lanczos"), new Choice("ewa_lanczos", "EWA Lanczos"), new Choice("ewa_lanczossharp", "EWA Lanczos sharp"),
		new Choice("ewa_lanczos4sharpest", "EWA Lanczos 4 sharpest"), new Choice("fsr", "FSR"),
		new Choice("fsrcnnx_x2_8_0_4_1", "FSRCNNX 8"), new Choice("fsrcnnx_x2_16_0_4_1", "FSRCNNX 16")
	};
	private static readonly Choice[] MixerChoices =
	{
		new Choice("direct", "Direct"), new Choice("queue", "Queue without blend"), new Choice("oversample", "Oversample"),
		new Choice("hermite", "Hermite"), new Choice("linear", "Linear"), new Choice("cubic", "Cubic")
	};

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
	private readonly List<Control> _settingControls = new List<Control>();
	private readonly Dictionary<string, Control> _fieldControls = new Dictionary<string, Control>();
	private readonly DispatcherTimer _watch = new DispatcherTimer();

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
		_backendDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "runtime", "chiaki-ng-Win");
		_backendExe = Path.Combine(_backendDir, "chiaki.exe");

		TabControl pages = Part<TabControl>("Pages");
		if (pages.Items.Count != 4)
			throw new InvalidOperationException("Workbench.xaml 'Pages' must hold 4 tabs, found " + pages.Items.Count.ToString(CultureInfo.InvariantCulture) + ".");

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

		_settingControls.AddRange(new Control[] { _vsync, _resolution, _rate, _codec, _decoder, _renderer, _scaling, _mixer, _windowMode, _bitrate, _volume, _audioBuffer });
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
		_consoleList.SelectionChanged += delegate
		{
			if (_syncingConsoles || _consoleList.SelectedIndex < 0)
				return;
			_selectedIndex = _consoleList.SelectedIndex;
			ConsoleEntry selected = SelectedConsole();
			if (selected != null && !ChildRunning() && !_readFailed)
				_title.Text = selected.Name;
		};

		ComboBox[] combos = { _resolution, _rate, _codec, _decoder, _renderer, _scaling, _mixer, _windowMode };
		foreach (ComboBox combo in combos)
			combo.SelectionChanged += delegate { OnDraftChanged(); };
		TextBox[] boxes = { _bitrate, _volume, _audioBuffer };
		foreach (TextBox box in boxes)
			box.TextChanged += delegate { OnDraftChanged(); };
		_vsync.Checked += delegate { OnDraftChanged(); };
		_vsync.Unchecked += delegate { OnDraftChanged(); };

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
		if (_child != null)
			_child.Dispose();
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
			List<ConsoleEntry> consoles = ReadConsoles();
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
		using (RegistryKey root = Registry.CurrentUser.CreateSubKey(RegistryPath, RegistryKeyPermissionCheck.ReadWriteSubTree))
		using (RegistryKey settings = root.CreateSubKey("settings", RegistryKeyPermissionCheck.ReadWriteSubTree))
		{
			SetIfAbsent(settings, "placebo_preset", "fast");
			SetIfAbsent(settings, "placebo_frame_mixer", "none");
			SetIfAbsent(settings, "disconnect_action", "nothing");
			SetIfAbsent(settings, "remote_play_ask", "false");
			SetIfAbsent(settings, "add_steam_shortcut_ask", "false");
			if (console != null)
				settings.SetValue("auto_connect_mac", console.Mac, console.MacKind);
			else
				settings.SetValue("auto_connect_mac", string.Empty, RegistryValueKind.String);
		}
	}

	private static void SetIfAbsent(RegistryKey key, string name, string value)
	{
		if (key.GetValue(name, null) == null)
			key.SetValue(name, value);
	}

	private List<ConsoleEntry> ReadConsoles()
	{
		List<ConsoleEntry> result = new List<ConsoleEntry>();
		using (RegistryKey root = Registry.CurrentUser.OpenSubKey(RegistryPath, false))
		{
			if (root == null)
				return result;
			using (RegistryKey hosts = root.OpenSubKey("registered_hosts", false))
			{
				if (hosts == null)
					return result;
				int size = ToInt(hosts.GetValue("size", 0), 0);
				for (int i = 1; i <= size; i++)
				{
					using (RegistryKey row = hosts.OpenSubKey(i.ToString(CultureInfo.InvariantCulture), false))
					{
						if (row == null)
							continue;
						int target = ToInt(row.GetValue("target", 0), 0);
						if (target < Ps5TargetMinimum)
							continue;
						object mac = row.GetValue("server_mac", null, RegistryValueOptions.DoNotExpandEnvironmentNames);
						if (mac == null)
							continue;
						RegistryValueKind kind = row.GetValueKind("server_mac");
						string name = row.GetValue("server_nickname", null) as string;
						if (string.IsNullOrWhiteSpace(name))
							name = "PS5";
						result.Add(new ConsoleEntry { Name = name, Mac = mac, MacKind = kind });
					}
				}
			}
		}
		return result;
	}

	private static int ToInt(object value, int fallback)
	{
		if (value is int)
			return (int)value;
		if (value is long)
			return (int)(long)value;
		string text = value as string;
		int parsed;
		if (text != null && int.TryParse(text.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out parsed))
			return parsed;
		return fallback;
	}

	private static string Str(object value)
	{
		return value == null ? null : Convert.ToString(value, CultureInfo.InvariantCulture);
	}

	private static bool ParseBool(string text, bool fallback)
	{
		if (text == null)
			return fallback;
		string t = text.Trim();
		return string.Equals(t, "true", StringComparison.OrdinalIgnoreCase) || t == "1";
	}

	private static bool Contains(Choice[] choices, string value)
	{
		foreach (Choice choice in choices)
			if (choice.Value == value)
				return true;
		return false;
	}

	private static string KnownOrNative(Choice[] choices, string value)
	{
		return Contains(choices, value) ? value : "~" + value;
	}

	private static string NativeDisplay(string value)
	{
		return "Native: " + value;
	}

	private Dictionary<string, string> ReadSavedValues()
	{
		Dictionary<string, string> saved = new Dictionary<string, string>();
		string preset = null;
		string mixer = null;
		string mapping = null;
		string resolution = null, rate = null, bitrate = null, codec = null, decoder = null, renderer = null, window = null, vsync = null, volume = null, buffer = null;
		using (RegistryKey s = Registry.CurrentUser.OpenSubKey(RegistryPath + @"\settings", false))
		{
			if (s != null)
			{
				resolution = Str(s.GetValue("resolution_local_ps5", null));
				rate = Str(s.GetValue("fps_local_ps5", null));
				bitrate = Str(s.GetValue("bitrate_local_ps5", null));
				codec = Str(s.GetValue("codec_local_ps5", null));
				decoder = Str(s.GetValue("hw_decoder", null));
				renderer = Str(s.GetValue("render_backend", null));
				window = Str(s.GetValue("window_type", null));
				vsync = Str(s.GetValue("vsync", null));
				volume = Str(s.GetValue("audio_volume", null));
				buffer = Str(s.GetValue("audio_buffer_size", null));
				preset = Str(s.GetValue("placebo_preset", null));
				mixer = Str(s.GetValue("placebo_frame_mixer", null));
				mapping = Str(s.GetValue("direct_frame_mapping", null));
			}
		}

		saved["resolution"] = KnownOrNative(ResolutionChoices, resolution ?? "1080p");
		string rateText = rate ?? "60";
		int rateNumber = ToInt(rateText, -1);
		saved["rate"] = rateNumber == 30 || rateNumber == 60 ? rateNumber.ToString(CultureInfo.InvariantCulture) : "~" + rateText;
		int bitrateNumber = ToInt(bitrate ?? "0", -1);
		saved["bitrate"] = bitrateNumber >= 0 ? bitrateNumber.ToString(CultureInfo.InvariantCulture) : NativeDisplay(bitrate);
		saved["codec"] = KnownOrNative(CodecChoices, codec ?? "h265");
		saved["decoder"] = KnownOrNative(DecoderChoices, decoder ?? "auto");
		saved["renderer"] = KnownOrNative(RendererChoices, renderer ?? "vulkan");
		saved["window"] = KnownOrNative(WindowChoices, window ?? "Fullscreen");
		saved["vsync"] = ParseBool(vsync, false) ? "true" : "false";

		string volumeText = volume ?? NativeVolumeMax.ToString(CultureInfo.InvariantCulture);
		int volumeNative = ToInt(volumeText, -1);
		saved["volume"] = volumeNative >= 0
			? ((int)Math.Round(volumeNative * 100.0 / NativeVolumeMax, MidpointRounding.AwayFromZero)).ToString(CultureInfo.InvariantCulture)
			: NativeDisplay(volumeText);

		string bufferText = buffer ?? "0";
		int bufferBytes = ToInt(bufferText, -1);
		if (bufferBytes == 0)
			saved["buffer"] = "0";
		else if (bufferBytes > 0 && bufferBytes % BytesPerMs == 0 && bufferBytes / BytesPerMs >= 10 && bufferBytes / BytesPerMs <= 100)
			saved["buffer"] = (bufferBytes / BytesPerMs).ToString(CultureInfo.InvariantCulture);
		else
			saved["buffer"] = NativeDisplay(bufferText + " B");

		string presetValue = preset ?? "fast";
		if (presetValue == "fast")
			saved["scaling"] = "direct";
		else if (presetValue == "custom")
		{
			string upscaler = null;
			using (RegistryKey p = Registry.CurrentUser.OpenSubKey(PlaceboPath, false))
			{
				if (p != null)
					upscaler = Str(p.GetValue("upscaler", null));
			}
			saved["scaling"] = upscaler != null && upscaler != "direct" && Contains(ScalingChoices, upscaler) ? upscaler : "~custom/" + (upscaler ?? "unset");
		}
		else
			saved["scaling"] = "~" + presetValue;

		string mixerValue = (mixer ?? "none").Trim().ToLowerInvariant();
		bool direct = ParseBool(mapping, true);
		if (direct)
			saved["mixer"] = mixerValue == "none" ? "direct" : "~" + mixerValue + " (direct mapping on)";
		else if (mixerValue == "none")
			saved["mixer"] = "queue";
		else if (mixerValue == "oversample" || mixerValue == "hermite" || mixerValue == "linear" || mixerValue == "cubic")
			saved["mixer"] = mixerValue;
		else
			saved["mixer"] = "~" + mixerValue;
		return saved;
	}

	private void LoadSettings()
	{
		_loading = true;
		try
		{
			Dictionary<string, string> saved = ReadSavedValues();
			_saved = saved;
			_settingsFailed = false;
			ShowCombo(_resolution, ResolutionChoices, saved["resolution"]);
			ShowCombo(_rate, RateChoices, saved["rate"]);
			ShowCombo(_codec, CodecChoices, saved["codec"]);
			ShowCombo(_decoder, DecoderChoices, saved["decoder"]);
			ShowCombo(_renderer, RendererChoices, saved["renderer"]);
			ShowCombo(_windowMode, WindowChoices, saved["window"]);
			ShowCombo(_scaling, ScalingChoices, saved["scaling"]);
			ShowCombo(_mixer, MixerChoices, saved["mixer"]);
			_bitrate.Text = saved["bitrate"];
			_volume.Text = saved["volume"];
			_audioBuffer.Text = saved["buffer"];
			_vsync.IsChecked = saved["vsync"] == "true";
			_sumResolution.Text = SummaryChoice(ResolutionChoices, saved["resolution"]);
			_sumRate.Text = SummaryChoice(RateChoices, saved["rate"]);
			_sumBitrate.Text = SummaryNumber(saved["bitrate"], "kbps");
			_sumDecoder.Text = SummaryChoice(DecoderChoices, saved["decoder"]);
			_sumScaling.Text = SummaryChoice(ScalingChoices, saved["scaling"]);
			_sumBuffer.Text = SummaryNumber(saved["buffer"], "ms");
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
			SetNotice("Stream settings could not be loaded: " + ex.Message + " Use Revert to retry.");
		}
		finally
		{
			_loading = false;
		}
		UpdateDraftState();
	}

	private static string SummaryChoice(Choice[] choices, string value)
	{
		if (value != null && value.StartsWith("~", StringComparison.Ordinal))
			return "Native";
		foreach (Choice choice in choices)
			if (choice.Value == value)
				return choice.Label;
		return "Native";
	}

	private static string SummaryNumber(string value, string unit)
	{
		if (value == null || value.StartsWith("Native:", StringComparison.Ordinal))
			return "Native";
		if (value == "0")
			return "Auto";
		return value + " " + unit;
	}

	private static void ShowCombo(ComboBox combo, Choice[] choices, string value)
	{
		List<Choice> items = new List<Choice>();
		if (value.StartsWith("~", StringComparison.Ordinal))
			items.Add(new Choice(value, NativeDisplay(value.Substring(1))));
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
		return ui;
	}

	private List<string> Changed(Dictionary<string, string> ui)
	{
		List<string> changed = new List<string>();
		if (_saved == null || _saved.Count == 0)
			return changed;
		foreach (string id in FieldOrder)
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
		List<string> changedIds = _settingsFailed || _saved == null ? new List<string>() : Changed(ReadUi());
		int count = changedIds.Count;
		foreach (Control control in _settingControls)
			control.IsEnabled = !running && !_settingsFailed;
		foreach (KeyValuePair<string, Control> pair in _fieldControls)
			pair.Value.Tag = changedIds.Contains(pair.Key) ? "changed" : null;
		_apply.IsEnabled = !running && !_settingsFailed && count > 0;
		_revert.IsEnabled = !running && (count > 0 || _settingsFailed);
		_apply.Content = count > 0 ? string.Format(CultureInfo.InvariantCulture, "Apply {0} change{1}", count, count == 1 ? string.Empty : "s") : "Apply";
		_revert.Content = "Revert";
	}

	private string Validate(Dictionary<string, string> ui, List<string> changed)
	{
		foreach (string id in changed)
		{
			string value = ui[id];
			string label = FieldLabels[id];
			switch (id)
			{
				case "resolution":
				case "rate":
				case "codec":
				case "decoder":
				case "renderer":
				case "window":
				case "scaling":
				case "mixer":
					if (value.Length == 0)
						return label + ": choose a value.";
					break;
				case "bitrate":
					{
						int n;
						if (!int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out n) || n < 0 || n > 100000)
							return "Bitrate: enter 0 for upstream auto or a whole number of kbps from 1 to 100000.";
						break;
					}
				case "volume":
					{
						int n;
						if (!int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out n) || n < 0 || n > 100)
							return "Volume: enter a whole number from 0 to 100.";
						break;
					}
				case "buffer":
					{
						int n;
						if (!int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out n) || !(n == 0 || (n >= 10 && n <= 100)))
							return "Audio buffer: enter 0 for auto or whole milliseconds from 10 to 100.";
						break;
					}
			}
		}
		if ((changed.Contains("renderer") || changed.Contains("codec")) && ui["renderer"] == "opengl" && ui["codec"] == "h265_hdr")
			return "Renderer and codec: OpenGL cannot display H.265 HDR. Pick another codec or the Vulkan renderer.";
		return null;
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
		string error = Validate(ui, changed);
		if (error != null)
		{
			SetNotice("Not saved. " + error);
			return;
		}
		try
		{
			EnsureBackendVerified();
			WriteChanges(ui, changed);
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

	private void WriteChanges(Dictionary<string, string> ui, List<string> changed)
	{
		using (RegistryKey root = Registry.CurrentUser.CreateSubKey(RegistryPath, RegistryKeyPermissionCheck.ReadWriteSubTree))
		using (RegistryKey s = root.CreateSubKey("settings", RegistryKeyPermissionCheck.ReadWriteSubTree))
		{
			foreach (string id in changed)
			{
				string value = ui[id];
				switch (id)
				{
					case "resolution":
						s.SetValue("resolution_local_ps5", value);
						break;
					case "rate":
						s.SetValue("fps_local_ps5", int.Parse(value, CultureInfo.InvariantCulture), RegistryValueKind.DWord);
						break;
					case "bitrate":
						s.SetValue("bitrate_local_ps5", int.Parse(value, CultureInfo.InvariantCulture), RegistryValueKind.DWord);
						break;
					case "codec":
						s.SetValue("codec_local_ps5", value);
						break;
					case "decoder":
						s.SetValue("hw_decoder", value);
						break;
					case "renderer":
						s.SetValue("render_backend", value);
						break;
					case "window":
						s.SetValue("window_type", value);
						break;
					case "vsync":
						s.SetValue("vsync", value);
						break;
					case "volume":
						s.SetValue("audio_volume", (int)Math.Round(128 * int.Parse(value, CultureInfo.InvariantCulture) / 100.0, MidpointRounding.AwayFromZero), RegistryValueKind.DWord);
						break;
					case "buffer":
						s.SetValue("audio_buffer_size", int.Parse(value, CultureInfo.InvariantCulture) * BytesPerMs, RegistryValueKind.DWord);
						break;
					case "scaling":
						if (value == "direct")
							s.SetValue("placebo_preset", "fast");
						else
						{
							WritePlaceboUpscaler(value);
							s.SetValue("placebo_preset", "custom");
						}
						break;
					case "mixer":
						if (value == "direct")
						{
							s.SetValue("placebo_frame_mixer", "none");
							s.SetValue("direct_frame_mapping", "true");
						}
						else
						{
							s.SetValue("placebo_frame_mixer", value == "queue" ? "none" : value);
							s.SetValue("direct_frame_mapping", "false");
						}
						break;
				}
			}
		}
	}

	private static void WritePlaceboUpscaler(string upscaler)
	{
		using (RegistryKey p = Registry.CurrentUser.CreateSubKey(PlaceboPath, RegistryKeyPermissionCheck.ReadWriteSubTree))
		{
			bool initialized = p.GetValue("version", null) != null;
			if (!initialized)
			{
				SetIfAbsent(p, "deinterlace", "no");
				SetIfAbsent(p, "deinterlace_preset", "default");
				SetIfAbsent(p, "deinterlace_algo", "yadif");
				SetIfAbsent(p, "deinterlace_skip_spatial", "no");
				SetIfAbsent(p, "deband", "yes");
				SetIfAbsent(p, "peak_detect_preset", "high_quality");
				SetIfAbsent(p, "color_map_preset", "high_quality");
				SetIfAbsent(p, "contrast_recovery", (0.3).ToString(CultureInfo.InvariantCulture));
				SetIfAbsent(p, "peak_percentile", (99.995).ToString(CultureInfo.InvariantCulture));
			}
			p.SetValue("upscaler", upscaler);
			if (!initialized)
				p.SetValue("version", "0");
		}
	}
}
