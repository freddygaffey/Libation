using AudibleApi;
using AudibleApi.Authorization;
using AudibleApi.Cryptography;
using Dinah.Core.Security;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace LibationMobile.Services;

/// <summary>
/// Reads an account exported from Libation desktop (Settings > Accounts > Export), which uses the
/// mkb79/audible-cli format. Mirrors Libation's own importer, <c>AudibleUtilities.Mkb79Auth</c>, without its
/// desktop account storage.
/// </summary>
public static class AudibleCliAccountImport
{
	/// <summary>Validate the export, refresh its access token, and save it as this app's Audible identity.</summary>
	/// <returns>The AudibleApi name of the account's marketplace, such as "us" or "australia".</returns>
	/// <exception cref="InvalidDataException">The text is not a usable account export.</exception>
	public static async Task<string> ImportAsync(string json, string identityFile)
	{
		// A common mix-up: Libation's master key (Settings > Export master key) only unlocks Libation's own token
		// file on the computer and has no Audible sign-in in it. An account export is JSON; anything else gets this.
		if (!json.TrimStart().StartsWith('{'))
			throw new InvalidDataException("That isn't an account export. If it's Libation's master key, that only unlocks Libation on your computer. In Libation, use Settings > Accounts, then Export on your account's row, and paste that file instead.");

		JObject export;
		try
		{
			export = JObject.Parse(json);
		}
		catch (Newtonsoft.Json.JsonException)
		{
			throw new InvalidDataException("That is not an account export. In Libation on your computer, use Settings > Accounts > Export, then paste the whole file here.");
		}

		var refreshToken = Required(export, "refresh_token");
		var adpToken = Required(export, "adp_token");
		var privateKey = Required(export, "device_private_key");
		var countryCode = Required(export, "locale_code");
		var withUsername = export.Value<bool?>("with_username") ?? false;

		var locale = Localization.Locales.FirstOrDefault(l => l.CountryCode == countryCode && l.WithUsername == withUsername)
			?? throw new InvalidDataException($"The export is for an Audible store this app does not know: {countryCode}.");

		// The access token in an export is usually expired. Refreshing it also proves the tokens still work.
		var accessToken = await new Authorize(locale).RefreshAccessTokenAsync(new RefreshToken(refreshToken));

		var cookies = (export["website_cookies"] as JObject)?.Properties()
			.Where(p => p.Value.Type == JTokenType.String)
			.Select(p => new KeyValuePair<string, SecretString>(p.Name, p.Value.Value<string>()!))
			.ToList();
		var device = export["device_info"] as JObject;
		var storeCookie = (export["store_authentication_cookie"] as JObject)?.Value<string>("cookie");

		var identity = new Identity(locale);
		identity.Update(
			new PrivateKey(privateKey),
			new AdpToken(adpToken),
			accessToken,
			new RefreshToken(refreshToken),
			cookies,
			device?.Value<string>("device_serial_number"),
			device?.Value<string>("device_type"),
			(export["customer_info"] as JObject)?.Value<string>("user_id"),
			device?.Value<string>("device_name"),
			storeCookie ?? "");

		File.Delete(identityFile);
		using (new IdentityPersister(identity, identityFile)) { }

		return locale.Name;
	}

	private static string Required(JObject export, string name)
		=> export.Value<string>(name) is { Length: > 0 } value
			? value
			: throw new InvalidDataException($"The account export is missing {name}. Export the account again from Libation on your computer.");
}
