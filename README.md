# GameCube App Store

Two .NET 8 applications in one solution:

* **GameCubeAppStore.Server** – ASP.NET Core Minimal API, ASP.NET Core Identity (bearer tokens),
  EF Core / SQLite and private disk-backed upload storage.
* **GameCubeAppStore.Console** – DolphinDotNet GameCube executable with an
  anonymous HTTP catalog client and a reflection-free JSON parser. The graphical storefront follows the approved 960×720 teal/orange mockup.

DolphinDotNet is pinned as a Git submodule under `external/DolphinDotNet`.
This matches its current SDK imports and avoids pretending it is a NuGet package.

## Clone and build

```sh
git clone --recurse-submodules https://github.com/capz/GameCube-app-store.git
cd GameCube-app-store
dotnet restore GameCubeAppStore.sln
# Build managed C# only (CI; no devkitPPC required):
dotnet build GameCubeAppStore.sln -c Release -p:DndBuildNative=false
# Run API:
dotnet run --project src/GameCubeAppStore.Server
# Build native DOL with DolphinDotNet native toolchain / Docker installed:
dotnet build src/GameCubeAppStore.Console -c Release
```

API local development data defaults to `src/GameCubeAppStore.Server/data` (ignored by Git).
Override using `Storage__Root`. At startup the backend logs the absolute path
of its SQLite database (`catalog.db`). If a previously registered account
cannot sign in, compare that path with the one from the original run; separate
working directories or IDE launch configurations can create separate databases.
Do not delete or overwrite an existing database when troubleshooting.

An HTTP 401 from `/account/login` means the credentials were rejected; an
HTTP 401 from `/api/developer/software` after login means the API rejected the
issued bearer token. Both cases are distinguished in the developer portal.
SQLite is initialized with `EnsureCreated`
for this initial prototype; use proper EF migrations before evolving a deployed DB.

## Accounts and catalog workflow

ASP.NET Core Identity provides `POST /account/register`,
`POST /account/login` and token refresh/logout functionality.
Register/login with Identity's built-in JSON contracts; use the returned
bearer access token in `Authorization: Bearer <token>` for developer writes.
Developer write APIs are bearer-only. This avoids CSRF exposure through
cookie-authenticated upload and management endpoints.

| Endpoint | Method | Purpose |
|---|---|---|
| `/api/catalog` | GET | Published software summaries, versions, cover and rating |
| `/api/catalog/{id}` | GET | Release metadata, screenshots and ratings |
| `/api/assets/{id}` | GET | Published files; own draft assets require bearer token |
| `/api/catalog/{id}/rating` | PUT | Authenticated 1–5 star rating |
| `/api/developer/software` | GET / POST | List own titles / create draft |
| `/api/developer/software/{id}` | GET / PUT | Read/edit own title |
| `/api/developer/software/{id}/publish` | POST | Publish if it has a release |
| `/api/developer/software/{id}/unpublish` | POST | Hide title |
| `/api/developer/software/{id}/releases` | POST | Upload versioned .dol/.zip, form fields `version`, `releaseNotes`, `file` |
| `/api/developer/software/{id}/screenshots` | POST | Upload PNG, form field `file` |
| `/api/developer/software/{id}/releases/{releaseId}` | DELETE | Remove a version |
| `/api/developer/software/{id}/screenshots/{shotId}` | DELETE | Remove screenshot |

The API enforces ownership, drafts/published visibility, unique release
versions, one rating per account per title, 64 MiB maximum download, 256 KiB
PNG maximum screenshot, and PNG dimensions no larger than 1024×1024. Server
calculates SHA-256; files are stored with generated opaque names, outside
`wwwroot`. Every release, asset and software record has upload/update dates.

## GameCube networking

Edit `src/GameCubeAppStore.Console/StoreSettings.cs` with your **LAN-reachable,
HTTP-only public catalog origin** before building the DOL.
Configure Dolphin's GameCube BBA interface and network reachability. Current
DolphinDotNet does not support HTTPS / System.Text.Json; HTTP + its JsonReader
are intentional. Its parser accepts at most 65,536 bytes of JSON. Keep public
catalog payloads bounded. The controller-driven storefront and screenshot browsing are implemented. GameCube-side installation and download management are **not yet implemented**.

**Deployment boundary:** do not expose `/account` or `/api/developer` over
cleartext HTTP. Use HTTPS for the developer website and authentication, and
serve only `GET /api/catalog*` plus published `GET /api/assets/*`
through a separate HTTP-facing read-only reverse proxy for the console.
Hosting the server directly over plain HTTP is for isolated development only.

## Status

The developer portal is served at the API root (same-origin sign-in and management UI). The GameCube front end supports controller navigation, title/rating/details browsing and screenshot preview. Native builds are validated in CI, but hardware BBA execution has not been verified.
Production deployment additionally requires database migrations, monitored
storage/backups, external identity verification / account moderation and
file/security scanning before accepting untrusted public submissions.
