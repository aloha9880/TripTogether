# TripTogether

A .NET MAUI iOS/Android app for local trip planning and foreground nearby sharing.

## Current implementation

- Create named trips with a default IANA time zone; create/edit/delete timed or all-day events with notes and location.
- Local SQLite persistence; device identifier in platform secure storage.
- Manual nearby sessions on shared Wi-Fi/hotspots using TCP + TLS, certificate fingerprint pinning, a 256-bit random invitation token, and ten-minute expiration.
- Explicit guest acceptance, selected-trip sharing, immutable revision DAG, deduplication, deletion tombstones, and visible concurrent edit/delete conflicts.
- Bounded full-history snapshots (4 MiB / 10,000 changes), atomic validated merge, cancellation and connection timeout.
- Pure .NET check executable and GitHub Actions core/Android build workflow.

This is an initial source implementation, not a verified mobile release. No .NET SDK or mobile toolchains were available in the authoring environment. Compilation, automated checks, device interoperability, mobile TLS compatibility, and UI operation remain unverified. CI must run after upload.

## Visual Studio setup

1. Install Visual Studio with .NET 10 support and the .NET MAUI/mobile development workload. Install the current compatible Android SDK/JDK through Visual Studio.
2. Open `TripTogether.sln`. Restore NuGet packages. Set `TripTogether.App` as startup project.
3. Select `net10.0-android` and an emulator or attached Android device. For real sharing, use two phones on the same reachable network.
4. For iOS, pair Visual Studio to a Mac with the Xcode version required by the installed .NET iOS workload. Configure Apple development signing/provisioning. Select an iOS simulator or physical device. Physical-device signing credentials are not included.
5. Application ID `com.aloha9880.triptogether` is provisional: verify ownership/availability before store publication.

Command-line checks (no mobile workload required for core):

```powershell
dotnet run --project tests/TripTogether.Core.Checks -c Release
dotnet run --project tests/TripTogether.Core.Checks -c Release -- --network
dotnet build src/TripTogether.App -f net10.0-android -c Debug
```

The optional network check requires a non-loopback IPv4 address and permission to open a local listening socket. iOS target builds require the Apple toolchain. Building the whole solution on Windows without a Mac may fail on iOS; build the selected Android framework explicitly.

## Nearby sharing

1. Both phones install the app and join the same Wi-Fi or hotspot. Avoid guest Wi-Fi/client isolation.
2. On the host, select a trip and find the phone's current Wi-Fi IPv4 address in its OS settings. Enter it and tap **Host one sharing session**.
3. Copy the invitation and transfer it privately to the other person. The invitation includes a temporary secret; anyone holding it can join that trip during its validity. Clipboard transfer is convenient but is not a private channel by itself.
4. The other person pastes it and taps **Preview and join invitation**, verifies the trip/host, and accepts.
5. Both apps remain open. The guest sends their existing changes for this trip, the host merges them, and sends the combined log back. Both persist independently. An interrupted session can leave one phone ahead: start another invitation/session to converge.
6. Resolve any displayed conflicts by choosing a version, then share again.

No internet is required by the app for the local transport. There is no Bluetooth, discovery, QR scanner, persistent pairing, automatic reconnect, or remote transport yet. Each invitation permits one incoming connection; an invalid connection consumes it. Create another invitation after failure. Host editing is blocked during the session. iOS local-network permission must be granted; network behavior needs real-device validation.

## Privacy and limits

SQLite stores calendar content unencrypted in the application sandbox. Do not claim database encryption. Android backup is disabled; iOS backup policy is not customized yet. No server, analytics, external calendar provider, or cloud account is used by the current app. The random device ID is an operation author marker, not a cryptographically verified long-term identity.

The host authenticates the guest using the temporary token; the guest pins the certificate hash from the privately received invitation. Pin validation is scoped to that connection. Certificates/keys are ephemeral. No permanent trust or owner/member authorization is implemented. Shared data cannot be reliably erased from another device.

Full history is retained indefinitely to prevent resurrection; there is no tombstone compaction. Trip metadata is immutable after creation. The app's timed editor requires explicit offsets; future UI should convert local times using the stored time zone and validate ambiguous/invalid DST times. No recurrence or reminders yet.

## Planned capabilities

See `docs/IMPLEMENTATION_PLAN.md` for persistent identity/membership, incremental sync, native calendar integration, ICS, QR/discovery, remote transport, and release milestones. These are not hidden success-path stubs or advertised as implemented.

## Upload to your empty GitHub repository

Unzip into a local folder. From that folder:

```powershell
git init -b main
git add .
git commit -m "Create TripTogether MAUI solution and nearby sharing foundation"
git remote add origin https://github.com/aloha9880/TripTogether.git
git push -u origin main
```

If you already cloned the empty repository, copy the archive contents into that clone and use its existing remote. Do not create a nested `TripTogether/TripTogether` folder. No license has been selected; dependencies retain their own licenses.
