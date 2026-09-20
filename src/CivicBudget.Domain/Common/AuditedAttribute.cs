namespace CivicBudget.Domain.Common;

/// <summary>
/// Marks an entity whose changes are recorded in the audit trail (entity, field, old value, new value,
/// user, UTC time). The audit interceptor in Infrastructure looks for this attribute; nothing else
/// needs to know. Opt-in by attribute rather than "audit everything" so the audit table itself, and
/// high-churn tables that carry no financial meaning, stay out.
/// </summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class AuditedAttribute : Attribute;

/// <summary>
/// Opts one property of an audited entity out of field-level auditing. For bookkeeping fields whose
/// change is already recorded as a named event (who submitted, when it was returned), a second
/// "changed Submitted by user id: seed → c199..." row on the timeline is noise, not evidence.
/// </summary>
[AttributeUsage(AttributeTargets.Property, Inherited = false)]
public sealed class NotAuditedAttribute : Attribute;
