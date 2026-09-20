using System.Diagnostics.CodeAnalysis;

namespace XKit.Redactor;

/// <summary>
/// The key-shaped call that predates <see cref="RedactorOptions"/>, kept so existing callers
/// compile unchanged.
/// </summary>
public static class RedactorExtensions
{
	/// <summary>
	/// Redacts <paramref name="value"/> found under <paramref name="key"/>, erasing whatever is found.
	/// Equivalent to passing <c>new RedactorOptions { Key = key }</c>.
	/// </summary>
	[return: NotNullIfNotNull(nameof(value))]
	public static string? Redact(this IRedactor redactor, string? value, string? key = null)
	{
		return redactor.Redact(value, key is null ? null : new RedactorOptions { Key = key });
	}
}
