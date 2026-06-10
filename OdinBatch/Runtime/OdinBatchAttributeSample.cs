// Copyright (c) 2026 Jake Pine
// SPDX-License-Identifier: MIT
// This software is provided "as is", without warranty of any kind. Use at your own risk.

using Sirenix.OdinInspector;
using UnityEngine;

/// <summary>
/// Demonstrates every Batch attribute pattern for OdinSourceAttributeChannelProcessor.
/// Works with Odin attributes, Unity attributes, and custom attributes. Propagated copies clone the compiled
/// attribute from the field that carries [BatchBegin]/[BatchDefine], so constructor args, nameof(), $ interpolation,
/// and Odin "$"/"@" resolvers are all preserved exactly as the compiler built them.
/// Marker rules: every other attribute in the SAME [ ] as the marker is the batch's set; [BatchEnd] is inclusive
/// (the field carrying it still gets the batch); a nameless [BatchEnd] closes ALL open batches; with no end a batch
/// runs to the end of the class body; and if a single [ ] holds two markers, BatchBegin wins (first one wins).
/// </summary>
public class OdinBatchAttributeSample : MonoBehaviour
{
    // ── Named batch ─────────────────────────────────────────────────────────────────────────────
    // BatchBegin opens "stats"; the OTHER attributes in the same [ ] are the batch set. Every field
    // through [BatchEnd("stats")] (inclusive) receives clones.

    [FoldoutGroup("Stats"), GUIColor(0.6f, 1f, 0.7f), ReadOnly, BatchBegin("stats")]
    public float statOne = 10f;
    public float statTwo = 20f;

    [BatchEnd("stats")]
    public float statThree = 30f; // still in the batch — end is inclusive

    public float afterStats; // no batch

    // ── Empty-name batch ────────────────────────────────────────────────────────────────────────
    // [BatchBegin] with no name opens the "" batch — handy when you only need one.

    [BoxGroup("Defaults"), BatchBegin]
    public int defaultsOne;

    public int defaultsTwo;

    [BatchEnd] // nameless end closes ALL open batches, including ""
    public int defaultsThree;

    public int afterDefaults;

    // ── Overlapping (nested) batches ─────────────────────────────────────────────────────────────
    // Independent batch names can overlap and nest freely. Fields covered by both receive both sets.
    // Nothing is replaced here because the two names carry different attribute types — see the Overwrite
    // example below for what happens when nested batches carry the SAME attribute type.

    [FoldoutGroup("Overlap"), BatchBegin("group")]
    public int overlapOne;

    [GUIColor(1f, 0.8f, 0.5f), BatchBegin("color")]
    public int overlapTwo; // group + color

    [BatchEnd("group")]
    public int overlapThree; // group + color (group ends here, inclusive)

    public int overlapFour; // color only

    [BatchEnd("color")]
    public int overlapFive; // color (inclusive)

    public int afterOverlap;

    // ── Close all batches at once ───────────────────────────────────────────────────────────────

    [FoldoutGroup("End All"), BatchBegin("a")]
    public int endAllOne;

    [GUIColor(0.9f, 0.5f, 0.5f), BatchBegin("b")]
    public int endAllTwo;

    [BatchEnd] // closes both "a" and "b"
    public int endAllThree;

    public int afterEndAll;

    // ── Define + Apply (reuse on non-adjacent fields) ───────────────────────────────────────────
    // BatchDefine stores a set WITHOUT propagating: only this field gets it. Other fields opt in with BatchApply.

    [FoldoutGroup("Reused"), ReadOnly, BatchDefine("ro")]
    public string definedField = "defined";

    public string notReused; // gets nothing — define does not propagate

    [BatchApply("ro")]
    public string reusedField = "same attributes as definedField";

    // ── Apply a BatchBegin name from outside its region ─────────────────────────────────────────
    // BatchBegin also defines its name, so BatchApply works even outside the begin/end region.

    [BatchApply("stats")]
    public float appliedStats; // gets the "stats" set defined far above

    // ── Re-open a batch ─────────────────────────────────────────────────────────────────────────
    // A bare [BatchBegin("stats")] with no other attributes re-opens the stored "stats" definition.

    [BatchBegin("stats")]
    public float reopenedOne; // stats again

    public float reopenedTwo;

    [BatchEnd("stats")]
    public float reopenedThree;

    // ── Overwrite — inner batch attributes replace outer ones of the same type ───────────────────
    // When two open batches both carry the same attribute type, the innermost (most recently opened)
    // batch wins and the attribute is applied only once — never twice.

    [GUIColor(0.9f, 0.7f, 1f), Range(0, 100), BatchBegin("outer")]
    public int outerOne = 100;

    [Range(0, 5), BatchBegin("inner")]
    public int innerOne = 5; //overwrites range, keeps color

    [BatchEnd("inner")]
    public int innerTwo = 5;

    [BatchEnd("outer")]
    public int outerTwo = 100;

    public int afterOverwrite; // nothing

    // ── Unity built-in attribute ────────────────────────────────────────────────────────────────

    [Range(0, 10), BatchBegin("range")]
    public int rangeOne;

    [BatchEnd("range")]
    public int rangeTwo;

