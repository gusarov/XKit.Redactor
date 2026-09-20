namespace XKit.Redactor.Tests;

public class SecretMaskingExtensionsTests
{
	private const string Hidden = RedactorOptions.DefaultMaskToken;

	[Test]
	public void Should_hand_back_null_and_empty_unchanged()
	{
		// Mask() is the partial-reveal mode applied to an isolated value, and that mode shows
		// whether there was anything to hide. Erase is what hides that, and it is not this method.
		Assert.Multiple(() =>
		{
			Assert.That(((string?)null).Mask(), Is.Null);
			Assert.That("".Mask(), Is.EqualTo(""));
		});
	}

	/// <summary>
	/// Every length at which the rule changes gets its own case, spelled out rather than computed.
	/// The rule was given as "more than 8 -> 1, more than 10 -> 2, more than 12 -> 3", and 8/9/10/11/
	/// 12/13 are exactly the lengths that distinguish that reading from a <c>&gt;=</c> one. If the
	/// intent was <c>&gt;= 12 -&gt; 3</c>, the case to change is the 12 one and nothing else.
	/// </summary>
	[TestCase("12345678", Hidden)] // 8 - the longest value that reveals nothing
	[TestCase("123456789", $"1{Hidden}9")] // 9 - the first that reveals anything
	[TestCase("1234567890", $"1{Hidden}0")] // 10 - still one per end
	[TestCase("1234567890a", $"12{Hidden}0a")] // 11 - two per end
	[TestCase("1234567890ab", $"12{Hidden}ab")] // 12 - still two: "more than 12", not ">= 12"
	[TestCase("1234567890abc", $"123{Hidden}abc")] // 13 - three per end, the cap
	public void Should_reveal_more_of_a_longer_secret_up_to_three_characters_per_end(string value, string expected)
	{
		Assert.That(value.Mask(), Is.EqualTo(expected));
	}

	[TestCase("1")]
	[TestCase("1234")]
	[TestCase("1234567")]
	public void Should_reveal_nothing_for_a_secret_of_eight_characters_or_fewer(string value)
	{
		Assert.That(value.Mask(), Is.EqualTo(Hidden));
	}

	[Test]
	public void Should_not_grow_the_mask_with_the_secret()
	{
		// The same token whatever is hidden behind it - a character per hidden character would
		// publish the length of the key, which is itself worth knowing to an attacker.
		var masked = "0123456789abcdefghijklmnopqrstuvwxyz".Mask();
		Assert.That(masked, Is.EqualTo($"012{Hidden}xyz"));
	}

	[Test]
	public void Should_not_let_the_revealed_ends_overlap()
	{
		// The two ends are taken from the same string; at 9 characters they must not share one.
		var masked = "abcdefghi".Mask();
		Assert.That(masked, Is.EqualTo($"a{Hidden}i"));
	}

	[Test]
	public void Should_use_the_callers_mask_token()
	{
		Assert.Multiple(() =>
		{
			Assert.That("0123456789abcdefghijklmnopqrstuvwxyz".Mask("***"), Is.EqualTo("012***xyz"));
			Assert.That("short".Mask("***"), Is.EqualTo("***"));
		});
	}
}
