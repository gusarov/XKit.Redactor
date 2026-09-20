#if NETSTANDARD2_0
// netstandard2.0 predates the nullable attributes. Declaring this one here lets the public surface
// carry the same annotations on every target; consumers' compilers find it by name in metadata.
namespace System.Diagnostics.CodeAnalysis
{
	[AttributeUsage(AttributeTargets.Parameter | AttributeTargets.Property | AttributeTargets.ReturnValue, AllowMultiple = true, Inherited = false)]
	internal sealed class NotNullIfNotNullAttribute : Attribute
	{
		public NotNullIfNotNullAttribute(string parameterName)
		{
			ParameterName = parameterName;
		}

		public string ParameterName { get; }
	}
}
#endif
