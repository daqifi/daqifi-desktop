# Style Guide

Concrete control specs for DAQiFi Desktop. This is the companion to
[design-philosophy.md](design-philosophy.md): that document says *why* (principles, no numbers), this one
says *what* (hex codes, sizes, style keys). When the two disagree, the philosophy wins and this document
is the bug.

Scope: what a control should look like and which style key to reach for. It does not dictate layout —
where things sit on a surface is a design decision per surface, with the Channels pane as the exemplar.

## Where the styles live

| File | Scope | Holds |
| --- | --- | --- |
| `Resources/DesignTokens.xaml` | app (via `App.xaml`) | Color tokens only — surfaces, borders, text, status. |
| `Resources/Controls.xaml` | app (via `App.xaml`) | **The canonical control specs.** Keyed styles: button family, field, dropdown. |
| `Resources/DarkDialog.xaml` | window (merged per dialog) | Implicit skins that re-dress a dialog's default controls (`Label`, `TextBox`, `RadioButton`, `GroupBox`). Merges `Controls.xaml` so those skins inherit from it. |

Two rules keep this from re-fragmenting:

- **Shared control specs go in `Controls.xaml`, keyed.** Never redeclare `PillButton` (or any other key
  here) in a view. That is exactly how the app ended up with five copies of the pill family that had
  drifted in padding, hover color, and disabled state ([#627](https://github.com/daqifi/daqifi-desktop/issues/627)).
- **Implicit (unkeyed) styles never go in `Controls.xaml`.** An implicit style at app scope restyles every
  control in the app. Window-scoped implicit skins belong in `DarkDialog.xaml`.

A view-scoped style is still correct for something genuinely local — a connection-type stripe color, the
Debug console's `DataGrid`. The test: *would a second view want this?* If yes, it belongs in `Controls.xaml`.

Tokens inside `Controls.xaml` and `DarkDialog.xaml` are referenced with `DynamicResource`, because
`DesignTokens.xaml` is a sibling merged dictionary and its keys are not in their static lookup scope.
`BasedOn` is the exception — it cannot take a `DynamicResource`, which is why `DarkDialog.xaml` merges
`Controls.xaml` rather than relying on app scope.

## Window chrome

Every dialog uses one convention:

```xml
BorderThickness="0"
Background="{StaticResource Surface}"
GlowBrush="{DynamicResource AccentColorBrush}"
```

No 1px outline — the accent glow is the window edge. This requires a MahApps `MetroWindow`.

Content sits in a root container with **`Margin="16"`**. Dialogs that autosize to a message
(Error, Success, DuplicateDevice) use `Margin="20"`, which suits their icon-plus-text layout.

**The one exception** is `MigrationStatusWindow`: a chromeless startup splash (`WindowStyle="None"`,
`AllowsTransparency="True"`) shown before the main window exists. A plain `Window` has no `GlowBrush`, and
giving a splash a title bar just to conform would be worse. It keeps a 1px `BorderDim` border, which is
what separates it from the desktop behind it. Do not copy this for anything the user interacts with.

## Buttons

One family. Three intents, two sizes, all in `Controls.xaml`:

| Key | Use for |
| --- | --- |
| `PillButton` | Neutral action. **The default** — use it unless the action is *the* primary one. |
| `AccentPillButton` | The primary action. At most one per surface. |
| `DangerPillButton` | Destructive action (delete, disconnect). |
| `DialogPillButton` | Neutral action in a dialog's action row. |
| `DialogAccentPillButton` | Primary action in a dialog's action row. |

There is deliberately no `DialogDangerPillButton`: no dialog currently has a destructive action —
destructive confirms are in-pane overlays, which use the compact `DangerPillButton`. Add it to
`Controls.xaml` if a dialog ever needs one.

Shared geometry: `CornerRadius="14"`, 1px border, `SemiBold`, `Cursor="Hand"`.

| | Compact (panes, drawers, toolbars) | Dialog action row |
| --- | --- | --- |
| Font size | 10 | 11 |
| Padding (neutral / danger) | `12,4` | `24,8` |
| Padding (accent) | `16,6` | `24,8` |
| MinWidth | — | 96 (so OK and Cancel match) |

Colors:

| | Rest | Hover |
| --- | --- | --- |
| Neutral | transparent bg, `BorderDim`, `TextSecondary` | `#141821` bg, `BorderBright`, `TextPrimary` |
| Accent | `Accent` bg + border, white text | `#1EB3F0` bg + border |
| Danger | transparent bg, `#4A1F28` border, `StatusRed` text | `#2A0F14` bg, `StatusRed` border |

Disabled is `Opacity="0.35"` on all of them.

Danger is outline-only at rest on purpose: the red is a warning, not an invitation, so it never competes
with the accent for the eye.

### Rules

- **Never set `Width`/`Height` on a button.** Padding sizes it; `MinWidth` handles dialog action rows. A
  fixed height is how the Firmware dialog ended up clipping descenders.
- **Never put `Margin` in a button style.** Spacing between controls is the call site's business — a style
  that carries margin cannot be right-aligned or centered without fighting it (views were already setting
  `Margin="0"` to undo it).
- **Action row order is Cancel → primary**, left to right, so the accent lands nearest the bottom-right
  corner in every dialog.
- **Field-adjacent buttons** (a Browse next to a path field) use `PillButton` with `Margin="8,0,0,0"` and
  **no explicit height** — in a `Grid` they stretch to the field's height, so the two can't drift apart.

## Fields

`DarkTextBox` in `Controls.xaml` is the canonical input. Dialogs that merge `DarkDialog.xaml` get it
implicitly on every `TextBox`; elsewhere apply it by key.

- Height: **`MinHeight="32"`** — the standard field height. (`MinHeight`, not `Height`, so a field can grow
  with wrapped content.) 30px clips descenders; don't go below 32.
- Padding `10,0`, `CornerRadius="3"`, 1px `BorderDim` border, `Surface` background.
- Border on hover `BorderBright`, on keyboard focus `Accent`. Disabled `Opacity="0.5"`.

`DarkComboBox` matches the field spec (`MinHeight="32"`, padding `10,0`, same border states) and pairs with
`DarkComboBoxItem` via `ItemContainerStyle` — it is opt-in by key, so it never silently overrides a
`ComboBox` in a dialog that merges `DarkDialog.xaml`. It is a full replacement template, not a re-skin,
because the app runs on the MahApps `Light.Blue` theme whose combo brings a white toggle and popup that
swallow light text.

Drawer fields (`DrawerTextBox` in the panes, `DarkTextFieldInput` in the Connection dialog) are a
deliberate variant: a borderless, transparent TextBox that sits inside an already-bordered container, so
the container draws the field and the TextBox only carries the text. The composite adds up to the same
thing `DarkTextBox` draws on its own. They are view-scoped, and they must **not** reuse the `DarkTextBox`
key — one key, one control.

## Spacing

| Situation | Value |
| --- | --- |
| Dialog content margin | `16` (autosizing alert dialogs: `20`) |
| Between adjacent buttons | `8` |
| Between a field and its adjacent button | `8` |
| Action row top margin | `16` |

## Typography

Faces: **Segoe UI** (inherited default) for everything, **Consolas** for numeric readouts and identifiers —
values, serial numbers, timestamps. Monospace is a signal that the text is data, not chrome; don't use it
for labels or prose.

The intended scale — per design-philosophy §6, the vocabulary is small on purpose:

| Size | Weight | Use |
| --- | --- | --- |
| 9–10 | `Bold` / `SemiBold`, uppercase | Section labels, pill buttons, column headers, status chips |
| 11 | `SemiBold` | Dialog action buttons, secondary labels |
| 12–13 | normal | Body text, list rows, readouts |
| 14 | normal | Dialog message text |
| 16+ | normal / `Light` | Headline numbers and empty-state titles only |

Hierarchy comes from **size and opacity** (`TextPrimary` → `TextSecondary` → `TextTertiary`), not from
bold/italic. There are no typography tokens in `DesignTokens.xaml` — it holds brushes only — so these are
set per control.

> **Honest status:** the codebase does not fully conform to this scale yet. A sweep at the time of writing
> found 13 distinct font sizes and 4 weights in `View/`. The table above is the target for new work and the
> direction to converge on when touching an existing surface — not a description of what's there today.
> Narrowing the existing usage is worth its own ticket.

## Color

All from `DesignTokens.xaml`; never hardcode a hex outside a style in `Controls.xaml`.

| Token | Value | Use |
| --- | --- | --- |
| `Surface` | `#0D0F12` | Window and pane background |
| `SurfaceHover` | `#13161C` | Row hover |
| `SurfaceRaised` | `#171A20` | Cards, drawers, panels, headers |
| `SurfaceActive` | `#1E2530` | Selected/active surface |
| `BorderDim` | `#2A2F38` | Default border, dividers |
| `BorderBright` | `#3A4252` | Hover border |
| `TextPrimary` | `#F5F5F7` | Values, primary labels |
| `TextSecondary` | `#8E9199` | Secondary labels |
| `TextTertiary` | `#5A5E66` | Column headers, hints |
| `Accent` | `#119EDA` | Primary action, focus, selection |
| `StatusGreen` | `#4ADE80` | Success, connected |
| `StatusAmber` | `#F59E0B` | Warning, locked |
| `StatusRed` | `#F43F5E` | Error, destructive |
| `Scrim` | `#CC000000` | Modal overlay |

Per design-philosophy §3, color is a channel of information. A channel's user-chosen plot color is that
channel's identity everywhere it appears, and it outranks these tokens on its own tile.

## Adding a new surface

1. Read [design-philosophy.md](design-philosophy.md), then study the Channels pane.
2. Use `Surface` as the background; use `SurfaceRaised` for cards and drawers.
3. Reach for `Controls.xaml` keys before writing a style. If you need a shared control that isn't there,
   add it there — not to your view.
4. Prefer an inline drawer to a dialog (§4). If it must be a dialog, follow the chrome convention above.
