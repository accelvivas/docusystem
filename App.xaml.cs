namespace docusystem;

public partial class App : Application
{
	private readonly IServiceProvider _services;

	public App(IServiceProvider services)
	{
		InitializeComponent();
		// UI is designed for light surfaces (e.g. #F4F6FB); implicit styles use AppThemeBinding.
		// Without this, system dark mode makes Labels use light text on light backgrounds.
		UserAppTheme = AppTheme.Light;
		_services = services;
	}

	protected override Window CreateWindow(IActivationState? activationState)
	{
		return new Window(_services.GetRequiredService<AppShell>());
	}
}
