# Contributing

Thanks for helping improve CCTV Backup.

## Before submitting a change

1. Create a focused branch from `main`.
2. Keep vendor SDK, decoder binaries, credentials and generated `bin`, `obj`
   and `dist` files out of commits.
3. Build the solution in Release mode:

   ```powershell
   dotnet build CctvBackup.sln --configuration Release
   ```

4. Run the tests:

   ```powershell
   dotnet test tests\CctvBackup.Tests\CctvBackup.Tests.csproj --configuration Release
   ```

5. Update the README or changelog when user-visible behavior changes.

## Pull requests

Describe the user impact, testing performed and any Windows or vendor-SDK
requirements. Keep pull requests small enough to review and do not include
real DVR credentials, serial IDs, IP addresses or private recordings.

## Issues

Include Windows version, application version, connection type, reproduction
steps and sanitized logs. Do not attach configuration files or recordings
unless all private information has been removed.
