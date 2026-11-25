using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

// Couch navigation. Arrow keys and an XInput pad move focus spatially; inside a
// page the scroll view pages forward before focus may leave for the footer.
// A selects, B backs out to the page rail, LB and RB switch pages. The pad is
// polled only while this window is active, so the player never sees it here.
internal sealed class GamepadNav : IDisposable
{
	[StructLayout(LayoutKind.Sequential)]
	private struct XInputGamepad
	{
		public ushort Buttons;
		public byte LeftTrigger;
		public byte RightTrigger;
		public short ThumbLX;
		public short ThumbLY;
		public short ThumbRX;
		public short ThumbRY;
	}

	[StructLayout(LayoutKind.Sequential)]
	private struct XInputState
	{
		public uint Packet;
		public XInputGamepad Pad;
	}

	[DllImport("xinput1_4.dll", EntryPoint = "XInputGetState")]
	private static extern int GetState14(int index, out XInputState state);

	[DllImport("xinput9_1_0.dll", EntryPoint = "XInputGetState")]
	private static extern int GetState910(int index, out XInputState state);

	private const ushort PadUp = 0x0001;
	private const ushort PadDown = 0x0002;
	private const ushort PadLeft = 0x0004;
	private const ushort PadRight = 0x0008;
	private const ushort PadLB = 0x0100;
	private const ushort PadRB = 0x0200;
	private const ushort PadA = 0x1000;
	private const ushort PadB = 0x2000;
	private const ushort Directions = PadUp | PadDown | PadLeft | PadRight;
	private const short StickThreshold = 16000;
	private const double ScrollStep = 120;

	private readonly Window _win;
	private readonly TabControl _pages;
	private readonly Func<UIElement> _home;
	private readonly Func<bool> _busy;
	private readonly Action<bool> _padChanged;
	private readonly DispatcherTimer _poll = new DispatcherTimer(DispatcherPriority.Input);

	private int _apiLevel;
	private int _pad = -1;
	private DateTime _nextScan;
	private bool _primed;
	private ushort _held;
	private ushort _repeatButton;
	private DateTime _repeatAt;
	private int _repeatCount;

	public GamepadNav(Window win, TabControl pages, Func<UIElement> home, Func<bool> busy, Action<bool> padChanged)
	{
		_win = win;
		_pages = pages;
		_home = home;
		_busy = busy;
		_padChanged = padChanged;
		_win.PreviewKeyDown += OnPreviewKeyDown;
		_win.AddHandler(UIElement.GotKeyboardFocusEvent, new KeyboardFocusChangedEventHandler(OnGotKeyboardFocus), true);
		_poll.Interval = TimeSpan.FromMilliseconds(16);
		_poll.Tick += delegate { Poll(); };
		_win.Activated += delegate
		{
			_primed = false;
			if (_apiLevel >= 0)
				_poll.Start();
		};
		_win.Deactivated += delegate { _poll.Stop(); };
	}

	public void Dispose()
	{
		_poll.Stop();
	}

	private void OnGotKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
	{
		if (InputManager.Current.MostRecentInputDevice is KeyboardDevice)
			RevealWithMargin(e.NewFocus as FrameworkElement);
	}

	private void OnPreviewKeyDown(object sender, KeyEventArgs e)
	{
		if (e.Handled || Keyboard.Modifiers != ModifierKeys.None || _busy())
			return;
		object focused = Keyboard.FocusedElement;
		switch (e.Key)
		{
			case Key.Up:
			case Key.Down:
				if (focused is ComboBoxItem || focused is TabItem || IsOpenCombo(focused))
					return;
				e.Handled = Move(e.Key == Key.Up ? FocusNavigationDirection.Up : FocusNavigationDirection.Down);
				break;
			case Key.Left:
			case Key.Right:
				if (focused is Slider || focused is TextBox || focused is ComboBoxItem || IsOpenCombo(focused))
					return;
				if (focused is TabItem && e.Key == Key.Left)
					return;
				e.Handled = Move(e.Key == Key.Left ? FocusNavigationDirection.Left : FocusNavigationDirection.Right);
				break;
			case Key.Enter:
			case Key.Space:
				ComboBox combo = focused as ComboBox;
				if (combo != null && !combo.IsDropDownOpen)
				{
					combo.IsDropDownOpen = true;
					e.Handled = true;
				}
				else if (e.Key == Key.Enter && focused is CheckBox)
				{
					Toggle((CheckBox)focused);
					e.Handled = true;
				}
				break;
		}
	}

	private static bool IsOpenCombo(object focused)
	{
		ComboBox combo = focused as ComboBox;
		return combo != null && combo.IsDropDownOpen;
	}

