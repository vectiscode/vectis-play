using System;
using System.Collections.Generic;
using System.Globalization;
using Microsoft.Win32;

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

internal sealed class KeyMapEntry
{
	public string Button;
	public string Key;
}

internal static class StreamSettings
{
	internal const string RegistryPath = @"Software\Vectis\Vectis";
	internal const string PlaceboPath = @"Software\Vectis\pl_render_params\placebo_settings";
	private const int Ps5TargetMinimum = 1000000;
	private const int BytesPerMs = 192;
	private const int NativeVolumeMax = 128;

	internal static readonly string[] FieldOrder = new[]
	{
		"resolution", "rate", "bitrate", "codec", "decoder", "renderer", "window", "vsync", "volume", "buffer", "scaling", "mixer",
		"keyboard", "walkkey", "walkscale", "mouseaim", "aimsens", "aimyratio", "aimdeadzone", "aimcurve", "aimwindow", "aimtau", "aimrelease",
		"colorsat", "colorvib", "colorcon", "colorbri", "colortemp", "colortint"
	};

	internal static readonly Dictionary<string, string> FieldLabels = new Dictionary<string, string>
	{
		{ "resolution", "Resolution" }, { "rate", "Frame rate" }, { "bitrate", "Bitrate" }, { "codec", "Codec" },
		{ "decoder", "Decoder" }, { "renderer", "Renderer" }, { "window", "Window mode" }, { "vsync", "VSync" },
		{ "volume", "Volume" }, { "buffer", "Audio buffer" }, { "scaling", "Upscaling" }, { "mixer", "Frame mixer" },
		{ "keyboard", "Keyboard" }, { "walkkey", "Walk key" }, { "walkscale", "Walk speed" },
		{ "mouseaim", "Mouse aim" }, { "aimsens", "Sensitivity" }, { "aimyratio", "Vertical ratio" },
		{ "aimdeadzone", "Deadzone" }, { "aimcurve", "Curve" }, { "aimwindow", "Smoothing window" },
		{ "aimtau", "Stop easing" }, { "aimrelease", "Release key" },
		{ "colorsat", "Saturation" }, { "colorvib", "Vibrance" }, { "colorcon", "Contrast" },
		{ "colorbri", "Brightness" }, { "colortemp", "Temperature" }, { "colortint", "Tint" }
	};

	internal static readonly Choice[] ResolutionChoices = { new Choice("360p", "360p"), new Choice("540p", "540p"), new Choice("720p", "720p"), new Choice("1080p", "1080p") };
	internal static readonly Choice[] RateChoices = { new Choice("30", "30 fps"), new Choice("60", "60 fps") };
	internal static readonly Choice[] CodecChoices = { new Choice("h264", "H.264"), new Choice("h265", "H.265"), new Choice("h265_hdr", "H.265 HDR") };
	internal static readonly Choice[] DecoderChoices = { new Choice("auto", "Automatic"), new Choice("vulkan", "Vulkan Video"), new Choice("d3d11va", "D3D11VA"), new Choice("cuda", "CUDA"), new Choice("none", "Software") };
	internal static readonly Choice[] RendererChoices = { new Choice("vulkan", "Vulkan"), new Choice("opengl", "OpenGL") };
	internal static readonly Choice[] WindowChoices =
	{
		new Choice("Selected Resolution", "Selected resolution"), new Choice("Adjust Manually", "Adjust manually"),
		new Choice("Fullscreen", "Fullscreen"), new Choice("Zoom", "Zoom"), new Choice("Stretch", "Stretch")
	};
	internal static readonly Choice[] ScalingChoices =
	{
		new Choice("direct", "Direct (fast)"), new Choice("nearest", "Nearest"), new Choice("bilinear", "Bilinear"),
		new Choice("bicubic", "Bicubic"), new Choice("gaussian", "Gaussian"), new Choice("catmull_rom", "Catmull-Rom"),
		new Choice("lanczos", "Lanczos"), new Choice("ewa_lanczos", "EWA Lanczos"), new Choice("ewa_lanczossharp", "EWA Lanczos sharp"),
		new Choice("ewa_lanczos4sharpest", "EWA Lanczos 4 sharpest"), new Choice("fsr", "FSR"),
		new Choice("fsrcnnx_x2_8_0_4_1", "FSRCNNX 8"), new Choice("fsrcnnx_x2_16_0_4_1", "FSRCNNX 16")
	};
	internal static readonly Choice[] MixerChoices =
	{
		new Choice("direct", "Direct"), new Choice("queue", "Queue without blend"), new Choice("oversample", "Oversample"),
		new Choice("hermite", "Hermite"), new Choice("linear", "Linear"), new Choice("cubic", "Cubic")
	};

