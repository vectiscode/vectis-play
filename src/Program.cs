using System;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Windows;
using System.Windows.Markup;

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
