using System;
using System.Collections.Generic;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

internal static class LinkProbe
{
	private const int SampleCount = 500;
	private const int SampleSpacingMs = 20;
	private const int PingTimeoutMs = 500;

	// Native Wifi API (wlanapi.dll). Struct offsets come from the Windows SDK
	// wlanapi.h / windot11.h layouts; only scalar fields are read, never names.
	private const uint WlanClientVersion = 2;
	private const int WlanOpcodeCurrentConnection = 7;
	private const int WlanBssTypeAny = 3;
	private const int WlanInterfaceEntrySize = 532;
	private const int WlanInterfaceStateOffset = 528;
	private const int ConnAttributesSize = 588;
	private const int ConnAssocOffset = 520;
	private const int AssocBssidOffset = 40;
	private const int AssocPhyTypeOffset = 48;
	private const int AssocSignalOffset = 56;
	private const int AssocRxOffset = 60;
	private const int AssocTxOffset = 64;
	private const int BssListHeaderSize = 8;
	private const int BssEntryFixedSize = 360;
	private const int BssBssidOffset = 40;
	private const int BssRssiOffset = 56;
	private const int BssFrequencyOffset = 92;
	private const int BssIeOffsetPos = 352;
	private const int BssIeSizePos = 356;

	public sealed class Result
	{
		public string Kind = "Other";
		public string LinkLabel = "-";
		public int? SignalPercent;
		public int? Rssi;
		public long? RxKbps;
		public long? TxKbps;
		public double? Median;
		public int? P95;
		public int? Max;
		public double LossPercent;
		public int Spikes;
		public int? SuggestedKbps;
		public string[] Notes = new string[0];
	}

	private sealed class RouteInfo
	{
		public IPAddress Gateway;
		public string Kind;
		public string InterfaceId;
	}

	private sealed class WifiDetails
	{
		public int PhyType = -1;
		public int SignalQuality = -1;
		public long RxKbps;
		public long TxKbps;
		public byte[] Bssid;
		public int? Rssi;
		public string Band;
		public bool HasRadio;
	}

	[DllImport("iphlpapi.dll")]
	private static extern uint GetBestInterface(uint dwDestAddr, out uint pdwBestIfIndex);

	[DllImport("wlanapi.dll")]
	private static extern uint WlanOpenHandle(uint dwClientVersion, IntPtr pReserved, out uint pdwNegotiatedVersion, out IntPtr phClientHandle);

	[DllImport("wlanapi.dll")]
	private static extern uint WlanCloseHandle(IntPtr hClientHandle, IntPtr pReserved);

	[DllImport("wlanapi.dll")]
	private static extern uint WlanEnumInterfaces(IntPtr hClientHandle, IntPtr pReserved, out IntPtr ppInterfaceList);

	[DllImport("wlanapi.dll")]
	private static extern uint WlanQueryInterface(IntPtr hClientHandle, ref Guid pInterfaceGuid, int OpCode, IntPtr pReserved, out uint pdwDataSize, out IntPtr ppData, IntPtr pWlanOpcodeValueType);

	[DllImport("wlanapi.dll")]
	private static extern uint WlanGetNetworkBssList(IntPtr hClientHandle, ref Guid pInterfaceGuid, IntPtr pDot11Ssid, int dot11BssType, [MarshalAs(UnmanagedType.Bool)] bool bSecurityEnabled, IntPtr pReserved, out IntPtr ppWlanBssList);

	[DllImport("wlanapi.dll")]
	private static extern void WlanFreeMemory(IntPtr pMemory);

	public static async Task<Result> RunAsync(IProgress<int?> progress, CancellationToken token)
	{
		return await Task.Run(delegate { return ProbeCore(progress, token); }, token);
	}

