# JakePine Odin Tools

Editor plugins for [Odin Inspector](https://odininspector.com/) (Sirenix) that read C# source at edit time. Unity 2021+ / C# 9+.

---

## Folder structure

Copy the entire `JakePineOdinTools` folder into your project (for example `Assets/Plugins/JakePineOdinTools`):

```
JakePineOdinTools/
├── LICENSE.txt
├── README.md                    ← you are here
├── OdinSource/                  ← required — shared source parsing + cache
│   ├── README.md
│   └── Editor/
│       └── OdinSourceFileHelper.cs
├── OdinBatch/                   ← optional — batch attribute propagation
│   ├── README.md
│   ├── Runtime/
│   └── Editor/
└── OdinAutoTooltip/             ← optional — XML summary → tooltips
    ├── README.md
    └── Editor/
```

### What to keep

| Folder | Required? |
|---|---|
| **OdinSource** | **Yes** — if you use any plugin below |
| **OdinBatch** | No — delete if you do not need batch attributes |
| **OdinAutoTooltip** | No — delete if you do not need auto-tooltips |

If you remove a plugin folder, leave **OdinSource** in place. Both plugins share one source-line cache through `OdinSourceFileHelper`.

---

## Plugins

- **[OdinBatch](OdinBatch/README.md)** — propagate Odin/Unity inspector attributes across members using `BatchBegin` / `BatchEnd` markers in source.
- **[OdinAutoTooltip](OdinAutoTooltip/README.md)** — apply `TooltipAttribute` from XML `/// <summary>` doc comments.

See each plugin’s README for installation details, examples, and options.

---

## Requirements

- Unity Editor
- Odin Inspector (for **OdinBatch** and **OdinAutoTooltip**; **OdinSource** has no Odin dependency)
