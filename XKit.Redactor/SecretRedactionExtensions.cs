using System.Diagnostics.CodeAnalysis;

namespace XKit.Redactor;

/// <summary>
/// Removes a <i>known</i> secret from text that is about to be logged, notified, or carried inside
/// an exception. Use this when the text comes from somewhere you do not control - a driver message,
/// a third-party error - and might quote a credential back at you. When the secret's value is not
/// known but its shape is, an <see cref="IRedactor"/> finds it instead.
/// </summary>
public static class SecretRedactionExtensions
{
	/// <summary>
	/// A standalone replacement of anything shorter than this would hit unrelated words and garble
	/// the text it is meant to keep readable. The secret as a whole is always replaced regardless.
	/// </summary>
	private const int MinimumDistinctiveLength = 4;

	/// <summary>
	/// Replaces every occurrence of <paramref name="secret"/> in <paramref name="text"/>, and for a
	/// URL-shaped secret such as a connection string also replaces the password on its own, in case
	/// only that part was echoed.
	///
	/// <para>
	/// Under <see cref="RedactionMode.Mask"/> an opaque secret is masked, but a URL-shaped one is
	/// still erased: its tail is the host, the database or a query string, and revealing either end
	/// of it says nothing useful while risking the password. Only the password on its own is masked.
	/// </para>
	/// </summary>
	[return: NotNullIfNotNull(nameof(text))]
	public static string? Redact(this string? text, string? secret, RedactorOptions? options = null)
	{
		var mode = options?.Mode ?? RedactionMode.Erase;
		var maskToken = options?.MaskToken ?? RedactorOptions.DefaultMaskToken;

		if (string.IsNullOrEmpty(text))
		{
			return mode == RedactionMode.Erase ? maskToken : text;
		}

		if (string.IsNullOrEmpty(secret))
		{
			return text;
		}

		var isUrlShaped = secret!.IndexOf("://", StringComparison.Ordinal) >= 0;
		var redacted = text!.Replace(secret, Hide(secret, isolated: !isUrlShaped, mode, maskToken));

		var password = PasswordOf(secret);
		if (password.Length >= MinimumDistinctiveLength)
		{
			redacted = redacted.Replace(password, Hide(password, isolated: true, mode, maskToken));
		}

		return redacted;
	}

	private static string Hide(string secret, bool isolated, RedactionMode mode, string maskToken)
	{
		return mode == RedactionMode.Mask && isolated
			? secret.Mask(maskToken)
			: maskToken;
	}

	/// <summary>
	/// The password out of <c>scheme://user:password@host</c>, or empty when there is none.
	/// </summary>
	private static string PasswordOf(string url)
	{
		var schemeEnd = url.IndexOf("://", StringComparison.Ordinal);
		var at = url.LastIndexOf('@');
		if (schemeEnd < 0 || at <= schemeEnd)
		{
			return string.Empty;
		}

		var userInfo = url.Substring(schemeEnd + 3, at - schemeEnd - 3);
		var colon = userInfo.IndexOf(':');
		return colon < 0 || colon + 1 >= userInfo.Length
			? string.Empty
			: userInfo.Substring(colon + 1);
	}
}
