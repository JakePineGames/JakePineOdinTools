# OdinBatch

**Propagate Odin and Unity inspector attributes across multiple fields using `Batch` attributes in source.**

_Requires **Odin Inspector** (Sirenix). Unity 2021+ / C# 9+._

---

## How it works

Put one or more attributes in a single `[ ]` together with a `[BatchBegin("name")]` marker. Every field, property, or method that follows automatically receives copies of those attributes through the matching `[BatchEnd("name")]` (inclusive), or to the end of the class body if you omit the end.

Copies are **cloned from the compiled attribute object** on the field that carries the marker — constructor args, `nameof()`, string interpolation, and Odin `"$"`/`"@"` resolver strings are all preserved exactly as the compiler built them. The `Batch*` markers themselves are stripped before Odin sees them.

The source `.cs` file is read at editor time only to discover markers, their grouping inside one `[ ]`, and member order.

---

## Installation

Part of [JakePineOdinTools](../README.md). Requires the sibling **OdinSource** folder (shared source parsing).

```
../OdinSource/Editor/OdinSourceFileHelper.cs   ← required shared utilities
Runtime/BatchAttributes.cs                     ← BatchBegin/End/Define/Apply markers
Editor/OdinBatchAttributeProcessor.cs          ← core attribute processor
Editor/OdinAttributeInstanceFactory.cs         ← attribute cloning
```

You may delete the **OdinAutoTooltip** folder if you do not need it. Do not delete **OdinSource**.

No other setup needed. The processor registers itself via Odin's `OdinAttributeProcessor<object>`.

---

## Markers

| Marker | Meaning |
|---|---|
| `[BatchBegin("name")]` | Open batch `name`; all OTHER attributes in the same `[ ]` are its set. Propagates to this and every following member. Also defines `name` for later `BatchApply`. |
| `[BatchBegin]` | Same, for the empty-name `""` batch. |
| `[BatchBegin("name")]` *(no other attributes)* | Re-opens a previously defined/begun `name`, reusing its stored set. |
| `[BatchEnd("name")]` | Close batch `name`. The member carrying it is **still included** (inclusive end). |
| `[BatchEnd]` | Close **all** open batches, including `""`. |
| `[BatchDefine("name")]` | Define a reusable set from the same `[ ]`, applied only to this member — does **not** propagate. |
| `[BatchApply("name")]` | Apply a previously defined/begun set to this member only. |

Rules:
- Batch names are **case-sensitive**; `""` is a valid name.
- All attributes in the **same `[ ]`** as the marker form the batch set. Use `[A, B, C, BatchBegin("x")]`, not `[A] [B] [BatchBegin("x")]` (separate brackets are separate groups).
- If a single `[ ]` holds two batch markers, **BatchBegin wins** over BatchDefine, and the **first** of the same kind wins.
- When two open batches carry the same attribute type, the **innermost** (most recently opened) batch wins and the attribute is applied only once — never twice.
- With no `BatchEnd`, a batch runs to the **end of the class body** (not into nested or derived types).
- The markers are processing directives only — they never appear in the inspector.
- Batch attributes are propagated onto fields, properties, and methods alike.

---

## Examples

### Named batch

`BatchBegin` opens the batch; all other attributes in the same `[ ]` form its set. Every member through `BatchEnd` (inclusive) receives copies. `afterStats` is outside the batch and receives nothing.

```csharp
[FoldoutGroup("Stats"), GUIColor(0.6f, 1f, 0.7f), ReadOnly, BatchBegin("stats")]
public float statOne = 10f;
public float statTwo = 20f;

[BatchEnd("stats")]
public float statThree = 30f;

public float afterStats;
```
<img width="469" height="110" alt="image" src="https://github.com/user-attachments/assets/ddc6e08e-ce50-4596-ae4f-a2154ad7f246" />


### Empty-name batch

`[BatchBegin]` with no argument opens the `""` batch. A nameless `[BatchEnd]` closes all open batches at once, including `""`.

