# Repository Guidelines

## Project Structure & Module Organization

This repository contains a single WPF desktop application solution:

- `BPMifir.sln` is the Visual Studio solution file.
- `BPMifir/BPMifir.csproj` targets `net8.0-windows7.0` with WPF enabled.
- `BPMifir/App.xaml` and `BPMifir/App.xaml.cs` define application startup.
- `BPMifir/MainWindow.xaml` and `BPMifir/MainWindow.xaml.cs` contain the main UI and XML conversion workflow.
- `BPMifir/Models/` contains XML serialization models for DB input, ESMA header, and report output.
- `BPMifir/logo-u8293-r.png` is included as a WPF resource.

Keep generated output files, local XML samples, and Visual Studio state out of source control unless they are intentional fixtures.

## Build, Test, and Development Commands

Run commands from the repository root:

```powershell
dotnet restore BPMifir.sln
dotnet build BPMifir.sln
dotnet run --project BPMifir/BPMifir.csproj
```

- `dotnet restore` downloads NuGet dependencies.
- `dotnet build` compiles the WPF application.
- `dotnet run` launches the desktop app for local manual testing.

Use Visual Studio when editing XAML visually or debugging UI event handlers.

## Coding Style & Naming Conventions

Use C# conventions already present in the project: PascalCase for types, methods, properties, and XAML control names; camelCase for local variables. Keep XML model classes aligned with serializer-generated naming unless refactoring the full serialization contract. Prefer four-space indentation in C# and consistent XAML attribute formatting.

Keep UI logic in `MainWindow.xaml.cs` focused on orchestration. Move reusable XML mapping or validation code into dedicated classes under `BPMifir/Models/` or a new clearly named folder.

## Testing Guidelines

There is currently no automated test project. For behavior changes, at minimum run `dotnet build BPMifir.sln` and manually verify a representative XML import/export through the WPF UI. If adding tests, create a separate test project such as `BPMifir.Tests/` using xUnit or NUnit, and name tests after the behavior being verified, for example `ConvertsBuyerLeiToEsmaPartyId`.

## Commit & Pull Request Guidelines

The existing history uses very short messages, but new commits should be clearer and imperative, for example `Add ESMA XML validation` or `Fix transaction price mapping`.

Pull requests should include a brief summary, build/test evidence, and any manual XML files or scenarios used for validation. Include screenshots only for visible UI changes. Link related issues when available and call out changes to XML serialization behavior explicitly.

## Agent-Specific Instructions

Do not leave stray PowerShell or `pwsh` windows open after running tasks. Prefer non-interactive commands and clean up any temporary launcher shells before finishing.