	private bool ReadPad(int index, out XInputState state)
	{
		state = new XInputState();
		try
		{
			return (_apiLevel == 0 ? GetState14(index, out state) : GetState910(index, out state)) == 0;
		}
		catch (Exception ex)
		{
			if (!(ex is DllNotFoundException) && !(ex is EntryPointNotFoundException))
				throw;
			_apiLevel = _apiLevel == 0 ? 1 : -1;
			if (_apiLevel < 0)
				_poll.Stop();
			return false;
		}
	}

	private void Poll()
	{
		if (!_win.IsActive || _win.WindowState == WindowState.Minimized)
			return;
		XInputState state;
		DateTime now = DateTime.UtcNow;
		if (_pad < 0)
		{
			// Querying empty slots is slow in XInput, so look for a pad only every 1.5 s.
			if (now < _nextScan)
				return;
			_nextScan = now.AddSeconds(1.5);
			for (int i = 0; i < 4 && _pad < 0 && _apiLevel >= 0; i++)
				if (ReadPad(i, out state))
					_pad = i;
			if (_pad < 0)
				return;
			_primed = false;
			_padChanged(true);
		}
		if (!ReadPad(_pad, out state))
		{
			_pad = -1;
			_held = 0;
			_repeatButton = 0;
			_padChanged(false);
			return;
		}

		ushort buttons = state.Pad.Buttons;
		if (state.Pad.ThumbLY > StickThreshold) buttons |= PadUp;
		if (state.Pad.ThumbLY < -StickThreshold) buttons |= PadDown;
		if (state.Pad.ThumbLX > StickThreshold) buttons |= PadRight;
		if (state.Pad.ThumbLX < -StickThreshold) buttons |= PadLeft;
		if (!_primed)
		{
			// Buttons already held when the window gains focus (the press that closed the player) do nothing.
			_held = buttons;
			_primed = true;
			return;
		}
		ushort pressed = (ushort)(buttons & ~_held);
		_held = buttons;
		if (_busy())
			return;

		ushort direction = (ushort)(pressed & Directions);
		if (direction != 0)
		{
			_repeatButton = LowestBit(direction);
			_repeatCount = 0;
			_repeatAt = now.AddMilliseconds(380);
			Direction(_repeatButton, 1);
		}
		else if (_repeatButton != 0 && (buttons & _repeatButton) != 0 && now >= _repeatAt)
		{
			_repeatCount++;
			_repeatAt = now.AddMilliseconds(90);
			Direction(_repeatButton, _repeatCount > 24 ? 5 : _repeatCount > 10 ? 2 : 1);
		}
		else if ((buttons & _repeatButton) == 0)
		{
			_repeatButton = 0;
		}

		if ((pressed & PadA) != 0)
			Select();
		if ((pressed & PadB) != 0)
			Back();
		if ((pressed & PadLB) != 0)
			SwitchPage(-1);
		if ((pressed & PadRB) != 0)
			SwitchPage(1);
	}

	private static ushort LowestBit(ushort value)
	{
		return (ushort)(value & -value);
	}

	private void Direction(ushort button, int steps)
	{
		FocusNavigationDirection dir = button == PadUp ? FocusNavigationDirection.Up
			: button == PadDown ? FocusNavigationDirection.Down
			: button == PadLeft ? FocusNavigationDirection.Left
			: FocusNavigationDirection.Right;
		bool vertical = dir == FocusNavigationDirection.Up || dir == FocusNavigationDirection.Down;
		object focused = Keyboard.FocusedElement;

		ComboBox open = focused as ComboBox;
		if (open != null && open.IsDropDownOpen)
		{
			UIElement item = open.ItemContainerGenerator.ContainerFromItem(open.SelectedItem) as UIElement
				?? open.ItemContainerGenerator.ContainerFromIndex(0) as UIElement;
			if (item != null)
				item.Focus();
			return;
		}
		if (focused is ComboBoxItem)
		{
			if (vertical)
				((UIElement)focused).MoveFocus(new TraversalRequest(dir));
			return;
		}
		Slider slider = focused as Slider;
		if (slider != null && !vertical)
		{
			RoutedCommand command = dir == FocusNavigationDirection.Right ? Slider.IncreaseSmall : Slider.DecreaseSmall;
			for (int i = 0; i < steps; i++)
				command.Execute(null, slider);
			return;
		}
		if (focused is TabItem && vertical)
		{
			SwitchPage(dir == FocusNavigationDirection.Up ? -1 : 1);
			return;
		}
		Move(dir);
	}