	private static async Task<Result> ProbeCore(IProgress<int?> progress, CancellationToken token)
	{
		RouteInfo route = FindRoute();
		Result result = new Result { Kind = route.Kind };
		WifiDetails wifi = null;
		if (route.Kind == "Wi-Fi")
		{
			wifi = QueryWifi(route.InterfaceId);
			result.LinkLabel = wifi == null ? "Wi-Fi (details unavailable)" : WifiLabel(wifi);
			if (wifi != null)
			{
				result.SignalPercent = wifi.SignalQuality >= 0 && wifi.SignalQuality <= 100 ? wifi.SignalQuality : (int?)null;
				result.Rssi = wifi.Rssi;
				result.RxKbps = wifi.RxKbps > 0 ? wifi.RxKbps : (long?)null;
				result.TxKbps = wifi.TxKbps > 0 ? wifi.TxKbps : (long?)null;
			}
		}
		else
		{
			result.LinkLabel = route.Kind;
		}

		List<int?> samples = await SampleGatewayAsync(route.Gateway, progress, token);
		ComputeStats(samples, result);
		ApplySuggestion(result, route.Kind, wifi);
		return result;
	}

	private static RouteInfo FindRoute()
	{
		uint ifIndex;
		if (GetBestInterface(0x01010101, out ifIndex) != 0)
			throw new InvalidOperationException("Windows could not pick a route interface.");
		NetworkInterface match = null;
		foreach (NetworkInterface candidate in NetworkInterface.GetAllNetworkInterfaces())
		{
			try
			{
				IPv4InterfaceProperties v4 = candidate.GetIPProperties().GetIPv4Properties();
				if (v4 != null && v4.Index == (int)ifIndex)
				{
					match = candidate;
					break;
				}
			}
			catch (NetworkInformationException)
			{
			}
		}
		if (match == null)
			throw new InvalidOperationException("The interface carrying the default route was not found.");
		IPAddress gateway = null;
		foreach (GatewayIPAddressInformation g in match.GetIPProperties().GatewayAddresses)
		{
			if (g.Address.AddressFamily == AddressFamily.InterNetwork)
			{
				gateway = g.Address;
				break;
			}
		}
		if (gateway == null)
			throw new InvalidOperationException("No IPv4 gateway found on the default route interface.");
		string kind = match.NetworkInterfaceType == NetworkInterfaceType.Wireless80211 ? "Wi-Fi"
			: match.NetworkInterfaceType == NetworkInterfaceType.Ethernet ? "Ethernet" : "Other";
		return new RouteInfo { Gateway = gateway, Kind = kind, InterfaceId = match.Id };
	}

	private static WifiDetails QueryWifi(string interfaceId)
	{
		IntPtr handle = IntPtr.Zero;
		IntPtr list = IntPtr.Zero;
		try
		{
			uint negotiated;
			if (WlanOpenHandle(WlanClientVersion, IntPtr.Zero, out negotiated, out handle) != 0 || handle == IntPtr.Zero)
				return null;
			if (WlanEnumInterfaces(handle, IntPtr.Zero, out list) != 0 || list == IntPtr.Zero)
				return null;
			int count = Marshal.ReadInt32(list, 0);
			Guid target = Guid.Empty;
			Guid parsed;
			if (!string.IsNullOrEmpty(interfaceId) && Guid.TryParse(interfaceId, out parsed))
				target = parsed;
			Guid selected = Guid.Empty;
			Guid fallback = Guid.Empty;
			for (int i = 0; i < count; i++)
			{
				int entry = 8 + i * WlanInterfaceEntrySize;
				byte[] guidBytes = new byte[16];
				Marshal.Copy(IntPtr.Add(list, entry), guidBytes, 0, 16);
				Guid guid = new Guid(guidBytes);
				if (target != Guid.Empty && guid == target)
				{
					selected = guid;
					break;
				}
				if (fallback == Guid.Empty && Marshal.ReadInt32(list, entry + WlanInterfaceStateOffset) == 1)
					fallback = guid;
			}
			if (selected == Guid.Empty)
				selected = fallback;
			if (selected == Guid.Empty)
				return null;

			WifiDetails wifi = new WifiDetails();
			QueryConnection(handle, selected, wifi);
			if (wifi.HasRadio && wifi.Bssid != null)
				QueryBss(handle, selected, wifi);
			return wifi;
		}
		catch (ArgumentException)
		{
			// Only Marshal and Guid argument validation can fail here; WLAN calls return codes.
			return null;
		}
		finally
		{
			if (list != IntPtr.Zero)
				WlanFreeMemory(list);
			if (handle != IntPtr.Zero)
				WlanCloseHandle(handle, IntPtr.Zero);
		}
	}