	// Qt::Key values for the keys the workbench offers. Anything else stored in
	// the registry shows as a native value and is left untouched.
	internal static readonly Choice[] WalkKeyChoices =
	{
		new Choice("16777251", "Alt"), new Choice("16777249", "Ctrl"), new Choice("16777252", "Caps Lock"),
		new Choice("67", "C"), new Choice("86", "V")
	};
	internal static readonly Choice[] AimReleaseChoices =
	{
		new Choice("16777271", "F8"), new Choice("16777272", "F9"), new Choice("16777273", "F10"),
		new Choice("16777254", "Scroll Lock"), new Choice("16777224", "Pause")
	};

	// Upstream stores each PS5 button map as a QKeySequence string under
	// keymap/<button name>, which is too rich for an in-app editor. The
	// Controls tab shows this read-only table and hands editing to chiaki-ng.
	// Columns: display name, registry value name, Vectis default (a QKeySequence
	// string). The defaults are a WASD layout written once on first use.
	private static readonly string[][] KeyMapRows =
	{
		new[] { "Left stick up", "left_stick_up", "W" },
		new[] { "Left stick left", "left_stick_left", "A" },
		new[] { "Left stick down", "left_stick_down", "S" },
		new[] { "Left stick right", "left_stick_right", "D" },
		new[] { "Cross", "cross", "Space" },
		new[] { "Circle", "moon", "C" },
		new[] { "Square", "box", "R" },
		new[] { "Triangle", "pyramid", "F" },
		new[] { "L1", "l1", "Q" },
		new[] { "R1", "r1", "E" },
		new[] { "L2", "l2", "1" },
		new[] { "R2", "r2", "4" },
		new[] { "L3", "l3", "Shift" },
		new[] { "R3", "r3", "V" },
		new[] { "D-Pad Up", "d-pad_up", "Up" },
		new[] { "D-Pad Down", "d-pad_down", "Down" },
		new[] { "D-Pad Left", "d-pad_left", "Left" },
		new[] { "D-Pad Right", "d-pad_right", "Right" },
		new[] { "Options", "options", "Esc" },
		new[] { "Create", "share", "Backspace" },
		new[] { "Touchpad", "touchpad", "Tab" },
		new[] { "PS", "ps", "Home" }
	};

	// Upstream stores the key map at the root as keymap/<name>. Write the WASD
	// layout only when no key map exists, so edits made in chiaki-ng stay.
	internal static void WriteDefaultKeyMap(RegistryKey root)
	{
		using (RegistryKey existing = root.OpenSubKey("keymap", false))
		{
			if (existing != null)
				return;
		}
		using (RegistryKey keymap = root.CreateSubKey("keymap", RegistryKeyPermissionCheck.ReadWriteSubTree))
		{
			foreach (string[] row in KeyMapRows)
				keymap.SetValue(row[1], row[2], RegistryValueKind.String);
		}
	}

	// Vectis keeps its own registration store so a stock chiaki-ng install is
	// untouched. On first run, adopt an existing stock registration (for
	// example one made through Veyra or chiaki-ng itself) so the console does
	// not have to be paired twice. The marker prevents re-importing after the
	// user removes the console inside Vectis.
	private const string LegacyRegistryPath = @"Software\Chiaki\Chiaki";

	internal static void ImportLegacyRegistration()
	{
		using (RegistryKey root = Registry.CurrentUser.CreateSubKey(RegistryPath, RegistryKeyPermissionCheck.ReadWriteSubTree))
		{
			if (root.GetValue("legacy_registration_imported", null) != null)
				return;
			root.SetValue("legacy_registration_imported", "1", RegistryValueKind.String);
			if (root.OpenSubKey("registered_hosts", false) != null)
				return;
			using (RegistryKey legacy = Registry.CurrentUser.OpenSubKey(LegacyRegistryPath + @"\registered_hosts", false))
			{
				if (legacy == null)
					return;
				using (RegistryKey target = root.CreateSubKey("registered_hosts", RegistryKeyPermissionCheck.ReadWriteSubTree))
					CopyKey(legacy, target);
			}
		}
	}