    public int afterRange;

    // ── Odin "$" resolver — group name evaluated at runtime from a member ────────────────────────
    // Cloning preserves the string exactly, so Odin re-evaluates the resolver on every propagated field.

    private string GetDynamicGroupName() => "Dynamic Group";

    [FoldoutGroup("$" + nameof(GetDynamicGroupName)), BatchBegin("dyn")]
    public int dynamicOne;

    [BatchEnd("dyn")]
    public int dynamicTwo;

    // ── ToggleGroup — ──────────────────────────────

    [ToggleGroup(nameof(toggleGroupEnabled), "My Toggle Group"), BatchBegin("toggle")]
    public bool toggleGroupEnabled;
    public int toggleOne;

    [BatchEnd("toggle")]
    public float toggleTwo;

    public int afterToggleGroup;

    // ── Marker precedence inside one [ ] ────────────────────────────────────────────────────────
    // BatchBegin always wins over BatchDefine; "ignored" is never created.

    [FoldoutGroup("Precedence"), BatchBegin("win"), BatchDefine("ignored")]
    public int precedenceOne;

    [BatchEnd("win")]
    public int precedenceTwo;

    public int afterPrecedence;

    // ── Mixing own attributes with batch attributes ──────────────────────────────────────────────
    // Non-batch attributes placed on any field are independent — they only apply to that specific field.
    // They stack on top of whatever the batch is propagating without affecting other fields in the batch.
    //
    // field "mixOne":  [Range(0,10)] is its own attribute + receives the batch (FoldoutGroup + GUIColor)
    // field "mixTwo":  plain field — receives only the batch
    // field "mixThree": [Tooltip] is its own attribute + receives the batch
    // field "mixFour": [BatchEnd] — receives the batch (inclusive) + [MinValue] is its own attribute
    //
    // Note: non-batch attributes on separate [ ] lines above the field work identically.

    [FoldoutGroup("Mixed"), GUIColor(0.7f, 0.9f, 1f), BatchBegin("mix")]
    [Range(0f, 10f)]            // own attribute — only mixOne gets this Range
    public float mixOne = 5f;

    public float mixTwo = 3f;   // batch only

    [Tooltip("Per-field tooltip, batch still active")]
    public float mixThree = 7f; // own Tooltip + batch

    [MinValue(0f), BatchEnd("mix")]
    public float mixFour = 1f;  // own MinValue + batch (inclusive end)

    public float afterMix;      // nothing

    // ── Group name containing commas/brackets inside the string ──────────────────────────────────
    // The string is one argument; the top-level commas separating attributes must not be confused with
    // the commas inside "Comma, In, Name".

    [FoldoutGroup("Comma, In, [Name]"), GUIColor(1f, 1f, 0.6f), BatchBegin("comma")]
    public int commaOne;

    [BatchEnd("comma")]
    public int commaTwo;

    public int afterComma;

    // ── Parser stress test ───────────────────────────────────────────────────────────────────────
    // A batch propagates onto every member it spans — fields, properties, AND methods — because all three can
    // carry Odin inspector attributes. The parser skips each property/method body (so the braces, semicolons,
    // (), [], {}, and // text inside them are never misread as fields) yet still applies the batch to the member
    // itself. Every member from stressFirst through stressEnd receives the "stress" FoldoutGroup + GUIColor.
    // ShowInInspector is included to demonstrate that the batch can apply attributes that make non-field members visible in the inspector.

    [FoldoutGroup("Stress"), ShowInInspector, GUIColor(0.8f, 0.8f, 1f), BatchBegin("stress")]
    public int stressFirst;                       // defines + receives the batch

    // Auto-property — receives the batch; its single-line { get; set; } body is skipped.
    public int AutoProp { get; set; }

    // Full property with get/set bodies that use the get/set keywords and braces — body skipped, batch applied.
    private int backing;
    public int FullProp
    {
        get { return backing; }
        set { backing = value; }
    }

    // Button method — receives the batch group; its full body (with { } ; // inside a string) is skipped.
    [Button]
    public void DoStressThing()
    {
        Debug.Log("nested { braces } ; and // not-a-comment, all inside a string");
    }

    // Expression-bodied method and property — both receive the batch.
    public int AddStress(int a, int b) => a + b;
    public int ComputedStress => stressFirst * 2;

    // Array, collection, and object/constructor initializers using [] () {} and commas.
    public int[] numbers = new int[] { 1, 2, 3 };
    public System.Collections.Generic.List<string> names = new System.Collections.Generic.List<string> { "a", "b", "c" };
    public Vector3 offset = new Vector3(1f, 2f, 3f);

    // Field whose string default is full of characters that would fool a naive parser.
    public string tricky = "x = 5; get; set; [a,b] (c) {d} // not a comment";

    public int stressLast;                        // still receives the batch

    [BatchEnd("stress")]
    public int stressEnd;                         // inclusive end — still receives the batch

    public int afterStress;                      // nothing

    // ── No end — batch applies to all remaining fields in the class body ─────────────────────────

    [BoxGroup("No End Batch"), BatchBegin("tail")]
    public string tailOne = "stays in box";
    public string tailTwo = "also in box";
}

