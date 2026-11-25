using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

internal sealed class LinkPanel
{
	private const int SampleSlots = 500;
	private const double TopPad = 10.0;

	private readonly Canvas _trace;
	private readonly TextBlock _link;
	private readonly TextBlock _signal;
	private readonly TextBlock _receive;
	private readonly TextBlock _median;
	private readonly TextBlock _p95;
	private readonly TextBlock _spikes;
	private readonly TextBlock _notes;
	private readonly Button _check;
	private readonly Button _use;

	private readonly List<int?> _samples = new List<int?>(SampleSlots);
	private bool _checking;
	private bool _childRunning;
	private string _currentKbps;

	public string SuggestedKbps { get; private set; }

	public LinkPanel(Canvas trace, TextBlock link, TextBlock signal, TextBlock receive,
		TextBlock median, TextBlock p95, TextBlock spikes, TextBlock notes, Button check, Button use)
	{
		_trace = trace;
		_link = link;
		_signal = signal;
		_receive = receive;
		_median = median;
		_p95 = p95;
		_spikes = spikes;
		_notes = notes;
		_check = check;
		_use = use;
		_trace.SizeChanged += delegate { Redraw(); };
		Reset();
	}

	public void Reset()
	{
		_samples.Clear();
		SuggestedKbps = null;
		_checking = false;
		SetReadouts(null);
		_notes.Text = string.Empty;
		_use.Visibility = Visibility.Collapsed;
		RefreshCheckButton();
		Redraw();
	}

	public void BeginCheck()
	{
		_checking = true;
		_samples.Clear();
		SuggestedKbps = null;
		_use.Visibility = Visibility.Collapsed;
		_notes.Text = string.Empty;
		RefreshCheckButton();
		Redraw();
	}

	public void AddSample(int? rtt)
	{
		_samples.Add(rtt);
		Redraw();
	}

	public void Finish(LinkProbe.Result result)
	{
		SetReadouts(result);
		string notes = result.Notes.Length > 0 ? string.Join(" ", result.Notes) : string.Empty;
		if (result.SuggestedKbps.HasValue)
		{
			SuggestedKbps = result.SuggestedKbps.Value.ToString(CultureInfo.InvariantCulture);
			if (notes.Length > 0)
				notes += " ";
			notes += "Suggested bitrate: " + SuggestedKbps + " kbps. Using it only updates the draft; choose Save settings to keep it.";
			_use.Visibility = Visibility.Visible;
		}
		else
		{
			SuggestedKbps = null;
			_use.Visibility = Visibility.Collapsed;
		}
		_notes.Text = notes;
		_checking = false;
		RefreshCheckButton();
		Redraw();
	}

	public void Fail(string message)
	{
		_notes.Text = "Network check failed: " + message;
		_checking = false;
		RefreshCheckButton();
	}

	public void Cancelled()
	{
		_checking = false;
		_samples.Clear();
		RefreshCheckButton();
		Redraw();
	}

	public void SetChildRunning(bool running)
	{
		_childRunning = running;
		RefreshCheckButton();
	}

	public void UpdateUseState(string currentKbps)
	{
		_currentKbps = (currentKbps ?? string.Empty).Trim();
		RefreshUseButton();
	}

	private void RefreshCheckButton()
	{
		_check.Content = _checking ? "Checking..." : "Check my network";
		_check.IsEnabled = !_checking && !_childRunning;
		RefreshUseButton();
	}

	private void RefreshUseButton()
	{
		if (SuggestedKbps == null)
			return;
		bool selected = string.Equals(_currentKbps, SuggestedKbps, StringComparison.Ordinal);
		_use.Content = selected ? "Bitrate selected" : "Use " + SuggestedKbps + " kbps";
		_use.IsEnabled = !selected && !_checking && !_childRunning;
	}

	private void SetReadouts(LinkProbe.Result result)
	{
		if (result == null)
		{
			_link.Text = "-";
			_signal.Text = "-";
			_receive.Text = "-";
			_median.Text = "-";
			_p95.Text = "-";
			_spikes.Text = "-";
			return;
		}
		_link.Text = string.IsNullOrEmpty(result.LinkLabel) ? "-" : result.LinkLabel;
		_signal.Text = FormatSignal(result);
		_receive.Text = FormatReceive(result.RxKbps);
		_median.Text = result.Median.HasValue ? result.Median.Value.ToString("0.#", CultureInfo.InvariantCulture) + " ms" : "-";
		_p95.Text = result.P95.HasValue ? result.P95.Value.ToString(CultureInfo.InvariantCulture) + " ms" : "-";
		_spikes.Text = result.Spikes.ToString(CultureInfo.InvariantCulture) + " · " + result.LossPercent.ToString("0.#", CultureInfo.InvariantCulture) + "%";
	}

	private static string FormatSignal(LinkProbe.Result result)
	{
		string percent = result.SignalPercent.HasValue ? result.SignalPercent.Value.ToString(CultureInfo.InvariantCulture) + "%" : null;
		string dbm = result.Rssi.HasValue ? result.Rssi.Value.ToString(CultureInfo.InvariantCulture) + " dBm" : null;
		if (percent != null && dbm != null)
			return percent + " · " + dbm;
		return percent ?? dbm ?? "-";
	}

	private static string FormatReceive(long? rxKbps)
	{
		if (!rxKbps.HasValue)
			return "-";
		double mbps = rxKbps.Value / 1000.0;
		return (mbps >= 100 ? mbps.ToString("0", CultureInfo.InvariantCulture) : mbps.ToString("0.#", CultureInfo.InvariantCulture)) + " Mbps";
	}

