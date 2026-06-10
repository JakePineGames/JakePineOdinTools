// Copyright (c) 2026 Jake Pine
// SPDX-License-Identifier: MIT
// This software is provided "as is", without warranty of any kind. Use at your own risk.

using System;

/// <summary>
/// Marker attributes that drive source-based attribute propagation (the "batch" system) read by
/// <c>OdinSourceAttributeChannelProcessor</c>. They carry no inspector behaviour themselves — they are
/// parsed from source to decide which real attributes (FoldoutGroup, GUIColor, ReadOnly, etc.) get copied
/// onto which fields. The processor strips them so they never reach Odin's inspector pipeline.
///
/// Pattern:
///   [FoldoutGroup("Stats"), ReadOnly, GUIColor("red"), BatchBegin("b")]  // all OTHER attributes in this []
///   public int field1;                                                    // are the batch's attribute set
///   public int field2;                                                    // also receives the batch
///   [BatchEnd("b")]                                                        // field3 still included (inclusive)
///   public int field3;
///
/// All four markers take an optional name. The empty name "" is a valid batch (handy when you only need one).
/// </summary>

/// <summary>
/// Opens a batch named <see cref="Name"/>. Every OTHER attribute inside the SAME <c>[ ]</c> becomes the batch's
/// attribute set, applied to this field and every following field through the matching <see cref="BatchEndAttribute"/>
/// (inclusive) — or to the end of the class body if no end is given. BatchBegin also performs an implicit
/// <see cref="BatchDefineAttribute"/>, so the same name can be reused later with <see cref="BatchApplyAttribute"/>.
/// If two batch markers share a single <c>[ ]</c>, BatchBegin always wins and the first one wins among duplicates.
/// A bare <c>[BatchBegin("b")]</c> with no other attributes re-opens a previously defined batch "b".
/// </summary>
[AttributeUsage(AttributeTargets.Field | AttributeTargets.Property, AllowMultiple = true, Inherited = true)]
public sealed class BatchBeginAttribute : Attribute
{
    public string Name { get; }

    public BatchBeginAttribute(string name = "")
    {
        Name = name ?? string.Empty;
    }
}

/// <summary>
/// Closes the batch named <see cref="Name"/>. The field carrying this marker is still included in the batch
/// (inclusive end). A nameless <c>[BatchEnd]</c> closes ALL currently open batches, including the "" batch.
/// </summary>
[AttributeUsage(AttributeTargets.Field | AttributeTargets.Property, AllowMultiple = true, Inherited = true)]
public sealed class BatchEndAttribute : Attribute
{
    public string Name { get; }

    public BatchEndAttribute(string name = "")
    {
        Name = name ?? string.Empty;
    }
}

/// <summary>
/// Defines a reusable batch named <see cref="Name"/> from the OTHER attributes in the same <c>[ ]</c>, but only
/// applies them to this single field — it does NOT propagate forward. Other fields can pull the same set later with
/// <see cref="BatchApplyAttribute"/>. Useful for a one-off template you want to repeat on non-adjacent fields.
/// </summary>
[AttributeUsage(AttributeTargets.Field | AttributeTargets.Property, AllowMultiple = true, Inherited = true)]
public sealed class BatchDefineAttribute : Attribute
{
    public string Name { get; }

    public BatchDefineAttribute(string name = "")
    {
        Name = name ?? string.Empty;
    }
}

/// <summary>
/// Applies the attribute set of a previously defined/begun batch named <see cref="Name"/> to this field only.
/// Works with names created by either <see cref="BatchBeginAttribute"/> or <see cref="BatchDefineAttribute"/>.
/// </summary>
[AttributeUsage(AttributeTargets.Field | AttributeTargets.Property, AllowMultiple = true, Inherited = true)]
public sealed class BatchApplyAttribute : Attribute
{
    public string Name { get; }

    public BatchApplyAttribute(string name = "")
    {
        Name = name ?? string.Empty;
    }
}
