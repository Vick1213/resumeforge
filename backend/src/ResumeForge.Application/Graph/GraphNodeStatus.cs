namespace ResumeForge.Application.Graph;

/// <summary>The terminal state a graph node reached during a run.</summary>
public enum GraphNodeStatus
{
    /// <summary>The node's body ran to completion without throwing.</summary>
    Succeeded,

    /// <summary>The node's body threw, or its condition threw.</summary>
    Failed,

    /// <summary>
    /// The node did not run, either because its own <c>.When(...)</c> condition was
    /// false, or because a dependency failed or was itself skipped for that reason.
    /// </summary>
    Skipped,

    /// <summary>The node did not run, or was interrupted, because the run was cancelled.</summary>
    Cancelled,
}