	private SolidColorBrush ResourceBrush(string key, byte r, byte g, byte b)
	{
		SolidColorBrush brush = _trace.TryFindResource(key) as SolidColorBrush;
		return brush ?? new SolidColorBrush(Color.FromRgb(r, g, b));
	}

	private void Redraw()
	{
		_trace.Children.Clear();
		double w = _trace.ActualWidth;
		double h = _trace.ActualHeight;
		if (w < 48 || h < 48)
			return;
		SolidColorBrush signal = ResourceBrush("Signal", 0xFF, 0x5B, 0x4B);
		SolidColorBrush muted = ResourceBrush("Muted", 0xA8, 0x9F, 0x98);
		SolidColorBrush paper = ResourceBrush("Paper", 0xF2, 0xEC, 0xE6);
		SolidColorBrush dim = ResourceBrush("Line2", 0x46, 0x3F, 0x39);

		double baselineY = h * 0.60;
		double plotLeft = 34;
		double plotRight = w - 40;
		double plotW = Math.Max(1, plotRight - plotLeft);
		double plotH = Math.Max(1, baselineY - TopPad);

		Line baseline = new Line { X1 = plotLeft, Y1 = baselineY, X2 = plotRight, Y2 = baselineY, Stroke = signal, StrokeThickness = 1.8 };
		_trace.Children.Add(baseline);

		List<int> values = new List<int>(_samples.Count);
		foreach (int? sample in _samples)
			if (sample.HasValue)
				values.Add(sample.Value);
		if (values.Count > 0)
		{
			values.Sort();
			double p99 = values[(int)Math.Ceiling(0.99 * values.Count) - 1];
			double scale = Math.Max(30.0, p99 + 5.0);
			int n = values.Count;
			double median = n % 2 == 1 ? values[n / 2] : (values[n / 2 - 1] + values[n / 2]) / 2.0;
			double threshold = Math.Max(3.0 * median, median + 15.0);

			PointCollection points = new PointCollection();
			PathGeometry gaps = new PathGeometry();
			List<Point> spikePoints = new List<Point>();
			for (int i = 0; i < _samples.Count; i++)
			{
				double x = plotLeft + i * plotW / (SampleSlots - 1);
				int? sample = _samples[i];
				if (!sample.HasValue)
				{
					PathFigure tick = new PathFigure(new Point(x, baselineY - 3), new[] { new LineSegment(new Point(x, baselineY + 3), true) }, false);
					gaps.Figures.Add(tick);
					continue;
				}
				double y = baselineY - Math.Min(scale, Math.Max(0, sample.Value)) / scale * plotH;
				Point point = new Point(x, y);
				points.Add(point);
				if (sample.Value > threshold)
					spikePoints.Add(point);
			}
			if (points.Count > 1)
			{
				Polyline trace = new Polyline { Stroke = signal, StrokeThickness = 1.6 };
				trace.Points = points;
				_trace.Children.Add(trace);
			}
			if (gaps.Figures.Count > 0)
			{
				Path gapPath = new Path { Data = gaps, Stroke = muted, StrokeThickness = 1.6 };
				_trace.Children.Add(gapPath);
			}
			foreach (Point point in spikePoints)
			{
				Ellipse dot = new Ellipse { Width = 4, Height = 4, Fill = signal };
				Canvas.SetLeft(dot, point.X - 2);
				Canvas.SetTop(dot, point.Y - 2);
				_trace.Children.Add(dot);
			}
		}

		AddGlyphs(signal, dim, paper, baselineY, w);
	}

	private void AddGlyphs(Brush signal, Brush dim, Brush paper, double baselineY, double w)
	{
		// Console glyph, left end, simplified from the session illustration.
		Canvas console = new Canvas { Width = 24, Height = 32 };
		console.Children.Add(new Path
		{
			Stroke = paper,
			StrokeThickness = 1.8,
			StrokeLineJoin = PenLineJoin.Round,
			Data = Geometry.Parse("M4 29V11Q4 4 11 4H15Q21 11 21 16Q21 21 15 29H11Q4 29 4 29Z")
		});
		console.Children.Add(new Path
		{
			Stroke = signal,
			StrokeThickness = 1.4,
			Data = Geometry.Parse("M7 4Q12 11 12 16Q12 21 7 29")
		});
		Ellipse consoleDot = new Ellipse { Width = 3, Height = 3, Fill = signal };
		Canvas.SetLeft(consoleDot, 8);
		Canvas.SetTop(consoleDot, 14.5);
		console.Children.Add(consoleDot);
		Canvas.SetLeft(console, 6);
		Canvas.SetTop(console, baselineY - 16);
		_trace.Children.Add(console);

		// Display glyph, right end.
		Canvas display = new Canvas { Width = 34, Height = 26 };
		display.Children.Add(new Path
		{
			Stroke = paper,
			StrokeThickness = 1.8,
			StrokeLineJoin = PenLineJoin.Round,
			Data = Geometry.Parse("M2 3H32Q34 3 34 5V19Q34 21 32 21H2Q0 21 0 19V5Q0 3 2 3Z")
		});
		display.Children.Add(new Path
		{
			Stroke = dim,
			StrokeThickness = 1,
			Data = Geometry.Parse("M5 6H29V16H5Z")
		});
		display.Children.Add(new Path
		{
			Stroke = paper,
			StrokeThickness = 1.6,
			StrokeStartLineCap = PenLineCap.Round,
			StrokeEndLineCap = PenLineCap.Round,
			Data = Geometry.Parse("M15 21V24M10 26H20")
		});
		Canvas.SetLeft(display, w - 40);
		Canvas.SetTop(display, baselineY - 13);
		_trace.Children.Add(display);
	}
}