```csharp
[BoxGroup("Defaults"), BatchBegin]
public int defaultsOne;
public int defaultsTwo;

[BatchEnd]
public int defaultsThree;

public int afterDefaults;
```
<img width="470" height="109" alt="image" src="https://github.com/user-attachments/assets/d25f9931-e842-4a84-8fe2-cf6b4c8afe08" />



### Overlapping batches

Independent batch names can overlap freely. Members covered by both receive both sets. Nothing is replaced here because the two names carry different attribute types.

```csharp
[FoldoutGroup("Overlap"), BatchBegin("group")]
public int overlapOne;

[GUIColor(1f, 0.8f, 0.5f), BatchBegin("color")]
public int overlapTwo;

[BatchEnd("group")]
public int overlapThree;

public int overlapFour;

[BatchEnd("color")]
public int overlapFive;

public int afterOverlap;
```
<img width="466" height="154" alt="image" src="https://github.com/user-attachments/assets/40da88ac-cdc4-469d-b86f-7ebf7175b279" />


### Close all batches at once

A nameless `[BatchEnd]` closes every currently open batch simultaneously.

```csharp
[FoldoutGroup("End All"), BatchBegin("a")]
public int endAllOne;

[GUIColor(0.9f, 0.5f, 0.5f), BatchBegin("b")]
public int endAllTwo;

[BatchEnd]
public int endAllThree;

public int afterEndAll;
```
<img width="466" height="116" alt="image" src="https://github.com/user-attachments/assets/4437fcd0-7c89-423f-9da3-707e577d5ca3" />


### Define + Apply

`BatchDefine` stores a named set that only applies to the defining member — it does not propagate. Other members opt in explicitly with `BatchApply`.

```csharp
[FoldoutGroup("Reused"), ReadOnly, BatchDefine("ro")]
public string definedField = "defined";

public string notReused;

[BatchApply("ro")]
public string reusedField = "same attributes as definedField";
```
<img width="470" height="94" alt="image" src="https://github.com/user-attachments/assets/e78859de-2fda-4b19-9382-96127afb2afa" />


### Apply a BatchBegin name from outside its region

`BatchBegin` also defines its name as a stored set, so `BatchApply` works even on members that are completely outside the begin/end region.

```csharp
[FoldoutGroup("Stats"), GUIColor(0.6f, 1f, 0.7f), ReadOnly, BatchBegin("stats")]
public float statOne = 10f;

[BatchEnd("stats")]
public float statThree = 30f;

[BatchApply("stats")]
public float appliedStats;
```

### Re-open a batch

A `BatchBegin` with no other attributes re-opens a previously defined batch name, reusing its stored attribute set.

```csharp
...
[BatchBegin("stats")]
public float reopenedOne;
public float reopenedTwo;

[BatchEnd("stats")]
public float reopenedThree;
```
<img width="470" height="193" alt="image" src="https://github.com/user-attachments/assets/2c64e14b-decb-4a57-9dc7-4402c60c8b6f" />


### Overwrite — inner batch replaces outer for the same attribute type

When two open batches both carry the same attribute type, the innermost (most recently opened) batch wins and the attribute is applied only once. Attribute types the inner batch does not carry still pass through from the outer batch.

```csharp
[GUIColor(0.9f, 0.7f, 1f), Range(0, 100), BatchBegin("outer")]
public int outerOne = 100;

[Range(0, 5), BatchBegin("inner")]
public int innerOne = 5;

[BatchEnd("inner")]
public int innerTwo = 5;

[BatchEnd("outer")]
public int outerTwo = 100;

public int afterOverwrite;
```
<img width="460" height="106" alt="image" src="https://github.com/user-attachments/assets/524a2435-f0ea-41fb-8707-3ab948c1620e" />


### Odin `$` resolver

Cloning preserves the string exactly, so Odin re-evaluates the resolver on every propagated member.

