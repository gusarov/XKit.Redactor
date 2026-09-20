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
	/// What <see cref="RedactionMode.Label"/> says in place of the secret, when the caller knows
	/// something no redactor can work out for itself - the classic being
	/// <c>ex.GetType().Name</c>, which turns "hidden" into "hidden because the connection string
	/// would not parse".
	///
	/// <para>
	/// <b>Labels are caller-driven.</b> A redactor resolves one as <c>Label ?? Key</c> and stops
	/// there - both come from the caller, and no redactor ever authors one of its own. With neither
	/// set, <see cref="RedactionMode.Label"/> is exactly <see cref="RedactionMode.Erase"/>.
	/// </para>
	///
	/// <para>
	/// That is deliberate, and not only for safety. A rule that matches inside a larger text leaves
	/// the surrounding structure readable on purpose, so a label the package invented there could
	/// only restate what is already on screen - <c>mongodb://●●●userinfo●●●@host/db</c> says nothing
	/// the <c>://…@</c> did not. A label earns its place where the <i>whole</i> value is hidden and
	/// no context survives to speak for it.
	/// </para>
	///
	/// <para>
	/// <b>Only ever set this to something the code chose.</b> Anything derived from the value itself
	/// - a message, a length, a first character - defeats the mode, which promises that nothing of
	/// the value survives.
	/// </para>
	/// </summary>
	public string? Label { get; set; }

	/// <summary>
	/// What a secret a redactor has found becomes, according to <see cref="Mode"/>. Every redactor
	/// goes through here, so the three modes cannot drift apart between them - and a fourth would be
	/// added in one place.
	/// </summary>
	/// <param name="secret">
	/// The span the redactor found. Read only by <see cref="RedactionMode.Mask"/>; the other two
	/// modes never look at it, which is what makes them safe on a span of unknown shape.
	/// </param>
	/// <param name="isolated">
	/// Whether <paramref name="secret"/> is the secret and nothing else. Only then may
	/// <see cref="RedactionMode.Mask"/> apply - it reveals both ends, and on a composite span the
	/// revealed end can be the secret itself.
	/// </param>
	public string Hide(string secret, bool isolated = true)
	{
		switch (Mode)
		{
			case RedactionMode.Mask:
				return isolated && !string.IsNullOrEmpty(secret)
					? secret.Mask(MaskToken)!
					: MaskToken;

			case RedactionMode.Label:
				var label = FirstNonEmpty(Label, Key);
				if (label is null)
				{
					return MaskToken;
				}

				// Half the token each side, capped at three, so a caller shortening the token cannot
				// slice past its end.
				var revealed = Math.Min(3, MaskToken.Length / 2);
				return MaskToken.Substring(0, revealed) + label + MaskToken.Substring(MaskToken.Length - revealed);

			default:
				return MaskToken;
		}
	}

	private string? FirstNonEmpty(params string?[] candidates)
	{
		foreach (var candidate in candidates)
		{
			if (!string.IsNullOrEmpty(candidate))
			{
				return candidate;
			}
		}

		return null;
	}

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