	private static void CopyKey(RegistryKey source, RegistryKey target)
	{
		foreach (string name in source.GetValueNames())
			target.SetValue(name, source.GetValue(name, null, RegistryValueOptions.DoNotExpandEnvironmentNames), source.GetValueKind(name));
		foreach (string sub in source.GetSubKeyNames())
		{
			using (RegistryKey src = source.OpenSubKey(sub, false))
			using (RegistryKey dst = target.CreateSubKey(sub, RegistryKeyPermissionCheck.ReadWriteSubTree))
				CopyKey(src, dst);
		}
	}

	internal static List<ConsoleEntry> ReadConsoles()
	{
		ImportLegacyRegistration();
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

	internal static int ToInt(object value, int fallback)
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

	internal static string Str(object value)
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

	private static bool TryDouble(string text, out double value)
	{
		return double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value)
			&& !double.IsNaN(value) && !double.IsInfinity(value);
	}

	// Reads a stored fraction (for example 0.45) and shows it as a percent
	// string (45) for the draft fields that display percentages.
	private static string ReadPercentField(string raw, double min, double max, string fallback)
	{
		if (raw == null)
			return fallback;
		double d;
		if (!TryDouble(raw, out d) || d < min || d > max)
			return NativeDisplay(raw);
		return (d * 100.0).ToString("0.########", CultureInfo.InvariantCulture);
	}

	private static string ReadDoubleField(string raw, double min, double max, string fallback)
	{
		if (raw == null)
			return fallback;
		double d;
		if (!TryDouble(raw, out d) || d < min || d > max)
			return NativeDisplay(raw);
		return d.ToString("0.########", CultureInfo.InvariantCulture);
	}

	private static string ReadIntField(string raw, int min, int max, string fallback)
	{
		if (raw == null)
			return fallback;
		int n = ToInt(raw, int.MinValue);
		if (n == int.MinValue || n < min || n > max)
			return NativeDisplay(raw);
		return n.ToString(CultureInfo.InvariantCulture);
	}

	// Canonical stored form for the double fields: invariant string, up to 8
	// decimals with trailing zeros trimmed so a saved draft reads back equal.
	private static string CanonicalDouble(string text)
	{
		double d = double.Parse(text.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture);
		return d.ToString("0.########", CultureInfo.InvariantCulture);
	}

	private static string CanonicalScaled(string text, double divisor)
	{
		double d = double.Parse(text.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture) / divisor;
		return d.ToString("0.########", CultureInfo.InvariantCulture);
	}

	internal static string NativeDisplay(string value)
	{
		return "Native: " + value;
	}

	internal static void SetIfAbsent(RegistryKey key, string name, string value)
	{
		if (key.GetValue(name, null) == null)
			key.SetValue(name, value);
	}

