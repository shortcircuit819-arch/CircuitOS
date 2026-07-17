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

## The one human step — getting a certificate

There is no way around this: a *trusted* signature requires a CA to validate your identity. That's the
entire point of the trust model. Pick one:

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
