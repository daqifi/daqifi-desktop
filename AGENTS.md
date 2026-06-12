# AGENTS.md

Full project context — architecture, build/test commands, and coding standards — lives in [CLAUDE.md](CLAUDE.md).

## C# / MVVM conventions
- Prefer CommunityToolkit.Mvvm `[ObservableProperty]` over manual backing fields and boilerplate setters when a property only needs standard change notification.
- Use `[NotifyPropertyChangedFor(...)]` for dependent UI properties.
- Prefer explicit property implementations only when validation, coercion, or other nontrivial side effects are required.
