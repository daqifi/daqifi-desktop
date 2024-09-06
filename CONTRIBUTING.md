# Contributor Guide

Welcome to DAQiFi Desktop! We are excited to have you contribute. This guide provides the steps and guidelines to help you start contributing to our repository.

## Getting Started

To get started with contributing, follow these steps:

1. Clone the repository
2. Create a new branch for your contribution. The `main` branch is protected and can not be pushed directly to.

## Development

- Ensure your code follows .NET style and formatting rules
- Write tests for your changes whenever applicable.
- Document new features or changes in the codebase.

## Making a PR

We use [conventional commits](https://www.conventionalcommits.org/en/v1.0.0/) (at the PR Title level) to ensure easy-to-read release notes. When merging into `main` we also squash commits into a single commit to keep the history clean.

### Conventional Commits

Use one of the following prefixes in your PR Title:

- `feat:` New feature
- `fix:` Fixing a bug
- `docs:` Updating documentation
- `deps:` Updating dependencies
- `chore:` Miscellaneous items (code refactor)

We strive to limit breaking changes. If you must make a breaking change, indicate it with an exclamation mark `!`. For example, `feat!:`.

### Code Review

A DAQiFi Core Member must review and approve all code before it can be merged into `main`.

## Releasing

The DAQiFi Core team is responsible for creating a new release of DAQiFi Desktop. They will create a new version using [semantic versioning](https://semver.org/). Good conventional commits help determine the new version number.
