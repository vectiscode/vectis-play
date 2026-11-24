using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;

// Pure port of the patched runtime's mouse-to-stick math. No WPF types so it
// can be unit driven and reused by the AimPad preview.
//
// Model: mouse deltas accumulate in a sliding window of WindowMs
// milliseconds. Velocity v is the summed counts divided by the window.
// d = sensitivity * v / 4 with the y component scaled by YRatio. The stick
// magnitude m is min(1, |d|). Output magnitude is
// deadzone + (1 - deadzone) * m^curve applied along d's direction, so the
// smallest nonzero motion jumps to the deadzone floor. When the window has
// been empty for WindowMs the output decays exponentially with StopTauMs,
// engaging only above 3 percent and releasing to zero below 1 percent.
internal sealed class InputMath
{
	private struct Sample
	{
		public long T;
		public long Dx;
		public long Dy;
	}

	public double Sensitivity = 2.0;
	public double YRatio = 1.0;
	public double Deadzone = 0.12;
	public double Curve = 0.5;
	public int WindowMs = 8;
	public int StopTauMs = 12;

	private readonly Queue<Sample> _samples = new Queue<Sample>(256);
	private long _sumX;
	private long _sumY;
	private long _windowTicks = -1;
	private long _lastMotion;
	private bool _decaying;
	private double _decayDirX;
	private double _decayDirY;
	private double _decayMag;
	private long _decayStart;
	private double _holdX;
	private double _holdY;
	private double _speed;

	public double LastSpeed
	{
		get { return _speed; }
	}

	private void EnsureWindow()
	{
		if (_windowTicks < 0 || _windowTicks != WindowTicksFor(WindowMs))
			_windowTicks = WindowTicksFor(WindowMs);
	}

	private static long WindowTicksFor(int windowMs)
	{
		return (long)((double)Stopwatch.Frequency * Math.Max(1, windowMs) / 1000.0);
	}

	private static double TicksToMs(long ticks)
	{
		return ticks * 1000.0 / Stopwatch.Frequency;
	}

	private void Evict(long now)
	{
		while (_samples.Count > 0 && now - _samples.Peek().T >= _windowTicks)
		{
			Sample old = _samples.Dequeue();
			_sumX -= old.Dx;
			_sumY -= old.Dy;
		}
	}

	public void Reset()
	{
		_samples.Clear();
		_sumX = 0;
		_sumY = 0;
		_lastMotion = 0;
		_decaying = false;
		_holdX = 0;
		_holdY = 0;
		_speed = 0;
	}

	// now is a Stopwatch.GetTimestamp() value. dx/dy are raw mouse counts.
	public void Feed(long now, long dx, long dy)
	{
		EnsureWindow();
		_samples.Enqueue(new Sample { T = now, Dx = dx, Dy = dy });
		_sumX += dx;
		_sumY += dy;
		if (dx != 0 || dy != 0)
			_lastMotion = now;
		_decaying = false;
	}

	// Writes the normalized stick position (-1 to 1 per axis) for time now.
	public void Compute(long now, out double x, out double y)
	{
		EnsureWindow();
		Evict(now);
		if (_samples.Count > 0)
		{
			double vx = _sumX / (double)WindowMs;
			double vy = _sumY * YRatio / (double)WindowMs;
			double dx = Sensitivity * vx / 4.0;
			double dy = Sensitivity * vy / 4.0;
			_speed = Math.Sqrt((double)_sumX * _sumX + (double)_sumY * _sumY) / WindowMs;
			double dlen = Math.Sqrt(dx * dx + dy * dy);
			double m = Math.Min(1.0, dlen);
			if (m <= 0.0)
			{
				x = 0;
				y = 0;
				_holdX = 0;
				_holdY = 0;
				_decaying = false;
				return;
			}
			double mag = Deadzone + (1.0 - Deadzone) * Math.Pow(m, Curve);
			double scale = mag / dlen;
			x = dx * scale;
			y = dy * scale;
			_holdX = x;
			_holdY = y;
			_decaying = false;
			return;
		}

		_speed = 0;
		if (!_decaying)
		{
			double lastMag = Math.Sqrt(_holdX * _holdX + _holdY * _holdY);
			if (lastMag <= 0.03)
			{
				x = 0;
				y = 0;
				return;
			}
			_decaying = true;
			_decayMag = lastMag;
			_decayDirX = _holdX / lastMag;
			_decayDirY = _holdY / lastMag;
			_decayStart = _lastMotion + _windowTicks;
		}
		double elapsedMs = TicksToMs(Math.Max(0, now - _decayStart));
		double decayed = StopTauMs <= 0 ? 0.0 : _decayMag * Math.Exp(-elapsedMs / StopTauMs);
		if (decayed < 0.01)
		{
			_decaying = false;
			x = 0;
			y = 0;
			return;
		}
		x = _decayDirX * decayed;
		y = _decayDirY * decayed;
	}

	// Steady-state stick magnitude for a constant mouse speed in counts per
	// millisecond along one axis. Used by the response graph.
	public double ResponseMagnitude(double speedCountsPerMs)
	{
		double m = Math.Min(1.0, Math.Abs(Sensitivity) * speedCountsPerMs / 4.0);
		if (m <= 0.0)
			return 0.0;
		return Deadzone + (1.0 - Deadzone) * Math.Pow(m, Curve);
	}

	// Pulls the draft field values out of the settings dictionary. Fields
	// that do not parse are ignored so a half-typed value never corrupts the
	// preview.
	public void ApplyUi(IReadOnlyDictionary<string, string> ui)
	{
		double d;
		string text;
		if (ui.TryGetValue("aimsens", out text) && TryDouble(text, out d) && d > 0)
			Sensitivity = d;
		if (ui.TryGetValue("aimyratio", out text) && TryDouble(text, out d) && d > 0)
			YRatio = d;
		if (ui.TryGetValue("aimdeadzone", out text) && TryDouble(text, out d) && d >= 0 && d <= 50)
			Deadzone = d / 100.0;
		if (ui.TryGetValue("aimcurve", out text) && TryDouble(text, out d) && d > 0)
			Curve = d;
		if (ui.TryGetValue("aimwindow", out text) && TryDouble(text, out d) && d >= 1 && d <= 100)
			WindowMs = (int)d;
		if (ui.TryGetValue("aimtau", out text) && TryDouble(text, out d) && d >= 0 && d <= 500)
			StopTauMs = (int)d;
		EnsureWindow();
	}

	private static bool TryDouble(string text, out double value)
	{
		return double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value)
			&& !double.IsNaN(value) && !double.IsInfinity(value);
	}
}
