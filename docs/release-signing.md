# CircuitOS Release Signing

**Status (2026-07-22): 1.0 ships UNSIGNED. SignPath Foundation denied the application.** The signing
pipeline is BUILT and PROVEN end-to-end and stays in the repo — the day a certificate exists it is a
thumbprint swap, no code changes. But signing is **no longer a 1.0 gate**.

Signing matters because an unsigned `Setup.exe` trips SmartScreen ("Windows protected your PC — unknown
publisher"), which is exactly the moment a non-technical streamer bails. Shipping unsigned means
accepting that cost and mitigating it in the docs instead — see the mitigation section below.

**Important: signing never blocked anything technical.** `Build-CircuitOSVelopack.ps1` takes no signing
parameters by default; omit `-CertificateThumbprint`/`-SignTemplate` and it publishes an unsigned
release. The updater, the feed, and the install→update round-trip are all indifferent to signatures.

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
2. ~~**Free code signing is now on the table.**~~ **Superseded 2026-07-22 — SignPath Foundation denied
   the application.** Public-OSS status did not convert into free signing. The paid options below are
   what remain.

## SignPath Foundation — DENIED (2026-07-22)

The free-OSS path is closed. CircuitOS applied to the SignPath Foundation program and was **rejected**;
1.0 ships unsigned as a result. Do not re-plan around SignPath without a new application and a new
answer from them.

## Shipping unsigned — how 1.0 mitigates it

Without a certificate the mitigation is transparency, not suppression. Three things carry it:

1. **Tell users what they'll see, before they see it.** `docs/installation-and-updates.md` documents the
   SmartScreen warning as expected, gives the exact click path (**More info** → **Run anyway**), and
   explains that a certificate proves identity, not safety.
2. **Publish a SHA-256 checksum with every release**, so anyone can verify the file they downloaded is
   the file that was built. This is the honest substitute for a signature: it proves integrity, and the
   public repo + public build provide the provenance a cert would otherwise vouch for.
3. **Never tell users to disable antivirus.** Collect the vendor + detection name and submit false
   positives (see the bottom of this doc).

Note the warning **returns on every release**, because each build is a new hash with no SmartScreen
reputation. Unsigned reputation accrues per-file and effectively resets each version — one more reason
to revisit signing after 1.0.

## After 1.0 — the paid path stays open

The pipeline is built, so this is a purchase decision and ~15–30 minutes, not an engineering project.
**Azure Trusted Signing (~$10/mo)** is the lowest-friction option: cloud-based, no USB token, individual
developers are eligible, and `azuresigntool` is already installed on the dev machine. Details below.

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
| ~~SignPath Foundation~~ | — | **CLOSED** | **Applied and denied 2026-07-22.** Not available to CircuitOS. |
| Traditional OV cert (DigiCert/Sectigo/SSL.com) | ~$100–400/yr | **High** | Since June 2023 the private key must live on FIPS hardware — a **physical USB token gets shipped to you**, or you pay extra for a cloud HSM. |
| EV cert | ~$300–600/yr | High | Same token problem, but grants instant SmartScreen reputation. |

**Worth knowing — the public repo still earned its keep.** The auto-updater requires the GitHub Releases
feed to be publicly readable (the app can't ship a token to read a private feed — same foot-gun as
shipping a master key). Going public closed that gate permanently. The hoped-for second payoff — free
SignPath signing — did not materialize, but the feed reason alone justified the move.

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
