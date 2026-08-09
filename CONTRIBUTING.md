# Contributing

Use Windows with the .NET 10 SDK. Fork, create a focused branch, then run:

```powershell
dotnet restore .\FrameHub.slnx
dotnet build .\FrameHub.slnx
dotnet test .\FrameHub.slnx
```

Keep bug-fix PRs focused; avoid unrelated refactors. Add or update tests for behavior changes, keep EN/PL localization keys in sync, and preserve the app’s explicit, reversible safety model. Describe user-visible behavior and validation in the PR.
