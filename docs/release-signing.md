# CircuitOS Release Signing

CircuitOS public releases should be Authenticode-signed. Removing script
launchers improves the recipient experience, but it does not establish publisher
identity or antivirus reputation.

## Requirements

- A trusted code-signing certificate with a private key
- The certificate installed in `Cert:\CurrentUser\My`
- Windows SDK SignTool installed
- Network access to the selected RFC 3161 timestamp service

Microsoft also offers Trusted Signing as a managed signing option:

`https://learn.microsoft.com/en-us/azure/trusted-signing/overview`

## Build And Sign

Build the clean release:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass `
  -File tools\package\Build-CircuitOSPackage.ps1
```

Then sign it using the certificate thumbprint:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass `
  -File tools\package\Sign-CircuitOSRelease.ps1 `
  -CertificateThumbprint "YOUR_CERTIFICATE_THUMBPRINT"
```

The signing script uses SHA-256, requests an RFC 3161 timestamp, verifies the
result, and rebuilds the release ZIP only after the signature is valid.

Microsoft SignTool documentation:

`https://learn.microsoft.com/en-us/windows/win32/seccrypto/signtool`

## False Positives

Code signing improves trust but cannot guarantee that every security product
will accept every new release. Record the exact vendor and detection name. For
Microsoft Defender, submit the signed file for analysis at:

`https://www.microsoft.com/en-us/wdsi/filesubmission`

Do not ask recipients to disable antivirus protection or create broad folder
exclusions. Every rebuilt EXE has a new hash, so signing should happen before
release testing and vendor submission.
