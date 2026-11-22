using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Shapes;

// Local keyboard-and-mouse preview for the Controls tab. Owns the response
// graph, the aim test pad, the WM_INPUT capture and the InputMath instance.
// Everything here stays on this PC: captured deltas only move the on-screen
// stick dot and are never sent to the backend or a console.
internal sealed class AimPad
{
	private const int WmInput = 0x00FF;
	private const uint RidInput = 0x10000003;
	private const uint RimTypeMouse = 0;
	private const uint RidevRemove = 0x00000001;
	private const ushort MouseMoveAbsolute = 0x0001;

	[StructLayout(LayoutKind.Sequential)]
	private struct RawInputDevice
	{
		public ushort UsagePage;
		public ushort Usage;
		public uint Flags;
		public IntPtr Target;
	}

	[StructLayout(LayoutKind.Sequential)]
	private struct RawInputHeader
	{
		public uint Type;
		public uint Size;
		public IntPtr Device;
		public IntPtr Message;
	}

	[StructLayout(LayoutKind.Sequential)]
	private struct RawMouse
	{
		public ushort Flags;
		public uint Buttons;
		public uint RawButtons;
		public int LastX;
		public int LastY;
		public UIntPtr Extra;
	}

	[StructLayout(LayoutKind.Sequential)]
	private struct RawInput
	{
		public RawInputHeader Header;
		public RawMouse Mouse;
	}

	[StructLayout(LayoutKind.Sequential)]
	private struct Rect
	{
		public int Left;
		public int Top;
		public int Right;
		public int Bottom;
	}

	[DllImport("user32.dll", SetLastError = true)]
	private static extern bool RegisterRawInputDevices(RawInputDevice[] devices, uint count, uint size);

	[DllImport("user32.dll")]
	private static extern uint GetRawInputData(IntPtr rawInput, uint command, IntPtr data, ref uint size, uint headerSize);

	[DllImport("user32.dll")]
	private static extern bool ClipCursor(IntPtr rect);

	private static readonly Brush BrushMuted = Frozen(new SolidColorBrush(Color.FromRgb(0xA8, 0x9F, 0x98)));
	private static readonly Brush BrushSignal = Frozen(new SolidColorBrush(Color.FromRgb(0xFF, 0x5B, 0x4B)));
	private static readonly Brush BrushLine = Frozen(new SolidColorBrush(Color.FromRgb(0x2B, 0x27, 0x24)));
	private static readonly Brush BrushPaper = Frozen(new SolidColorBrush(Color.FromRgb(0xF2, 0xEC, 0xE6)));

	private static Brush Frozen(Brush brush)
	{
		brush.Freeze();
		return brush;
	}

	private static readonly Dictionary<int, Key> ReleaseKeys = new Dictionary<int, Key>
	{
		{ 16777271, Key.F8 },
		{ 16777272, Key.F9 },
		{ 16777273, Key.F10 },
		{ 16777254, Key.Scroll },
		{ 16777224, Key.Pause }
	};

	private readonly Window _win;
	private readonly Canvas _graph;
	private readonly Canvas _pad;
	private readonly TextBlock _speed;
	private readonly TextBlock _stick;
	private readonly InputMath _math = new InputMath();
	private readonly object _gate = new object();
	private readonly List<Point> _trail = new List<Point>();

	private bool _capturing;
	private bool _disposed;
	private HwndSource _source;
	private Cursor _prevCursor;
	private Thread _pump;
	private volatile bool _pumpStop;
	private double _outX;
	private double _outY;
	private double _outSpeed;
	private long _lastUi;
	private int _releaseQtKey = 16777271;