```csharp
private string GetDynamicGroupName() => "Dynamic Group";

[FoldoutGroup("$" + nameof(GetDynamicGroupName)), BatchBegin("dyn")]
public int dynamicOne;

[BatchEnd("dyn")]
public int dynamicTwo;
```
<img width="466" height="71" alt="image" src="https://github.com/user-attachments/assets/7e07f719-0a99-4307-9703-ce87aee5aab3" />


### ToggleGroup

```csharp
[ToggleGroup(nameof(toggleGroupEnabled), "My Toggle Group"), BatchBegin("toggle")]
public bool toggleGroupEnabled;
public int toggleOne;

[BatchEnd("toggle")]
public float toggleTwo;

public int afterToggleGroup;
```
<img width="464" height="93" alt="image" src="https://github.com/user-attachments/assets/a69d5251-54f5-4f31-8b00-40104ce87103" />


### Mixing per-field attributes with batch attributes

Attributes placed on a member outside the batch brackets are per-member only — they stack with whatever the batch propagates without affecting any other member in the batch.

```csharp
[FoldoutGroup("Mixed"), GUIColor(0.7f, 0.9f, 1f), BatchBegin("mix")]
[Range(0f, 10f)]
public float mixOne = 5f;

public float mixTwo = 3f;

[Tooltip("Per-field tooltip")]
public float mixThree = 7f;

[MinValue(0f), BatchEnd("mix")]
public float mixFour = 1f;

public float afterMix;
```
<img width="475" height="109" alt="image" src="https://github.com/user-attachments/assets/a968ab26-fd1e-4115-81cd-4c17578c1c74" />


### No end — batch runs to end of class body

A batch with no `BatchEnd` propagates to every remaining member in the class body.

```csharp
[BoxGroup("No End Batch"), BatchBegin("tail")]
public string tailOne = "stays in box";
public string tailTwo = "also in box";
```
<img width="468" height="115" alt="image" src="https://github.com/user-attachments/assets/4b676b56-4f09-4355-afba-cf28ac3f546c" />


### Supports Different Member Types

Treats fields, auto-properties, full properties, and methods as one linear member list and propagates attributes to every kind in the exact order they appear in code.

```csharp
public int afterComma;

[FoldoutGroup("Stress"), ShowInInspector, GUIColor(0.8f, 0.8f, 1f), BatchBegin("stress")]
public int stressFirst;

public int AutoProp { get; set; }

private int backing;
public int FullProp
{
    get { return backing; }
    set { backing = value; }
}

[Button]
public void DoStressThing()
{
    Debug.Log("nested { braces } ; and // not-a-comment, all inside a string");
}

public int AddStress(int a, int b) => a + b;

public int ComputedStress => stressFirst * 2;

public int[] numbers = new int[] { 1, 2, 3 };
public System.Collections.Generic.List<string> names = new System.Collections.Generic.List<string> { "a", "b", "c" };

public Vector3 offset = new Vector3(1f, 2f, 3f);

public string tricky = "x = 5; get; set; [a,b] (c) {d} // not a comment";

public int stressLast;

[BatchEnd("stress")]
public int stressEnd;

public int afterStress;
```
<img width="469" height="357" alt="image" src="https://github.com/user-attachments/assets/31b4276e-f91e-49b6-9997-f13938200215" />


### Marker precedence

When `BatchBegin` and `BatchDefine` share the same `[ ]`, `BatchBegin` always wins. The `BatchDefine` is ignored entirely and the name it would have created is never stored.

```csharp
[FoldoutGroup("Precedence"), BatchBegin("win"), BatchDefine("ignored")]
public int precedenceOne;

[BatchEnd("win")]
public int precedenceTwo;

public int afterPrecedence;
```
---

## Notes

- The processor reads the **source `.cs` file** at editor time to find markers and member order. The file must be on disk (standard Unity workflow).
- Attribute **values always come from the compiled field** that carries the marker — source parsing only identifies which attribute types form the batch.
- `PropertyGroupAttribute`-derived attributes (`FoldoutGroup`, `BoxGroup`, etc.) are cloned fresh on every inspector repaint because Odin mutates group instances during resolution.
- Batches are per-type and cleared on every assembly reload.
