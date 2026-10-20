# Contributing

Use the existing C# UI, local Node service and optional Python analysis boundaries. Keep changes focused and preserve the minimal player layout.

Before changing persistence or installation, work in an isolated `CLEARTONE_DATA` directory and back up any existing data. Never test against a user's live account store by default.

For code changes, run `dotnet run --project tests/CoreTests -c Release` and `node --test tests/service.test.cjs`. Build and launch WinUI changes, checking a real responsive window as well as compiler success. State what was and was not tested.

For dependency updates, regenerate lockfiles and `node scripts/license-inventory.cjs`, review advisories and preserve upstream licenses. Public contributions must not include accounts, listening history, media, album artwork, personal screenshots or local runtime/model directories.

The build and validation scripts are the maintained workflow for this project. Do not introduce scheduled jobs without an explicit product need. If a change fails verification, retain the previous working build and fix the regression before release.