	public AimPad(Window win, Canvas graph, Canvas pad, TextBlock speed, TextBlock stick)
	{
		_win = win;
		_graph = graph;
		_pad = pad;
		_speed = speed;
		_stick = stick;
		graph.SizeChanged += delegate { RedrawGraph(); };
		pad.SizeChanged += delegate { RedrawPad(); };
		pad.MouseLeftButtonDown += delegate { BeginCapture(); };
		win.KeyDown += OnKeyDown;
		win.Deactivated += delegate { Release(); };
		win.Closed += delegate { Dispose(); };
		_speed.Text = "-";
		_stick.Text = "-";
		RedrawGraph();
		RedrawPad();
	}

	public bool IsCapturing
	{
		get { return _capturing; }
	}

	// Feeds the draft values into the math and redraws the response graph.
	// Called on every draft change so the curve tracks edits live.
	public void SyncFrom(IReadOnlyDictionary<string, string> ui)
	{
		_math.ApplyUi(ui);
		string text;
		int key;
		if (ui.TryGetValue("aimrelease", out text)
				&& int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out key))
			_releaseQtKey = key;
		RedrawGraph();
	}

	// Test hook for the verification harness: feeds synthetic deltas through
	// the same InputMath path the raw-input handler uses, then computes once.
	// Sends nothing anywhere.
	internal void FeedForTest(long dx, long dy)
	{
		long now = Stopwatch.GetTimestamp();
		double x, y;
		lock (_gate)
		{
			_math.Feed(now, dx, dy);
			_math.Compute(now, out x, out y);
			_outX = x;
			_outY = y;
			_outSpeed = _math.LastSpeed;
		}
		if (!_disposed)
			_win.Dispatcher.BeginInvoke(new Action(RedrawPad));
	}

	public void BeginCapture()
	{
		if (_capturing || _disposed)
			return;
		IntPtr hwnd = new WindowInteropHelper(_win).Handle;
		if (hwnd == IntPtr.Zero)
			return;
		HwndSource source = HwndSource.FromHwnd(hwnd);
		if (source == null)
			return;
		_source = source;
		_source.AddHook(WndProc);
		RawInputDevice mouse = new RawInputDevice { UsagePage = 0x01, Usage = 0x02, Flags = 0, Target = hwnd };
		if (!RegisterRawInputDevices(new[] { mouse }, 1, (uint)Marshal.SizeOf(typeof(RawInputDevice))))
		{
			_source.RemoveHook(WndProc);
			_source = null;
			return;
		}
		Point origin = _pad.PointToScreen(new Point(0, 0));
		Rect clip = new Rect
		{
			Left = (int)Math.Round(origin.X),
			Top = (int)Math.Round(origin.Y),
			Right = (int)Math.Round(origin.X + _pad.ActualWidth),
			Bottom = (int)Math.Round(origin.Y + _pad.ActualHeight)
		};
		ClipTo(clip);
		_prevCursor = _win.Cursor;
		_win.Cursor = Cursors.None;
		_capturing = true;
		_pumpStop = false;
		lock (_gate)
		{
			_math.Reset();
			_trail.Clear();
		}
		_pump = new Thread(PumpLoop) { IsBackground = true, Name = "VectisPlay.AimPad" };
		_pump.Start();
		RedrawPad();
	}

	public void Release()
	{
		if (!_capturing)
			return;
		_capturing = false;
		_pumpStop = true;
		Thread pump = _pump;
		_pump = null;
		if (pump != null)
			pump.Join(250);
		if (_source != null)
		{
			RawInputDevice mouse = new RawInputDevice { UsagePage = 0x01, Usage = 0x02, Flags = RidevRemove, Target = IntPtr.Zero };
			RegisterRawInputDevices(new[] { mouse }, 1, (uint)Marshal.SizeOf(typeof(RawInputDevice)));
			_source.RemoveHook(WndProc);
			_source = null;
		}
		ClipCursor(IntPtr.Zero);
		if (_prevCursor != null)
		{
			_win.Cursor = _prevCursor;
			_prevCursor = null;
		}
		lock (_gate)
		{
			_math.Reset();
			_trail.Clear();
			_outX = 0;
			_outY = 0;
			_outSpeed = 0;
		}
		RedrawPad();
	}

	public void Dispose()
	{
		if (_disposed)
			return;
		_disposed = true;
		Release();
	}

	private void OnKeyDown(object sender, KeyEventArgs e)
	{
		if (!_capturing)
			return;
		if (e.Key == Key.Escape)
		{
			Release();
			e.Handled = true;
			return;
		}
		Key mapped;
		if (ReleaseKeys.TryGetValue(_releaseQtKey, out mapped) && e.Key == mapped)
		{
			Release();
			e.Handled = true;
		}
	}

	private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
	{
		if (msg != WmInput || !_capturing)
			return IntPtr.Zero;
		uint size = 0;
		uint headerSize = (uint)Marshal.SizeOf(typeof(RawInputHeader));
		GetRawInputData(lParam, RidInput, IntPtr.Zero, ref size, headerSize);
		if (size == 0 || size > 1024)
			return IntPtr.Zero;
		IntPtr buffer = Marshal.AllocHGlobal((int)size);
		try
		{
			if (GetRawInputData(lParam, RidInput, buffer, ref size, headerSize) != size)
				return IntPtr.Zero;
			RawInput raw = (RawInput)Marshal.PtrToStructure(buffer, typeof(RawInput));
			if (raw.Header.Type != RimTypeMouse)
				return IntPtr.Zero;
			if ((raw.Mouse.Flags & MouseMoveAbsolute) != 0)
				return IntPtr.Zero;
			FeedRaw(raw.Mouse.LastX, raw.Mouse.LastY);
		}
		finally
		{
			Marshal.FreeHGlobal(buffer);
		}
		return IntPtr.Zero;
	}

	private void FeedRaw(long dx, long dy)
	{
		lock (_gate)
			_math.Feed(Stopwatch.GetTimestamp(), dx, dy);
	}

	// Runs the math at the runtime's pacing cap (about 500 Hz) and republishes
	// the stick position for the UI thread to draw.
	private void PumpLoop()
	{
		while (!_pumpStop)
		{
			long now = Stopwatch.GetTimestamp();
			double x, y;
			lock (_gate)
			{
				_math.Compute(now, out x, out y);
				_outX = x;
				_outY = y;
				_outSpeed = _math.LastSpeed;
			}
			if (now - _lastUi >= Stopwatch.Frequency / 30)
			{
				_lastUi = now;
				try
				{
					if (!_win.Dispatcher.HasShutdownStarted && !_win.Dispatcher.HasShutdownFinished)
						_win.Dispatcher.BeginInvoke(new Action(RedrawPad));
				}
				catch (InvalidOperationException)
				{
					_pumpStop = true;
				}
			}
			Thread.Sleep(1);
		}
	}

	private void RedrawGraph()
	{
		_graph.Children.Clear();
		double w = _graph.ActualWidth;
		double h = _graph.ActualHeight;
		if (w < 80 || h < 60)
			return;
		double left = 36, right = 12, top = 12, bottom = 20;
		double pw = w - left - right;
		double ph = h - top - bottom;
		Line axisX = new Line { X1 = left, Y1 = top + ph, X2 = left + pw, Y2 = top + ph, Stroke = BrushLine, StrokeThickness = 1 };
		Line axisY = new Line { X1 = left, Y1 = top, X2 = left, Y2 = top + ph, Stroke = BrushLine, StrokeThickness = 1 };
		_graph.Children.Add(axisX);
		_graph.Children.Add(axisY);
		Polyline curve = new Polyline { Stroke = BrushSignal, StrokeThickness = 2 };
		PointCollection points = new PointCollection();
		for (int i = 0; i <= 160; i++)
		{
			double speed = 8.0 * i / 160.0;
			double value = _math.ResponseMagnitude(speed);
			points.Add(new Point(left + pw * speed / 8.0, top + ph * (1.0 - Math.Min(1.05, value) / 1.05)));
		}
		curve.Points = points;
		_graph.Children.Add(curve);
		AxisLabel(_graph, "0", left - 14, top + ph - 7);
		AxisLabel(_graph, "8", left + pw - 8, top + ph - 7);
		AxisLabel(_graph, "1", left - 14, top - 2);
		AxisLabel(_graph, "ct/ms", left + pw - 34, top + ph + 4);
	}

	private static void AxisLabel(Canvas canvas, string text, double x, double y)
	{
		TextBlock label = new TextBlock
		{
			Text = text,
			Foreground = BrushMuted,
			FontSize = 10,
			FontFamily = new FontFamily("Consolas")
		};
		Canvas.SetLeft(label, x);
		Canvas.SetTop(label, y);
		canvas.Children.Add(label);
	}

	private void RedrawPad()
	{
		_pad.Children.Clear();
		double w = _pad.ActualWidth;
		double h = _pad.ActualHeight;
		if (w < 40 || h < 40)
			return;
		double cx = w / 2;
		double cy = h / 2;
		double r = Math.Min(w, h) / 2 - 14;
		Ring(_pad, cx, cy, r, BrushLine, 1, 1);
		double dz = Math.Max(0, Math.Min(0.5, _math.Deadzone));
		Ring(_pad, cx, cy, Math.Max(3, r * dz), BrushMuted, 1, 0.8);
		Dot(_pad, cx, cy, 1.6, BrushMuted, 0.9);
		double ox, oy, speed;
		lock (_gate)
		{
			ox = _outX;
			oy = _outY;
			speed = _outSpeed;
		}
		double px = cx + Clamp(ox, -1, 1) * r * 0.9;
		double py = cy + Clamp(oy, -1, 1) * r * 0.9;
		_trail.Add(new Point(px, py));
		while (_trail.Count > 14)
			_trail.RemoveAt(0);
		for (int i = 0; i < _trail.Count; i++)
			Dot(_pad, _trail[i].X, _trail[i].Y, 2.2, BrushSignal, 0.15 + 0.45 * (i + 1) / (double)_trail.Count);
		Dot(_pad, px, py, 5.5, BrushSignal, 1);
		double mag = Math.Sqrt(ox * ox + oy * oy);
		_speed.Text = "SPD " + speed.ToString("0.0", CultureInfo.InvariantCulture) + " ct/ms";
		_stick.Text = "STICK " + (mag * 100).ToString("0", CultureInfo.InvariantCulture) + "%";
	}

	private static double Clamp(double value, double min, double max)
	{
		return value < min ? min : value > max ? max : value;
	}

	private static void Ring(Canvas canvas, double cx, double cy, double r, Brush stroke, double thick, double opacity)
	{
		Ellipse ring = new Ellipse { Width = 2 * r, Height = 2 * r, Stroke = stroke, StrokeThickness = thick, Opacity = opacity };
		Canvas.SetLeft(ring, cx - r);
		Canvas.SetTop(ring, cy - r);
		canvas.Children.Add(ring);
	}

	private static void Dot(Canvas canvas, double cx, double cy, double r, Brush fill, double opacity)
	{
		Ellipse dot = new Ellipse { Width = 2 * r, Height = 2 * r, Fill = fill, Opacity = opacity };
		Canvas.SetLeft(dot, cx - r);
		Canvas.SetTop(dot, cy - r);
		canvas.Children.Add(dot);
	}

	private static bool ClipTo(Rect rect)
	{
		int size = Marshal.SizeOf(typeof(Rect));
		IntPtr pointer = Marshal.AllocHGlobal(size);
		try
		{
			Marshal.StructureToPtr(rect, pointer, false);
			return ClipCursor(pointer);
		}
		finally
		{
			Marshal.FreeHGlobal(pointer);
		}
	}
}
