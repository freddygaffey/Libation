using AudibleApi;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace LibationMobile.Services;

/// <summary>The signed-in Audible account: its tokens on disk, and an API client built from them.</summary>
public class AudibleAccount
{
	/// <summary>Audible marketplaces, by AudibleApi name, with the names people know them by.</summary>
	public static IReadOnlyList<(string Name, string DisplayName)> Regions { get; } =
	[
		("us", "United States"),
		("uk", "United Kingdom"),
		("australia", "Australia"),
		("canada", "Canada"),
		("germany", "Germany"),
		("france", "France"),
		("italy", "Italy"),
		("spain", "Spain"),
		("india", "India"),
		("japan", "Japan"),
		("brazil", "Brazil"),
	];

	private readonly string identityFile;
	private readonly MobileSettings settings;
	private readonly SemaphoreSlim apiGate = new(1, 1);
	private Api? api;

	public AudibleAccount(string identityFile, MobileSettings settings)
	{
		this.identityFile = identityFile;
		this.settings = settings;
	}

	public bool IsSignedIn => File.Exists(identityFile) && settings.RegionName is not null;

	/// <summary>The account's Audible marketplace. Only valid while signed in.</summary>
	public Locale Locale => Localization.Get(settings.RegionName);

	/// <summary>
	/// Sign in through Amazon's web page. <paramref name="login"/> shows the page and returns the URL Amazon
	/// redirects to once the user has signed in. AudibleApi then registers this app as an Android device and
	/// saves the tokens.
	/// </summary>
	public async Task SignInAsync(string regionName, ILoginChoiceEager login)
	{
		await apiGate.WaitAsync();
		try
		{
			File.Delete(identityFile);
			api = await EzApiCreator.GetApiAsync(login, Localization.Get(regionName), identityFile, registrationProfile: DeviceRegistrationProfile.Default);
			settings.RegionName = regionName;
		}
		finally
		{
			apiGate.Release();
		}
	}

	/// <summary>Sign in with an account exported from Libation desktop, instead of signing in again.</summary>
	public async Task ImportAsync(string exportJson)
	{
		await apiGate.WaitAsync();
		try
		{
			api = null;
			settings.RegionName = await AudibleCliAccountImport.ImportAsync(exportJson, identityFile);
		}
		finally
		{
			apiGate.Release();
		}
	}

	/// <summary>An API client from the saved tokens, refreshing them if they have expired.</summary>
	public async Task<Api> GetApiAsync()
	{
		await apiGate.WaitAsync();
		try
		{
			if (!IsSignedIn)
				throw new System.InvalidOperationException("Not signed in to Audible.");
			return api ??= await EzApiCreator.GetApiAsync(Localization.Get(settings.RegionName!), identityFile);
		}
		finally
		{
			apiGate.Release();
		}
	}

	/// <summary>Forget the account. Downloaded books stay on the device.</summary>
	public void SignOut()
	{
		api = null;
		File.Delete(identityFile);
		settings.RegionName = null;
	}

	public static string DisplayNameOf(string? regionName)
		=> Regions.FirstOrDefault(r => r.Name == regionName).DisplayName ?? regionName ?? "";
}
