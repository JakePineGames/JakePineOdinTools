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
<img width="469" height="110" alt="image" src="https://github.com/user-attachments/assets/4d75dbee-10ff-454a-8ec8-3a92134ac143" />


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
<img width="470" height="109" alt="image" src="https://github.com/user-attachments/assets/1f325bfd-b2cf-4fa3-bf6f-e79d4fbedcd7" />


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
<img width="466" height="154" alt="image" src="https://github.com/user-attachments/assets/4a7aee19-ff29-4abb-9584-9b3eb77a2349" />


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
<img width="466" height="116" alt="image" src="https://github.com/user-attachments/assets/6044b5a9-88ad-4b4b-b157-44d240a1194c" />


### Define + Apply

`BatchDefine` stores a named set that only applies to the defining member — it does not propagate. Other members opt in explicitly with `BatchApply`.

```csharp
[FoldoutGroup("Reused"), ReadOnly, BatchDefine("ro")]
public string definedField = "defined";

public string notReused;

[BatchApply("ro")]
public string reusedField = "same attributes as definedField";
```
<img width="470" height="94" alt="image" src="https://github.com/user-attachments/assets/d7c0f297-ecf4-4abe-a04b-18b71031730a" />


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
<img width="470" height="193" alt="image" src="https://github.com/user-attachments/assets/5608a05c-a580-4710-b8a6-56f838337391" />


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
<img width="460" height="106" alt="image" src="https://github.com/user-attachments/assets/a71dc5e6-b1d7-46fd-a310-f2d0ae9532ce" />


### Odin `$` resolver

Cloning preserves the string exactly, so Odin re-evaluates the resolver on every propagated member.

```csharp
private string GetDynamicGroupName() => "Dynamic Group";

[FoldoutGroup("$" + nameof(GetDynamicGroupName)), BatchBegin("dyn")]
public int dynamicOne;

[BatchEnd("dyn")]
public int dynamicTwo;
```
<img width="466" height="71" alt="image" src="https://github.com/user-attachments/assets/bb3c343c-20ac-491e-92f7-94eb2bf1cccc" />


### ToggleGroup

```csharp
[ToggleGroup(nameof(toggleGroupEnabled), "My Toggle Group"), BatchBegin("toggle")]
public bool toggleGroupEnabled;
public int toggleOne;

[BatchEnd("toggle")]
public float toggleTwo;

public int afterToggleGroup;
```
<img width="464" height="93" alt="image" src="https://github.com/user-attachments/assets/15452a35-fd7b-4dd0-b9d0-bc82a9794989" />


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
<img width="475" height="109" alt="image" src="https://github.com/user-attachments/assets/cf737544-a841-45f7-8fe8-52cd92c81062" />


### No end — batch runs to end of class body

A batch with no `BatchEnd` propagates to every remaining member in the class body.

```csharp
[BoxGroup("No End Batch"), BatchBegin("tail")]
public string tailOne = "stays in box";
public string tailTwo = "also in box";
```
<img width="468" height="115" alt="image" src="https://github.com/user-attachments/assets/8cafbc3d-c3bb-4c6c-b7d6-7721c95c51b3" />


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
<img width="469" height="357" alt="image" src="https://github.com/user-attachments/assets/7a1800f1-7e59-4e2f-9080-9214b76af217" />


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
