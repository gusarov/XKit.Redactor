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
	/// The highest priority of three sources. A redactor resolves the label as
	/// <c>Label ?? Key ?? &lt;name of the rule that matched&gt;</c>, so the mode is useful with
	/// nobody passing anything, and exact where somebody does. With none of the three resolvable the
	/// output falls back to the plain mask token, i.e. to <see cref="RedactionMode.Erase"/>.
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
	/// <param name="fallbackLabel">
	/// What the redactor itself knows - the name of the rule that matched, say. Lowest priority,
	/// behind <see cref="Label"/> and <see cref="Key"/>.
	/// </param>
	public string Hide(string secret, bool isolated = true, string? fallbackLabel = null)
	{
		switch (Mode)
		{
			case RedactionMode.Mask:
				return isolated && !string.IsNullOrEmpty(secret)
					? secret.Mask(MaskToken)!
					: MaskToken;

			case RedactionMode.Label:
				var label = FirstNonEmpty(Label, Key, fallbackLabel);
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