	internal static Dictionary<string, string> ReadSavedValues()
	{
		Dictionary<string, string> saved = new Dictionary<string, string>();
		string preset = null;
		string mixer = null;
		string mapping = null;
		string resolution = null, rate = null, bitrate = null, codec = null, decoder = null, renderer = null, window = null, vsync = null, volume = null, buffer = null;
		string kbEnabled = null, walkKey = null, walkScale = null, aimEnabled = null;
		string aimSens = null, aimYRatio = null, aimDeadzone = null, aimCurve = null, aimWindow = null, aimTau = null, aimRelease = null;
		string colorSat = null, colorVib = null, colorCon = null, colorBri = null, colorTemp = null, colorTint = null;
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
				kbEnabled = Str(s.GetValue("keyboard_enabled", null));
				walkKey = Str(s.GetValue("keyboard_walk_key", null));
				walkScale = Str(s.GetValue("keyboard_walk_scale", null));
				aimEnabled = Str(s.GetValue("mouse_aim_enabled", null));
				aimSens = Str(s.GetValue("mouse_aim_sensitivity", null));
				aimYRatio = Str(s.GetValue("mouse_aim_y_ratio", null));
				aimDeadzone = Str(s.GetValue("mouse_aim_deadzone", null));
				aimCurve = Str(s.GetValue("mouse_aim_curve", null));
				aimWindow = Str(s.GetValue("mouse_aim_window_ms", null));
				aimTau = Str(s.GetValue("mouse_aim_stop_tau_ms", null));
				aimRelease = Str(s.GetValue("mouse_aim_release_key", null));
				colorSat = Str(s.GetValue("color_saturation", null));
				colorVib = Str(s.GetValue("color_vibrance", null));
				colorCon = Str(s.GetValue("color_contrast", null));
				colorBri = Str(s.GetValue("color_brightness", null));
				colorTemp = Str(s.GetValue("color_temperature", null));
				colorTint = Str(s.GetValue("color_tint", null));
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

		saved["keyboard"] = ParseBool(kbEnabled, true) ? "true" : "false";
		saved["walkkey"] = KnownOrNative(WalkKeyChoices, walkKey ?? "16777251");
		saved["walkscale"] = ReadPercentField(walkScale, 0.2, 0.8, "45");
		saved["mouseaim"] = ParseBool(aimEnabled, false) ? "true" : "false";
		saved["aimsens"] = ReadDoubleField(aimSens, 0.1, 5.0, "1");
		saved["aimyratio"] = ReadDoubleField(aimYRatio, 0.25, 4.0, "1");
		saved["aimdeadzone"] = ReadPercentField(aimDeadzone, 0.0, 0.5, "12");
		saved["aimcurve"] = ReadDoubleField(aimCurve, 0.3, 3.0, "0.5");
		saved["aimwindow"] = ReadIntField(aimWindow, 2, 32, "8");
		saved["aimtau"] = ReadIntField(aimTau, 0, 50, "12");
		saved["aimrelease"] = KnownOrNative(AimReleaseChoices, aimRelease ?? "16777271");
		saved["colorsat"] = ReadPercentField(colorSat, 0.0, 2.0, "100");
		saved["colorvib"] = ReadPercentField(colorVib, -1.0, 1.0, "0");
		saved["colorcon"] = ReadPercentField(colorCon, 0.5, 2.0, "100");
		saved["colorbri"] = ReadPercentField(colorBri, -0.5, 0.5, "0");
		saved["colortemp"] = ReadPercentField(colorTemp, -1.0, 1.0, "0");
		saved["colortint"] = ReadPercentField(colorTint, -1.0, 1.0, "0");
		return saved;
	}

	// Read-only view of the upstream keymap table for the Controls tab. Stored
	// QKeySequence strings are shown verbatim (first sequence only, matching
	// how upstream reads them); absent values fall back to the upstream
	// default key name.
	internal static List<KeyMapEntry> ReadKeyMap()
	{
		List<KeyMapEntry> rows = new List<KeyMapEntry>();
		Dictionary<string, string> stored = new Dictionary<string, string>();
		using (RegistryKey s = Registry.CurrentUser.OpenSubKey(RegistryPath + @"\keymap", false))
		{
			if (s != null)
				foreach (string name in s.GetValueNames())
				{
					string text = Str(s.GetValue(name, null, RegistryValueOptions.DoNotExpandEnvironmentNames));
					if (!string.IsNullOrWhiteSpace(text))
						stored[name] = text.Trim();
				}
		}
		foreach (string[] row in KeyMapRows)
		{
			string key;
			if (stored.TryGetValue(row[1], out key))
			{
				int comma = key.IndexOf(',');
				if (comma >= 0)
					key = key.Substring(0, comma).Trim();
				if (key.Length == 0)
					key = null;
			}
			rows.Add(new KeyMapEntry { Button = row[0], Key = key ?? row[2] });
		}
		return rows;
	}

	internal static string SummaryChoice(Choice[] choices, string value)
	{
		if (value != null && value.StartsWith("~", StringComparison.Ordinal))
			return "Native";
		foreach (Choice choice in choices)
			if (choice.Value == value)
				return choice.Label;
		return "Native";
	}

	internal static string SummaryNumber(string value, string unit)
	{
		if (value == null || value.StartsWith("Native:", StringComparison.Ordinal))
			return "Native";
		if (value == "0")
			return "Auto";
		return value + " " + unit;
	}

