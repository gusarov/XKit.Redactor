namespace XKit.Redactor;

/// <summary>
/// Everything a caller can tell a redactor beyond the text itself. New knobs go here rather than on
/// the <see cref="IRedactor.Redact"/> signature.
/// </summary>
public class RedactorOptions
{
	/// <summary>
	/// What stands where an erased secret was, and what sits in the middle of a masked one. Eight
	/// bullets whatever was hidden, so the output carries neither the secret nor its length.
	/// </summary>
	public const string DefaultMaskToken = "●●●●●●●●";

	/// <summary>
	/// Fragments that make a key name look like it holds a credential. Case-insensitive substring
	/// matches, and written to over-match: "key" alone catches "Poloniex:Key" and "MonkeyCount"
	/// alike, because a harmless value hidden costs a debugging session and a live key printed
	/// costs a rotation.
	/// </summary>
	private static readonly string[] _secretKeyMarkers =
	[
		"password",
		"pwd",
		"secret",
		"apikey",
		"api_key",
		"token",
		"clientsecret",
		"client_secret",
		"privatekey",
		"private_key",
		"key",
		"credential",
		"connectionstring",
	];

	/// <summary>
	/// The name the value was found under - a JSON key, a config key, a header name. A detector may
	/// use it as a hint; it is not on its own a decision.
	/// </summary>
	public string? Key { get; set; }

	/// <summary>
	/// What each secret found becomes. <see cref="RedactionMode.Erase"/> unless a caller opts in to
	/// partial reveal.
	/// </summary>
	public RedactionMode Mode { get; set; } = RedactionMode.Erase;

	/// <summary>
	/// Replaces <see cref="DefaultMaskToken"/> for callers that need an ASCII-only log, or a token
	/// their existing tests already expect. Keep it fixed-width: a token that grows with the secret
	/// publishes the secret's length.
	/// </summary>
	public string MaskToken { get; set; } = DefaultMaskToken;

	/// <summary>
	/// Whether <see cref="Key"/> reads as the name of a credential. Detectors use this to lower
	/// their bar (the entropy detector) or as a last resort when no structural rule fired (the
	/// credential detector).
	/// </summary>
	public bool KeyLooksSecret
	{
		get
		{
			if (string.IsNullOrEmpty(Key))
			{
				return false;
			}

			foreach (var marker in _secretKeyMarkers)
			{
				if (Key!.IndexOf(marker, StringComparison.OrdinalIgnoreCase) >= 0)
				{
					return true;
				}
			}

			return false;
		}
	}
}
