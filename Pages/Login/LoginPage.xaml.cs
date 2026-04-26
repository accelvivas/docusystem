namespace docusystem.Pages.Login;

using docusystem.Services;

/// <summary>
/// Login screen — layout in LoginPage.xaml. Authentication via <see cref="IAuthService"/> (Laravel API).
/// </summary>
public partial class LoginPage : ContentPage
{
	private bool isPasswordVisible;

	private readonly IAuthService _authService;

	public LoginPage(IAuthService authService)
	{
		InitializeComponent();
		_authService = authService;
	}

	private void OnShowPasswordToggled(object? sender, EventArgs e)
	{
		isPasswordVisible = !isPasswordVisible;
		PasswordEntry.IsPassword = !isPasswordVisible;
		ShowPasswordBtnToggle.Text = isPasswordVisible ? "Hide" : "Show";
	}

	private async void OnLoginClicked(object? sender, EventArgs e)
	{
		ClearValidationErrors();

		if (!ValidateInputs())
		{
			return;
		}

		try
		{
			ShowLoadingState(true);

			var email = EmailEntry.Text?.Trim() ?? string.Empty;
			var password = PasswordEntry.Text ?? string.Empty;

			var result = await _authService.LoginAsync(email, password);
			if (!result.Success || result.User is null)
			{
				PasswordErrorLabel.Text = string.IsNullOrWhiteSpace(result.Message)
					? "Invalid email or password."
					: result.Message;
				PasswordErrorLabel.IsVisible = true;
				return;
			}

			// Session (token + user) is already saved by IAuthService / ISessionService during LoginAsync, before this line runs.

			if (Shell.Current is AppShell appShell)
			{
				appShell.SetAuthenticatedState(true);
			}

			await Shell.Current.GoToAsync("//dashboard");
		}
		catch (Exception ex)
		{
			await DisplayAlertAsync("Error", $"An unexpected error occurred: {ex.Message}", "OK");
		}
		finally
		{
			ShowLoadingState(false);
		}
	}

	private bool ValidateInputs()
	{
		var isValid = true;

		var email = EmailEntry.Text?.Trim() ?? string.Empty;
		if (string.IsNullOrEmpty(email))
		{
			EmailErrorLabel.Text = "Email is required";
			EmailErrorLabel.IsVisible = true;
			isValid = false;
		}
		else if (!email.Contains('@') || !email.Contains('.'))
		{
			EmailErrorLabel.Text = "Please enter a valid email address";
			EmailErrorLabel.IsVisible = true;
			isValid = false;
		}

		var password = PasswordEntry.Text ?? string.Empty;
		if (string.IsNullOrEmpty(password))
		{
			PasswordErrorLabel.Text = "Password is required";
			PasswordErrorLabel.IsVisible = true;
			isValid = false;
		}
		return isValid;
	}

	private void ClearValidationErrors()
	{
		EmailErrorLabel.IsVisible = false;
		PasswordErrorLabel.IsVisible = false;
	}

	private void ShowLoadingState(bool isLoading)
	{
		LoginBtn.IsEnabled = !isLoading;
		LoginBtn.Opacity = isLoading ? 0.6 : 1.0;
		LoadingIndicator.IsRunning = isLoading;
		LoadingIndicator.IsVisible = isLoading;
	}

	private async void OnForgotPasswordClicked(object? sender, EventArgs e)
	{
		await DisplayAlertAsync("Forgot Password", "Password recovery will use your Laravel web flow or API.", "OK");
	}

	private async void OnSignUpClicked(object? sender, EventArgs e)
	{
		await DisplayAlertAsync("Sign Up", "Registration is handled through the NU Lipa web system.", "OK");
	}
}