	internal static string Validate(Dictionary<string, string> ui, List<string> changed)
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
				case "walkkey":
				case "aimrelease":
					if (value.Length == 0)
						return label + ": choose a value.";
					break;
				case "walkscale":
					{
						double n;
						if (!TryDouble(value, out n) || n < 20 || n > 80)
							return "Walk speed: enter a percentage from 20 to 80.";
						break;
					}
				case "aimsens":
					{
						double n;
						if (!TryDouble(value, out n) || n < 0.1 || n > 5)
							return "Sensitivity: enter a number from 0.1 to 5.";
						break;
					}
				case "aimyratio":
					{
						double n;
						if (!TryDouble(value, out n) || n < 0.25 || n > 4)
							return "Vertical ratio: enter a number from 0.25 to 4.";
						break;
					}
				case "aimdeadzone":
					{
						double n;
						if (!TryDouble(value, out n) || n < 0 || n > 50)
							return "Deadzone: enter a percentage from 0 to 50.";
						break;
					}
				case "aimcurve":
					{
						double n;
						if (!TryDouble(value, out n) || n < 0.3 || n > 3)
							return "Curve: enter a number from 0.3 to 3.";
						break;
					}
				case "aimwindow":
					{
						int n;
						if (!int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out n) || n < 2 || n > 32)
							return "Smoothing window: enter whole milliseconds from 2 to 32.";
						break;
					}
				case "aimtau":
					{
						int n;
						if (!int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out n) || n < 0 || n > 50)
							return "Stop easing: enter whole milliseconds from 0 to 50.";
						break;
					}
				case "colorsat":
					{
						double n;
						if (!TryDouble(value, out n) || n < 0 || n > 200)
							return "Saturation: enter a percentage from 0 to 200.";
						break;
					}
				case "colorvib":
					{
						double n;
						if (!TryDouble(value, out n) || n < -100 || n > 100)
							return "Vibrance: enter a percentage from -100 to 100.";
						break;
					}
				case "colorcon":
					{
						double n;
						if (!TryDouble(value, out n) || n < 50 || n > 200)
							return "Contrast: enter a percentage from 50 to 200.";
						break;
					}
				case "colorbri":
					{
						double n;
						if (!TryDouble(value, out n) || n < -50 || n > 50)
							return "Brightness: enter a percentage from -50 to 50.";
						break;
					}
				case "colortemp":
				case "colortint":
					{
						double n;
						if (!TryDouble(value, out n) || n < -100 || n > 100)
							return FieldLabels[id] + ": enter a number from -100 to 100.";
						break;
					}
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

	internal static void WriteChanges(Dictionary<string, string> ui, List<string> changed)
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
					case "keyboard":
						s.SetValue("keyboard_enabled", value);
						break;
					case "walkkey":
						s.SetValue("keyboard_walk_key", int.Parse(value, CultureInfo.InvariantCulture), RegistryValueKind.DWord);
						break;
					case "walkscale":
						s.SetValue("keyboard_walk_scale", CanonicalScaled(value, 100.0));
						break;
					case "mouseaim":
						s.SetValue("mouse_aim_enabled", value);
						break;
					case "aimsens":
						s.SetValue("mouse_aim_sensitivity", CanonicalDouble(value));
						break;
					case "aimyratio":
						s.SetValue("mouse_aim_y_ratio", CanonicalDouble(value));
						break;
					case "aimdeadzone":
						s.SetValue("mouse_aim_deadzone", CanonicalScaled(value, 100.0));
						break;
					case "aimcurve":
						s.SetValue("mouse_aim_curve", CanonicalDouble(value));
						break;
					case "aimwindow":
						s.SetValue("mouse_aim_window_ms", int.Parse(value, CultureInfo.InvariantCulture), RegistryValueKind.DWord);
						break;
					case "aimtau":
						s.SetValue("mouse_aim_stop_tau_ms", int.Parse(value, CultureInfo.InvariantCulture), RegistryValueKind.DWord);
						break;
					case "aimrelease":
						s.SetValue("mouse_aim_release_key", int.Parse(value, CultureInfo.InvariantCulture), RegistryValueKind.DWord);
						break;
					case "colorsat":
						s.SetValue("color_saturation", CanonicalScaled(value, 100.0));
						break;
					case "colorvib":
						s.SetValue("color_vibrance", CanonicalScaled(value, 100.0));
						break;
					case "colorcon":
						s.SetValue("color_contrast", CanonicalScaled(value, 100.0));
						break;
					case "colorbri":
						s.SetValue("color_brightness", CanonicalScaled(value, 100.0));
						break;
					case "colortemp":
						s.SetValue("color_temperature", CanonicalScaled(value, 100.0));
						break;
					case "colortint":
						s.SetValue("color_tint", CanonicalScaled(value, 100.0));
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
