namespace XKit.Redactor;

/// <summary>
/// What a secret a redactor has found is turned into. Finding a secret and deciding how much of it
/// may survive are separate questions, and only the caller knows the answer to the second one.
/// </summary>
public enum RedactionMode
{
	/// <summary>
	/// Nothing of the secret survives - not a character, not its length, not whether it was null. The
	/// default, and the only safe answer when the text is going somewhere it cannot be recalled from.
	/// </summary>
	Erase,

	/// <summary>
	/// Each secret is replaced by its <see cref="SecretMaskingExtensions.Mask"/>, so two different
	/// secrets in the same text stay tellable apart - enough to answer "is this still the key I
	/// rotated?" without publishing either. It costs up to three characters per end of every secret
	/// it touches, so it is opt-in and never the default. Null and empty come back unchanged in this
	/// mode.
	/// </summary>
	Mask,
}
