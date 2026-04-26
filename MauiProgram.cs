using Microsoft.Extensions.Logging;
using docusystem.Pages.Login;
using docusystem.Pages.Dashboard;
using docusystem.Pages.Notifications;
using docusystem.Pages.Approvals;
using docusystem.Pages.RevisionHistory;
using docusystem.Pages.Forms;
using docusystem.Services;

namespace docusystem;

public static class MauiProgram
{
	public static MauiApp CreateMauiApp()
	{
		var builder = MauiApp.CreateBuilder();
		builder
			.UseMauiApp<App>()
			.ConfigureFonts(fonts =>
			{
				fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
				fonts.AddFont("OpenSans-Semibold.ttf", "OpenSansSemibold");
			});

		var appConfig = ApiSettingsLoader.Load();
		builder.Services.AddSingleton(appConfig.Api);
		builder.Services.AddSingleton(appConfig.Data);
		builder.Services.AddSingleton(appConfig.Auth);
		builder.Services.AddSingleton(appConfig.Supabase);
		builder.Services.AddSingleton<SupabaseService>();

		builder.Services.AddSingleton<SessionPersistenceService>();

		builder.Services.AddSingleton<ISessionService, SessionService>();

		builder.Services.AddTransient<LaravelAuthDelegatingHandler>();
		builder.Services.AddHttpClient("LaravelApi")
			.AddHttpMessageHandler<LaravelAuthDelegatingHandler>()
			.ConfigureHttpClient((sp, client) =>
			{
				var opts = sp.GetRequiredService<ApiEndpointOptions>();
				client.BaseAddress = new Uri(ApiEndpointOptions.NormalizeBaseUrl(opts.LaravelBaseUrl));
				client.DefaultRequestHeaders.TryAddWithoutValidation("Accept", "application/json");
				client.Timeout = TimeSpan.FromSeconds(30);
			});

		builder.Services.AddSingleton<AppSessionService>();
		builder.Services.AddSingleton<IAuthService, AuthService>();
		builder.Services.AddSingleton<IApiService, ApiService>();
		builder.Services.AddSingleton<IProposalService, ProposalService>();
		builder.Services.AddSingleton<INotificationService, NotificationService>();
		builder.Services.AddSingleton<IRevisionService, RevisionService>();
		builder.Services.AddSingleton<IApprovalService, ApprovalService>();

		builder.Services.AddSingleton<AppShell>();

		builder.Services.AddTransient<LoginPage>();
		builder.Services.AddTransient<DashboardPage>();
		builder.Services.AddTransient<NotificationsPage>();
		builder.Services.AddTransient<PendingApprovalsPage>();
		builder.Services.AddTransient<ProposalDetailsPage>();
		builder.Services.AddTransient<RevisionHistoryPage>();
		builder.Services.AddTransient<ActivityRequestFormPage>();
		builder.Services.AddTransient<ProposalFormPage>();

#if DEBUG
		builder.Logging.AddDebug();
#endif

		return builder.Build();
	}
}
