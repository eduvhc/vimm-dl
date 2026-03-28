namespace Module.Core.Pipeline;

/// <summary>
/// Result of a pipeline's duplicate check. Returned by IPipeline.CheckDuplicate.
/// Null return means "not a duplicate" — the user can re-download freely.
/// Non-null means "duplicate found" — the user should confirm before adding.
/// </summary>
public record DuplicateCheckResult(string Reason, bool ArchiveExists, bool IsoExists);
