using System.Diagnostics.CodeAnalysis;

namespace XKit.Redactor;

/// <summary>
/// Masks a secret that is about to be logged: enough survives to tell two keys apart or to confirm
/// which one a host picked up, never enough to use one.
///
/// <para>
/// <b>The whole value must be the secret.</b> This reveals characters from each end, so it is only
/// safe on something opaque - an API key, a token, a password on its own. Never pass it text that
/// merely <i>contains</i> a secret, such as a connection string or a URL: the ends of those are not
/// the harmless parts, and <c>"...?password=Sup3rS3cret"</c> would come back as <c>"...ret"</c>.
/// Text of unknown shape goes through an <see cref="IRedactor"/> instead.
/// </para>
///
/// <para>
/// The middle is always the mask token, never one character per hidden character, so the output
/// does not leak the secret's length either.
/// </para>
/// </summary>
public static class SecretMaskingExtensions
{
	/// <summary>
	/// Reveals up to three characters at each end, and fewer - down to none - the shorter the secret
	/// is: on an 8-character value even one character per end is an eighth of the secret, so nothing
	/// at all is shown. This is <see cref="RedactionMode.Mask"/> applied to a value the caller has
	/// already isolated, so null and empty come back unchanged, the way that mode treats them.
	/// </summary>
	/// <param name="value">The secret, and nothing but the secret.</param>
	/// <param name="maskToken">What hides the middle; <see cref="RedactorOptions.DefaultMaskToken"/> when null.</param>
	[return: NotNullIfNotNull(nameof(value))]
	public static string? Mask(this string? value, string? maskToken = null)
	{
		if (string.IsNullOrEmpty(value))
		{
			return value;
		}

		maskToken ??= RedactorOptions.DefaultMaskToken;

		var revealed = value!.Length switch
		{
			<= 8 => 0,
			<= 10 => 1,
			<= 12 => 2,
			_ => 3,
		};

		if (revealed == 0)
		{
			return maskToken;
		}

		return value.Substring(0, revealed) + maskToken + value.Substring(value.Length - revealed);
	}
}
