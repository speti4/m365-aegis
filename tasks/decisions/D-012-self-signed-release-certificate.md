---
status: decided
date: 2026-10-30
cost: medium
sources: [wip/085-signed-exes-and-the-release-zip.md]
---

# D-012 — The release is signed with a self-signed code-signing certificate the team's machines trust

## Context

The release job (task 085) signs the product's exes and assemblies with a certificate from the `release` environment's
secret; which certificate was open. The readers are the support team, on the employer's managed Windows 11 machines,
plus anyone who downloads a release of the public repository. Windows trusts a signature only through a root in the
machine's store; SmartScreen judges a downloaded file by the reputation of its signer; Smart App Control, where it is
on, runs only code signed through Microsoft's trusted root program. The signing script takes a PFX file, which every
option below can produce except the hardware-bound key of a bought certificate.

## Decision

A self-signed code-signing certificate, made once by the maintainer: RSA 3072, SHA-256, code signing only, not a CA,
valid three years (for example `New-SelfSignedCertificate -Type CodeSigningCert -Subject "CN=m365-aegis release
signing" -KeyLength 3072 -NotAfter (Get-Date).AddYears(3) -CertStoreLocation Cert:\CurrentUser\My`), exported once as
a PFX into the `release` environment's secrets (`SIGNING_CERTIFICATE` as base64, `SIGNING_CERTIFICATE_PASSWORD`) and
as a `.cer` for the team; the signatures are timestamped by a public RFC 3161 server, so they outlive the certificate.
The public part is not committed (`.gitignore` keeps `*.cer` out; a trust anchor shipped next to what it authenticates
proves nothing): the maintainer hands it to the team's machines, where an administrator imports it into Trusted Root
Certification Authorities and Trusted Publishers, by hand once per machine or through Intune or a group policy, and
the release notes carry its thumbprint. Cost: none. On a team machine that trusts it the Digital Signatures tab,
`Get-AuthenticodeSignature` and `signtool verify /pa` say valid and the doctor's `signature` row passes; on any other
machine the signature still proves the file unchanged and names the signer but reads as untrusted (the doctor warns),
SmartScreen treats the download as unrecognized (the guide covers the checksum, the attestation and `Unblock-File`),
and Smart App Control would block it. The risk is the key: whoever holds it can sign code those machines trust, so it
lives only in the `release` environment's secret, and a leak means a new certificate and its removal from the
machines. A later task pins the expected thumbprint in the doctor (task 093). Azure Trusted Signing is the upgrade
path if the tool goes beyond the team. Proposed by the task session of 085 on 2026-10-30; decided by the maintainer on
2026-10-30.

## Not chosen

- A bought OV or EV code-signing certificate from a public CA (roughly USD 200 to 600 a year, more for EV; trusted on
  every Windows machine): since June 2023 the CA/Browser Forum's rules put the private key on a hardware token or the
  CA's cloud HSM, so it cannot be a PFX in a GitHub secret; CI signing would need the CA's paid cloud signing service
  and its tool, the identity validated is the employer's or the maintainer's, and SmartScreen reputation still builds
  over time (EV no longer skips it).
- Azure Trusted Signing (Microsoft-managed keys, about USD 10 a month for the basic tier, short-lived certificates
  under a root Windows trusts, signtool through its dlib or Microsoft's GitHub action with OIDC and no long-lived
  secret, Smart App Control compatible): the best technical fit, but it needs an Azure subscription and an identity
  validation, an organization's (the employer's legal name would be the publisher of every release of a public
  personal repository) or an individual's, which Microsoft offers in a few countries only.
- No signature: the checksum and the attestation already prove the build, but the doctor and the team's machines could
  tell nothing about a changed exe.
