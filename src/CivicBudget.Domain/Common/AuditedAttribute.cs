namespace CivicBudget.Domain.Common;

/// <summary>
/// Marks an entity whose changes are recorded in the audit trail (entity, field, old value, new value,
/// user, UTC time). The audit interceptor in Infrastructure looks for this attribute; nothing else
/// needs to know. Opt-in by attribute rather than "audit everything" so the audit table itself, and
/// high-churn tables that carry no financial meaning, stay out.
/// </summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class AuditedAttribute : Attribute;
