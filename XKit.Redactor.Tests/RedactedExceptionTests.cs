using System.Reflection;

namespace XKit.Redactor.Tests;

public class RedactedExceptionTests
{
	private const string Hidden = RedactorOptions.DefaultMaskToken;
	private const string ConnectionString = "mongodb://apex:Sup3rS3cretP4ss@mongo.xkit.tools/db";

	/// <summary>
	/// The guarantee that gives the type its name, expressed the way the compiler enforces it: there
	/// is no way to build one without handing over the original. If a constructor is ever added that
	/// takes only a message, this fails.
	/// </summary>
	[Test]
	public void Should_offer_no_constructor_that_omits_the_inner_exception()
	{
		var constructors = typeof(RedactedException).GetConstructors(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);

		Assert.That(constructors, Is.Not.Empty);
		foreach (var constructor in constructors)
		{
			Assert.That(
				constructor.GetParameters().Any(p => typeof(Exception).IsAssignableFrom(p.ParameterType))
				, Is.True
				, $"{constructor} can be called without an inner exception"
			);
		}
	}

	[Test]
	public void Should_redact_the_message_on_construction()
	{
		var exception = new RedactedException($"'{ConnectionString}' is not valid.", new FormatException());

		Assert.Multiple(() =>
		{
			Assert.That(exception.Message, Does.Not.Contain("Sup3rS3cretP4ss"));
			Assert.That(exception.Message, Is.EqualTo($"'mongodb://{Hidden}@mongo.xkit.tools/db' is not valid."), "the host and database still say which connection failed");
		});
	}

	[Test]
	public void Should_keep_the_original_exception_object_rather_than_a_copy_of_its_text()
	{
		var original = new FormatException("the original");

		var exception = new RedactedException("wrapped", original);

		Assert.That(exception.InnerException, Is.SameAs(original), "a copy of the text is not the exception");
	}

	[Test]
	public void Should_keep_the_originals_stack_trace()
	{
		Exception original;
		try
		{
			throw new FormatException("thrown so it has a stack");
		}
		catch (FormatException ex)
		{
			original = ex;
		}

		var exception = new RedactedException("wrapped", original);

		Assert.Multiple(() =>
		{
			Assert.That(exception.InnerException!.StackTrace, Is.Not.Null);
			Assert.That(exception.ToString(), Does.Contain(nameof(Should_keep_the_originals_stack_trace)));
		});
	}

	/// <summary>
	/// The reason an overridden <c>ToString()</c> earns its place: the secret can be in the inner
	/// exception's message, which this type never got to redact on construction.
	/// </summary>
	[Test]
	public void Should_redact_a_secret_that_only_the_inner_exception_quotes()
	{
		var original = new FormatException($"driver echoed {ConnectionString} back at us");

		var exception = new RedactedException("could not connect", original);

		Assert.Multiple(() =>
		{
			Assert.That(exception.ToString(), Does.Not.Contain("Sup3rS3cretP4ss"));
			Assert.That(exception.ToString(), Does.Contain("mongo.xkit.tools"), "everything that is not the secret survives");
			Assert.That(original.Message, Does.Contain("Sup3rS3cretP4ss"), "the original object is untouched - only the formatted view is redacted");
		});
	}

	[Test]
	public void Should_not_let_a_subclass_unredact_the_formatted_chain()
	{
		// ToString() is a sealed override, so a derived type cannot hand the secret back. This is the
		// runtime half of that; the compile-time half is that overriding it does not build.
		var method = typeof(RedactedException).GetMethod(nameof(ToString), BindingFlags.Public | BindingFlags.Instance);

		Assert.That(method!.IsFinal, Is.True, "ToString() must stay sealed or the guarantee is only a convention");
	}

	[Test]
	public void Should_fall_back_to_the_shared_credential_redactor()
	{
		var exception = new RedactedException($"pwd={ConnectionString}", new FormatException(), redactor: null);

		Assert.That(exception.Message, Does.Not.Contain("Sup3rS3cretP4ss"));
	}

	[Test]
	public void Should_use_the_redactor_it_is_given()
	{
		// A redactor that hides everything proves the argument is actually consulted.
		var exception = new RedactedException("nothing secret here", new FormatException(), new EverythingIsSecret());

		Assert.Multiple(() =>
		{
			Assert.That(exception.Message, Is.EqualTo("[redacted]"));
			Assert.That(exception.ToString(), Is.EqualTo("[redacted]"), "and again when it is formatted");
		});
	}

	[Test]
	public void Should_honour_the_options_when_formatting_as_well_as_constructing()
	{
		var options = new RedactorOptions { MaskToken = "***" };
		var original = new FormatException($"inner quoted {ConnectionString}");

		var exception = new RedactedException($"outer quoted {ConnectionString}", original, options: options);

		Assert.Multiple(() =>
		{
			Assert.That(exception.Message, Does.Contain("mongodb://***@"));
			Assert.That(exception.ToString(), Does.Contain("mongodb://***@"), "the same token on the inner message");
			Assert.That(exception.ToString(), Does.Not.Contain(Hidden), "not the default token anywhere");
		});
	}

	[Test]
	public void Should_refuse_a_null_inner_exception()
	{
		Assert.That(
			() => new RedactedException("wrapped", null!)
			, Throws.ArgumentNullException.With.Message.Contains("stack trace")
		);
	}

	[Test]
	public void Should_erase_a_null_message_like_any_other_value()
	{
		var exception = new RedactedException(null, new FormatException());

		Assert.That(exception.Message, Is.EqualTo(Hidden), "never 'Exception of type ... was thrown'");
	}

	[Test]
	public void Should_survive_being_chained_inside_another_redacted_exception()
	{
		var inner = new RedactedException($"inner saw {ConnectionString}", new FormatException());

		var outer = new RedactedException("outer", inner);

		Assert.Multiple(() =>
		{
			Assert.That(outer.ToString(), Does.Not.Contain("Sup3rS3cretP4ss"));
			Assert.That(outer.InnerException, Is.SameAs(inner));
		});
	}

	private class EverythingIsSecret : IRedactor
	{
		public string? Redact(string? value, RedactorOptions? options = null)
		{
			return "[redacted]";
		}
	}
}
