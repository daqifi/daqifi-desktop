# Design Philosophy

This is the north star for DAQiFi Desktop's visual and interaction design. It is not a style guide — there are no hex codes, widths, or control specs here. It is the set of principles that decisions should be measured against. When something feels off, come back to this document.

The application is an instrument. People use it to watch signals from hardware, not to admire an interface. Everything that follows flows from that.

## Principles

### 1. The data is the interface

The most important pixels on screen are the ones showing live values, plots, and device state. Chrome — borders, backgrounds, labels, affordances — exists to frame and organize the data, never to compete with it. If a decorative element draws the eye before the readout does, it is wrong.

### 2. Dark by default

Operators look at this application for hours, often alongside oscilloscopes, logic analyzers, and benches under fluorescent light. Dark surfaces reduce glare, let color-coded signals pop, and cue the user that this is a tool for focused work. Dark is the reference — design against it first.

### 3. Color carries meaning

Color is a channel of information, not decoration. Reserve it for things that should be read as color:

- **Type** — analog vs. digital vs. bidirectional has a consistent color across the app.
- **State** — active, warning, error, disabled are color-coded and never overloaded.
- **Identity** — the user-chosen plot color for a channel is that channel's color everywhere it appears: tiles, legends, plots, readouts.

Grey and off-white carry everything else. Avoid purely aesthetic color.

### 4. Direct manipulation over modals

A user should be able to act on a thing by touching the thing. A tile toggles on click. A channel's settings open from the channel's own tile. Dialogs that float over the app, demand attention, and then vanish are a last resort — reserved for destructive confirmations and genuine forks in the road. Inline drawers and panels beat modals because they preserve context.

### 5. Progressive disclosure

Show the common case. Hide the rest behind a deliberate gesture. A channel tile shows its name, type, value, and on/off state; scaling expressions, color pickers, and direction toggles live one tap deeper, behind a gear. The surface stays calm; the depth is there when asked for.

### 6. Typography reads like instrumentation

- **Numeric readouts** use a monospaced face so digits don't jitter as values change and decimals line up across rows.
- **Labels and controls** use a restrained sans-serif at a small, even weight.
- **Hierarchy** comes from size and opacity, not from bold/italic/decorative treatments.

The typographic vocabulary is small on purpose. A handful of sizes, two or three weights.

### 7. Ambient status, not banners

State is shown where the state lives. An active channel carries its state on its own tile; a connected device on its own card; a running session on its own control. Global banners, toast stacks, and blocking overlays fragment attention — use them only for conditions that genuinely interrupt the user's task.

### 8. Restraint is a feature

Empty space is a design element. Not every region needs a border. Not every control needs a label above and a hint below. When in doubt, remove something before adding something. A sparse surface reads as confident and reliable; a dense one reads as busy and fragile.

### 9. One visual system

The app will always contain work-in-progress areas that haven't been brought into the current language — legacy flyouts, older dialogs, panels in an earlier style. Treat those as transition states, not as a second supported style. New work adopts the current system; redesigns replace rather than skin. A mixed interface is a temporary reality, never a goal.

### 10. Motion serves comprehension

Animation exists to show relationships: this drawer came from that tile, this panel is closing, this value just changed. Motion should be quick, eased, and functional. Restrained delight is fine — a satisfying transition reinforces trust. Decorative or attention-seeking animation does not; cut it.

## Reference

The Channels pane is the current exemplar. When designing a new surface or redesigning an existing one, study it first: how tiles carry state, how the settings drawer opens inline, how color is used (and withheld), how dense information reads as calm. Match that feel before matching any specific control.
