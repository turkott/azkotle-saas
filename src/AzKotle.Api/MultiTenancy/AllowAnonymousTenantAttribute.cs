namespace AzKotle.Api.MultiTenancy;

[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = false, Inherited = true)]
public sealed class AllowAnonymousTenantAttribute : Attribute
{
}