	private static void QueryConnection(IntPtr handle, Guid guid, WifiDetails wifi)
	{
		uint size;
		IntPtr data;
		if (WlanQueryInterface(handle, ref guid, WlanOpcodeCurrentConnection, IntPtr.Zero, out size, out data, IntPtr.Zero) != 0 || data == IntPtr.Zero)
			return;
		try
		{
			if (size < (uint)ConnAttributesSize)
				return;
			if (Marshal.ReadInt32(data, 0) != 1)
				return;
			int assoc = ConnAssocOffset;
			wifi.PhyType = Marshal.ReadInt32(data, assoc + AssocPhyTypeOffset);
			wifi.SignalQuality = Marshal.ReadInt32(data, assoc + AssocSignalOffset);
			wifi.RxKbps = Marshal.ReadInt32(data, assoc + AssocRxOffset);
			wifi.TxKbps = Marshal.ReadInt32(data, assoc + AssocTxOffset);
			wifi.Bssid = new byte[6];
			Marshal.Copy(IntPtr.Add(data, assoc + AssocBssidOffset), wifi.Bssid, 0, 6);
			wifi.HasRadio = true;
		}
		finally
		{
			WlanFreeMemory(data);
		}
	}

	private static void QueryBss(IntPtr handle, Guid guid, WifiDetails wifi)
	{
		IntPtr list;
		if (WlanGetNetworkBssList(handle, ref guid, IntPtr.Zero, WlanBssTypeAny, false, IntPtr.Zero, out list) != 0 || list == IntPtr.Zero)
			return;
		try
		{
			int total = Marshal.ReadInt32(list, 0);
			int count = Marshal.ReadInt32(list, 4);
			int pos = BssListHeaderSize;
			for (int i = 0; i < count && pos + BssEntryFixedSize <= total; i++)
			{
				bool match = true;
				for (int b = 0; b < 6; b++)
				{
					if (Marshal.ReadByte(list, pos + BssBssidOffset + b) != wifi.Bssid[b])
					{
						match = false;
						break;
					}
				}
				if (match)
				{
					wifi.Rssi = Marshal.ReadInt32(list, pos + BssRssiOffset);
					uint frequency = (uint)Marshal.ReadInt32(list, pos + BssFrequencyOffset);
					wifi.Band = BandFromFrequency(frequency);
					return;
				}
				int ieOffset = Marshal.ReadInt32(list, pos + BssIeOffsetPos);
				int ieSize = Marshal.ReadInt32(list, pos + BssIeSizePos);
				int stride = ieOffset + ieSize;
				if (stride < BssEntryFixedSize || pos + stride > total)
					stride = BssEntryFixedSize;
				pos += stride;
			}
		}
		finally
		{
			WlanFreeMemory(list);
		}
	}

	private static string BandFromFrequency(uint khz)
	{
		if (khz == 0)
			return null;
		if (khz < 3000000)
			return "2.4 GHz";
		if (khz < 5925000)
			return "5 GHz";
		return "6 GHz";
	}

	private static string PhyGeneration(int phyType)
	{
		switch (phyType)
		{
			case 7: return "Wi-Fi 4";
			case 8: return "Wi-Fi 5";
			case 10: return "Wi-Fi 6";
			case 11: return "Wi-Fi 7";
			default: return null;
		}
	}

