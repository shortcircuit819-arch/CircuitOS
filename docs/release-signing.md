# CircuitOS Release Signing

**Status (2026-07-16): the signing pipeline is BUILT and PROVEN end-to-end. The only missing piece is a
real certificate**, which requires a validated identity and therefore a human. Everything else is one
command.

Signing matters because an unsigned `Setup.exe` trips SmartScreen ("Windows protected your PC — unknown
publisher"), which is exactly the moment a non-technical streamer bails.

## What's already done

`tools/package/Build-CircuitOSVelopack.ps1` publishes, packs the Velopack installer + update feed, signs,
verifies the signature, and can upload the release — in one command. It reads the version from the
`.csproj` so it can never drift.

Proven on 2026-07-16 with a throwaway self-signed certificate: SignTool discovery, the sign command,
**RFC 3161 timestamping** (DigiCert countersigned successfully), Velopack's `--signParams` hook (signed
3/3 app files + `Setup.exe`), and signature verification all work. The test build reported
`UnknownError` — signature present, chain untrusted — which is precisely correct for a self-signed cert.

**So: the day a real certificate exists, signing is a thumbprint swap. No code changes.**

## Tooling — already installed (2026-07-16)

Every signing tool CircuitOS can use is present on the dev machine, so there's nothing to install:

- **AzureSignTool 7.0.1** (`azuresigntool`) — for the Azure Trusted Signing path.
- **Windows SDK SignTool** — `C:\Program Files (x86)\Windows Kits\10\bin\10.0.26100.0\x64\`.
- **Velopack's embedded signtool** — used automatically by `vpk pack --signParams/--signTemplate`.

## The repo is now PUBLIC — this changes everything (2026-07-16)

Two consequences:

1. **The auto-update feed works now.** The updater reads GitHub Releases; a public repo means the app can
   fetch them without a token. Publish a signed release (`… -Upload`) and in-app updates go live.
2. **Free code signing is now on the table.** Public OSS projects qualify for **SignPath Foundation**,
   which signs releases at **no cost**. This is now the recommended path — it removes the money barrier
   entirely.

## Recommended path: SignPath Foundation (free, public OSS)

The one thing no tool or agent can do for you is **prove to a Certificate Authority that you are you** —
that identity check *is* the trust, so it's irreducibly yours. With SignPath Foundation the rest is free:

1. Apply to the **SignPath Foundation** program at `https://signpath.org/` (or `about.signpath.io/product/open-source`)
   — submit the CircuitOS GitHub repo. Approval is a review by them (you, the owner, apply; I can't apply
   as you).
2. Once approved, SignPath gives you an **organization id, project/signing-policy slugs, and an API token**.
   SignPath signs via **submission** (upload the built artifact → they sign → you get it back), typically
   wired through their **official GitHub Action** in a release workflow, or their CLI/API for a local build.
3. Add those as repo secrets (for CI) or feed them to the signing step for a local build.

That's the whole of your side. If you want, I'll write the GitHub Actions release workflow (public repos
get **free** Actions minutes) that builds → submits to SignPath → publishes the signed release, so a tag
push produces a signed 1.0 automatically.

## Alternative: Azure Trusted Signing (~$10/mo, fits the current pipeline today)

Lower setup if you'd rather not wait for SignPath approval; it plugs straight into
`Build-CircuitOSVelopack.ps1 -SignTemplate` (azuresigntool is already installed):

1. Azure Portal → create a **Trusted Signing account** (note the region + endpoint URL).
2. Complete the one-time **identity validation** (individual ID check).
3. Create a **certificate profile** (note its name).
4. Give those values to the build command below. Done — from then on every release signs automatically.

Realistically ~15–30 minutes of your time, once, ever.

## The certificate options — getting one

A *trusted* signature requires a CA to validate your identity. Pick one:

| Option | Cost | Friction | Notes |
|---|---|---|---|
| **Azure Trusted Signing** | ~$10/mo | **Lowest** — cloud, no hardware | Recommended. Individual developers are eligible. One-time ID validation, then sign from anywhere. |
| **SignPath Foundation** | **Free** | Low — but OSS only | Requires CircuitOS to be a **public** open-source repo. See the note below. |
| Traditional OV cert (DigiCert/Sectigo/SSL.com) | ~$100–400/yr | **High** | Since June 2023 the private key must live on FIPS hardware — a **physical USB token gets shipped to you**, or you pay extra for a cloud HSM. |
| EV cert | ~$300–600/yr | High | Same token problem, but grants instant SmartScreen reputation. |

**Worth knowing — public repo solves two problems at once.** The auto-updater *already* requires the
GitHub Releases feed to be publicly readable (the app can't ship a token to read a private feed — same
foot-gun as shipping a master key). If CircuitOS goes public to serve the update feed, it likely also
becomes eligible for **free** SignPath Foundation signing. If it stays private, publish releases to a
separate public releases repo and use Azure Trusted Signing.

### Azure Trusted Signing — the recommended path
1. Azure subscription → create a **Trusted Signing account** (region matters; note the endpoint URL).
2. Complete **identity validation** (one-time; individual ID check).
3. Create a **certificate profile** (note the profile name).
4. Hand the endpoint / account / profile values to the build — done.

## Building a signed release

```powershell
# Azure Trusted Signing (recommended)
tools\package\Build-CircuitOSVelopack.ps1 -SignTemplate 'azuresigntool sign -kvu <endpoint> -kvi <client-id> -kvs <secret> -kvc <cert-profile> -tr http://timestamp.acs.microsoft.com -td sha256 {{file}}'

# A local certificate / token
tools\package\Build-CircuitOSVelopack.ps1 -CertificateThumbprint <THUMBPRINT>

# Add -Upload to publish the release + update feed to GitHub
```

The legacy ZIP flow has its own signer for the plain `CircuitOS.exe`:

```powershell
tools\package\Sign-CircuitOSRelease.ps1 -CertificateThumbprint <THUMBPRINT>
```

It uses SHA-256, requests an RFC 3161 timestamp, verifies the result, and only then rebuilds the ZIP.

## Requirements (already satisfied on the dev machine)

- Windows SDK SignTool — **present** at `C:\Program Files (x86)\Windows Kits\10\bin\10.0.26100.0\x64\`.
  (Velopack also carries its own embedded signtool.)
- Network access to the RFC 3161 timestamp service — **verified working**.
- A certificate with a private key in `Cert:\CurrentUser\My` (local-cert path only).

## False Positives

Code signing improves trust but cannot guarantee every security product accepts every new release.
Record the exact vendor and detection name. For Microsoft Defender, submit the signed file at
`https://www.microsoft.com/en-us/wdsi/filesubmission`. Never ask recipients to disable antivirus or add
broad folder exclusions. Every rebuilt EXE has a new hash, so sign *before* release testing and vendor
submission.
