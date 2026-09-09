# 35 — Generic widget port

**Phase** 5 · **Depends on** 33 · **Blocks** 36–42

## Goal
Replace PoB's generic widget layer with stock Avalonia controls. **This ticket is mostly deletion — ~4,500 LOC of reusable widgets collapses to a fraction.**

## Scope — direct mappings
| PoB (`src/Classes/`) | LOC | Avalonia |
|---|---:|---|
| `ScrollBarControl` | 327 | `ScrollViewer` — **delete the class entirely** |
| `EditControl` | 761 | `TextBox` — undo, selection, caret, filters all free. **Delete ~700 of 761.** |
| `ListControl` + ~20 subclasses | ~4,000 | `ListBox`/`ItemsControl` + `ItemTemplate` per subclass. **Biggest single win.** |
| `DropDownControl` | 573 | `ComboBox` + `ItemTemplate`; type-ahead built in |
| `ButtonControl` | 130 | `Button`; `+`/`-`/`x` glyphs → `PathIcon` |
| `CheckBoxControl` | 117 | `CheckBox` |
| `SliderControl` | 185 | `Slider` |
| `LabelControl` | 24 | `TextBlock` |
| `SectionControl` | 32 | `HeaderedContentControl` / `Border` |
| `RectangleOutlineControl` | 23 | `Border` |
| `TextListControl` | 122 | `ItemsControl` |
| `PopupDialog` | 96 | `Window.ShowDialog` or a `DialogHost` overlay |
| `PathControl` / `FolderListControl` | 209 | `IStorageProvider` pickers |
| `SearchHost` | 153 | Built into `ComboBox`/`AutoCompleteBox` |
| `DraggerControl` | 146 | `GridSplitter` |
| `ResizableEditControl` | 50 | `TextBox` in a `GridSplitter` cell |
| `lua-utf8` caret logic (`EditControl.lua:555-572`) | — | .NET strings are UTF-16 natively; `System.Globalization.StringInfo` + `Rune`. **Strictly better than the original.** |

## Drag and drop
The hand-rolled `dragTargetList`/`CanReceiveDrag`/`ReceiveDrag` protocol (`ListControl.lua:8-16`, drag initiated by a 10px-squared distance threshold at `:159-161`, floating cursor label via `main.showDragText`) maps to Avalonia's native `DragDrop` + `DataObject` with format strings. `ItemsTab.lua:1188-1202` hand-wires 20+ targets across three tabs — that mesh becomes declarative.

## Gotchas
- `PopupDialog` (`:15-33`) **retroactively re-anchors** every control handed to it: unanchored controls get `TOP`-anchored to the dialog, and controls anchored to a *string name* get resolved against the sibling table. Callers pass loose control tables with string references. None of this survives; rewrite the call sites.
- Modal semantics: when any popup exists, `Main.lua:402-407` does `wipeTable(self.inputEvents)` — the app behind it is **fully inert**. `ShowDialog` gives you this.

## Acceptance
No PoB widget class remains that has a stock Avalonia equivalent.

## Libraries
Avalonia 11, Avalonia.Xaml.Behaviors.