	private static string WifiLabel(WifiDetails wifi)
	{
		if (wifi == null || !wifi.HasRadio)
			return "Wi-Fi";
		string first = PhyGeneration(wifi.PhyType) ?? "Wi-Fi";
		return wifi.Band != null ? first + " · " + wifi.Band : first;
	}

	private static async Task<List<int?>> SampleGatewayAsync(IPAddress gateway, IProgress<int?> progress, CancellationToken token)
	{
		List<Task<PingReply>> sends = new List<Task<PingReply>>(SampleCount);
		using (Ping ping = new Ping())
		{
			for (int i = 0; i < SampleCount; i++)
			{
				token.ThrowIfCancellationRequested();
				sends.Add(SendPingAsync(ping, gateway));
				await Task.Delay(SampleSpacingMs, token);
			}
			List<int?> samples = new List<int?>(SampleCount);
			foreach (Task<PingReply> send in sends)
			{
				token.ThrowIfCancellationRequested();
				int? rtt = null;
				PingReply reply = await send;
				if (reply != null && reply.Status == IPStatus.Success)
					rtt = (int)reply.RoundtripTime;
				samples.Add(rtt);
				if (progress != null)
					progress.Report(rtt);
			}
			return samples;
		}
	}

	private static async Task<PingReply> SendPingAsync(Ping ping, IPAddress gateway)
	{
		try
		{
			return await ping.SendPingAsync(gateway, PingTimeoutMs);
		}
		catch (PingException)
		{
			return null;
		}
		catch (InvalidOperationException)
		{
			return null;
		}
	}

	private static void ComputeStats(List<int?> samples, Result result)
	{
		List<int> ok = new List<int>(samples.Count);
		foreach (int? sample in samples)
			if (sample.HasValue)
				ok.Add(sample.Value);
		ok.Sort();
		result.LossPercent = (SampleCount - ok.Count) * 100.0 / SampleCount;
		if (ok.Count == 0)
			return;
		int n = ok.Count;
		result.Median = n % 2 == 1 ? ok[n / 2] : (ok[n / 2 - 1] + ok[n / 2]) / 2.0;
		result.P95 = ok[(int)Math.Ceiling(0.95 * n) - 1];
		result.Max = ok[n - 1];
		double threshold = Math.Max(3.0 * result.Median.Value, result.Median.Value + 15.0);
		foreach (int value in ok)
			if (value > threshold)
				result.Spikes++;
	}

	private static void ApplySuggestion(Result result, string kind, WifiDetails wifi)
	{
		if (kind == "Ethernet")
		{
			result.Notes = new[] { "This PC is wired. The router hop is unlikely to limit the stream bitrate." };
			return;
		}
		if (kind != "Wi-Fi")
			return;
		List<string> notes = new List<string>();
		if (wifi != null && wifi.Band == "2.4 GHz")
			notes.Add("This is 2.4 GHz Wi-Fi, which is often crowded. Use 5 or 6 GHz if available.");
		if (result.Spikes > 5)
			notes.Add("Delay jumped during the test. Other Wi-Fi devices or interference can cause this.");
		if (result.LossPercent > 2.0)
			notes.Add("Some router replies did not come back. Some routers limit ping, so treat this as a hint.");
		if (wifi != null && wifi.SignalQuality >= 0 && wifi.SignalQuality < 50)
			notes.Add("The Wi-Fi signal is weak. Move closer to the router or use a cable.");
		if (notes.Count > 2)
			notes.RemoveRange(2, notes.Count - 2);
		result.Notes = notes.ToArray();
		if (wifi == null || wifi.RxKbps <= 0)
			return;
		long ceiling = (long)Math.Floor(wifi.RxKbps * 0.25 / 500.0) * 500;
		ceiling = Math.Max(3000, Math.Min(50000, ceiling));
		bool unstable = result.LossPercent > 2.0
			|| (result.P95.HasValue && result.Median.HasValue && result.P95.Value - result.Median.Value > 20.0);
		if (unstable)
			ceiling = Math.Max(3000, ceiling / 2);
		result.SuggestedKbps = (int)ceiling;
	}
}
