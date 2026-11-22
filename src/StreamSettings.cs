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

internal static class StreamSettings
{
	internal const string RegistryPath = @"Software\Vectis\Vectis";
	internal const string PlaceboPath = @"Software\Vectis\pl_render_params\placebo_settings";
	private const int Ps5TargetMinimum = 1000000;
	private const int BytesPerMs = 192;
	private const int NativeVolumeMax = 128;

	internal static readonly string[] FieldOrder = new[] { "resolution", "rate", "bitrate", "codec", "decoder", "renderer", "window", "vsync", "volume", "buffer", "scaling", "mixer" };

	internal static readonly Dictionary<string, string> FieldLabels = new Dictionary<string, string>
	{
		{ "resolution", "Resolution" }, { "rate", "Frame rate" }, { "bitrate", "Bitrate" }, { "codec", "Codec" },
		{ "decoder", "Decoder" }, { "renderer", "Renderer" }, { "window", "Window mode" }, { "vsync", "VSync" },
		{ "volume", "Volume" }, { "buffer", "Audio buffer" }, { "scaling", "Upscaling" }, { "mixer", "Frame mixer" }
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

	internal static List<ConsoleEntry> ReadConsoles()
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