	private void Select()
	{
		object focused = Keyboard.FocusedElement;
		ComboBoxItem item = focused as ComboBoxItem;
		if (item != null)
		{
			ComboBox owner = ItemsControl.ItemsControlFromItemContainer(item) as ComboBox;
			if (owner != null)
			{
				owner.SelectedItem = owner.ItemContainerGenerator.ItemFromContainer(item);
				owner.IsDropDownOpen = false;
				owner.Focus();
			}
			return;
		}
		ComboBox combo = focused as ComboBox;
		if (combo != null)
		{
			if (combo.IsEnabled)
				combo.IsDropDownOpen = !combo.IsDropDownOpen;
			return;
		}
		CheckBox check = focused as CheckBox;
		if (check != null)
		{
			Toggle(check);
			return;
		}
		Button button = focused as Button;
		if (button != null)
		{
			if (button.IsEnabled && button.IsVisible)
				button.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent, button));
			return;
		}
		if (focused is TabItem)
		{
			Move(FocusNavigationDirection.Right);
			return;
		}
		if (!(focused is Slider) && !(focused is TextBox))
			FocusHome();
	}

	private void Back()
	{
		object focused = Keyboard.FocusedElement;
		ComboBoxItem item = focused as ComboBoxItem;
		ComboBox owner = item != null ? ItemsControl.ItemsControlFromItemContainer(item) as ComboBox : focused as ComboBox;
		if (owner != null && owner.IsDropDownOpen)
		{
			owner.IsDropDownOpen = false;
			owner.Focus();
			return;
		}
		TabItem selected = _pages.SelectedItem as TabItem;
		if (selected != null && !ReferenceEquals(focused, selected))
			selected.Focus();
	}

	private void SwitchPage(int delta)
	{
		int count = _pages.Items.Count;
		if (count < 2)
			return;
		int index = Math.Max(0, Math.Min(count - 1, _pages.SelectedIndex + delta));
		_pages.SelectedIndex = index;
		TabItem selected = _pages.SelectedItem as TabItem;
		if (selected != null)
			selected.Focus();
	}

	private static void Toggle(CheckBox check)
	{
		if (check.IsEnabled)
			check.IsChecked = check.IsChecked != true;
	}

	private void FocusHome()
	{
		UIElement home = _home();
		if (home != null && home.IsVisible && home.IsEnabled)
			home.Focus();
	}

	// Moves focus one step in a direction. Targets inside the current page win
	// while they are on screen; otherwise the page scrolls if it still can, so
	// no setting is skipped on the way to the footer or the page rail.
	internal bool Move(FocusNavigationDirection dir)
	{
		FrameworkElement current = Keyboard.FocusedElement as FrameworkElement;
		if (current == null || !current.IsVisible || !current.IsDescendantOf(_win))
		{
			FocusHome();
			return true;
		}
		List<FrameworkElement> candidates = new List<FrameworkElement>();
		Collect(_win, current, candidates);
		Rect from = BoundsOf(current);
		ScrollViewer page = PageOf(current);
		FrameworkElement target = null;
		if (page != null)
		{
			// After paging past it, the focused control may sit above or below the
			// viewport; measure from the nearest viewport edge instead.
			Rect view = page.TransformToAncestor(_win).TransformBounds(new Rect(0, 0, page.ViewportWidth, page.ViewportHeight));
			if (from.Bottom < view.Top || from.Top > view.Bottom)
			{
				bool above = from.Bottom < view.Top;
				if ((above && dir == FocusNavigationDirection.Up) || (!above && dir == FocusNavigationDirection.Down))
				{
					RevealWithMargin(current);
					return true;
				}
				from.Y = above ? view.Top - from.Height : view.Bottom;
			}
			target = Nearest(from, dir, candidates, delegate(FrameworkElement c) { return PageOf(c) == page && OnScreen(c, page); });
			bool vertical = dir == FocusNavigationDirection.Up || dir == FocusNavigationDirection.Down;
			if (target == null && vertical && Scroll(page, dir))
				return true;
			if (target == null && dir == FocusNavigationDirection.Left)
				target = _pages.SelectedItem as TabItem;
			// Leaving a page up or down starts from the viewport edge, so the
			// rail beside the page is never mistaken for the next row.
			if (vertical)
				from.Y = dir == FocusNavigationDirection.Down ? view.Bottom - from.Height : view.Top;
		}
		if (target == null)
			target = Nearest(from, dir, candidates, delegate(FrameworkElement c)
			{
				ScrollViewer owner = PageOf(c);
				return owner == null || OnScreen(c, owner);
			});
		if (target == null)
			return false;
		target.Focus();
		RevealWithMargin(target);
		return true;
	}

	private static bool Scroll(ScrollViewer page, FocusNavigationDirection dir)
	{
		if (dir == FocusNavigationDirection.Down && page.VerticalOffset < page.ScrollableHeight - 0.5)
		{
			page.ScrollToVerticalOffset(Math.Min(page.ScrollableHeight, page.VerticalOffset + ScrollStep));
			return true;
		}
		if (dir == FocusNavigationDirection.Up && page.VerticalOffset > 0.5)
		{
			page.ScrollToVerticalOffset(Math.Max(0, page.VerticalOffset - ScrollStep));
			return true;
		}
		return false;
	}

	// Focusable, enabled, visible controls; page rail entries other than the
	// selected one are skipped because focusing a tab switches the page.
	private void Collect(DependencyObject node, FrameworkElement current, List<FrameworkElement> found)
	{
		int count = VisualTreeHelper.GetChildrenCount(node);
		for (int i = 0; i < count; i++)
		{
			FrameworkElement child = VisualTreeHelper.GetChild(node, i) as FrameworkElement;
			if (child == null || !child.IsVisible)
				continue;
			Control control = child as Control;
			bool candidate = control != null && control.Focusable && control.IsEnabled && control.IsTabStop
				&& !ReferenceEquals(control, current) && !(control is TabControl) && !(control is ScrollViewer)
				&& (!(control is TabItem) || ReferenceEquals(control, _pages.SelectedItem));
			if (candidate)
				found.Add(child);
			if (control is ComboBox || control is TextBox || control is Slider || control is ButtonBase)
				continue;
			Collect(child, current, found);
		}
	}

	private FrameworkElement Nearest(Rect from, FocusNavigationDirection dir, List<FrameworkElement> candidates, Predicate<FrameworkElement> allowed)
	{
		FrameworkElement best = null;
		double bestScore = double.MaxValue;
		foreach (FrameworkElement candidate in candidates)
		{
			if (!allowed(candidate))
				continue;
			Rect to = BoundsOf(candidate);
			double along, across, offset;
			switch (dir)
			{
				case FocusNavigationDirection.Down:
					if (to.Top < from.Bottom - 8) continue;
					along = to.Top - from.Bottom;
					across = Gap(from.Left, from.Right, to.Left, to.Right);
					offset = Math.Abs((to.Left + to.Right) - (from.Left + from.Right)) / 2;
					break;
				case FocusNavigationDirection.Up:
					if (to.Bottom > from.Top + 8) continue;
					along = from.Top - to.Bottom;
					across = Gap(from.Left, from.Right, to.Left, to.Right);
					offset = Math.Abs((to.Left + to.Right) - (from.Left + from.Right)) / 2;
					break;
				case FocusNavigationDirection.Right:
					if (to.Left < from.Right - 8) continue;
					along = to.Left - from.Right;
					across = Gap(from.Top, from.Bottom, to.Top, to.Bottom);
					offset = Math.Abs((to.Top + to.Bottom) - (from.Top + from.Bottom)) / 2;
					break;
				default:
					if (to.Right > from.Left + 8) continue;
					along = from.Left - to.Right;
					across = Gap(from.Top, from.Bottom, to.Top, to.Bottom);
					offset = Math.Abs((to.Top + to.Bottom) - (from.Top + from.Bottom)) / 2;
					break;
			}
			bool horizontal = dir == FocusNavigationDirection.Left || dir == FocusNavigationDirection.Right;
			if (horizontal && across > Math.Max(0, along) * 0.5 + 16)
				continue;
			double score = Math.Max(0, along) + across * 4 + offset * 0.1;
			if (score < bestScore)
			{
				bestScore = score;
				best = candidate;
			}
		}
		return best;
	}

	private static double Gap(double a0, double a1, double b0, double b1)
	{
		return Math.Max(0, Math.Max(b0 - a1, a0 - b1));
	}

	private Rect BoundsOf(FrameworkElement element)
	{
		return element.TransformToAncestor(_win).TransformBounds(new Rect(element.RenderSize));
	}

	private static bool OnScreen(FrameworkElement element, ScrollViewer page)
	{
		Rect r = element.TransformToAncestor(page).TransformBounds(new Rect(element.RenderSize));
		return r.Bottom > 4 && r.Top < page.ViewportHeight - 4;
	}

	private static void RevealWithMargin(FrameworkElement element)
	{
		if (element == null || element is TabItem || element.ActualHeight <= 0)
			return;
		element.BringIntoView(new Rect(-12, -32, element.ActualWidth + 24, element.ActualHeight + 112));
	}

	private ScrollViewer PageOf(DependencyObject element)
	{
		for (DependencyObject node = VisualTreeHelper.GetParent(element); node != null; node = VisualTreeHelper.GetParent(node))
		{
			ScrollViewer viewer = node as ScrollViewer;
			if (viewer != null && viewer.TemplatedParent == null)
				return viewer;
			if (node is TabControl)
				return null;
		}
		return null;
	}
}
