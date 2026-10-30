---
size: M
origin: session 2026-10-30
created: 2026-10-30
started: 2026-10-30
plan: none
milestone: M6 Release and team
---

# 085 — Signed exes and the release zip through a tag-driven release workflow

## Context

A release workflow on a `v*` tag that publishes the two exes framework-dependent and ReadyToRun (D-007), signs
them (the certificate question is part of the task: a self-signed certificate the team trusts versus a bought one,
recorded as a decision), zips them with the usage guide of task 086 and the licenses, and attaches the zip to a GitHub
release with its hash.
Why: the M6 done-when is the support team running a release from the zip on their own machines; today a release is a
local publish.
Done when: a tag produces a release with a signed zip whose `signtool verify` passes on a clean Windows 11 machine,
the doctor runs from the unzipped folder, and the release steps are in docs/development.md.
What exists already (docs/development.md, "The release"): `scripts/release.ps1` publishes, checks the version, writes
the third-party notices, copies the README as `GUIDE.md`, zips, writes `SHA256SUMS.txt` and the notes; the `release`
job runs it on a `v*` tag on `main`, attests the build provenance and publishes the GitHub release. So this task is the
signing (the certificate decision, the `signtool` step in the script and the job, the verification), the guide swap
(task 086's `docs/usage.md` ships as the guide instead of the README) and the docs; the workflow itself is not rebuilt.
Ninth round of 2026-10-30, with task 086 on one branch; the task session owns the steps below and may reshape them.
The certificate decision is written as a `proposed` decision in the task's PR text and Log; the main session files it
as a `D-` record (task sessions create no ids), and the maintainer decides it.

## Docs that move

- `docs/development.md`, "The release": the signing step, where the certificate lives (a GitHub secret, never the
  repo), how a reader verifies the exes and the zip, the guide that ships
- `README.md`: the release section, if it names the guide file or the verification
- `SECURITY.md`: how a downloaded release is verified (the attestation and the signature)

## Not in this change

- The usage guide's text (task 086, same branch)
- Distribution through winget or Intune
- A bought certificate's purchase (the decision names the options; the maintainer buys or trusts)

## Steps

1. [x] The certificate decision drafted: self-signed (made once, its public part in the repo for the team to trust,
       the private key a GitHub secret) versus a bought code-signing certificate versus Azure Trusted Signing;
       cost, what the team's machines show, what the doctor can check; the draft in the PR text and the Log
2. [x] `scripts/release.ps1` signs `m365-aegis.exe` and `m365-aegis-gui.exe` (and the ReadyToRun assemblies the
       chosen scheme covers) with `signtool` when a certificate is given, times tamps them, and verifies them
       (`signtool verify /pa`); without a certificate it says so and the zip is unsigned (the dry run of the
       `windows` job stays green)
3. [x] The `release` job gets the certificate from a secret, signs, and the release notes name the signature; a
       missing secret fails the release, not the dry run
4. [x] The doctor's check list, if it can: the running exe's signature state (`pass` signed by the expected
       publisher, `warn` unsigned), or a documented reason why not
5. [x] Tests: the release script's dry run covers the signing branch over a throwaway self-signed certificate made in
       the test (Pester or a PowerShell test the `windows` job runs); the docs under *Docs that move*

## Log

- 2026-10-30 — promoted on `task/085-signed-release` with task 086
- 2026-10-30 — signing: `scripts/sign-release.ps1` (new; `release.ps1 -CertificatePath` calls it after the publish and
  the version check, before the notices and the zip). It signs the product's own files at the top of the publish
  folder, `m365-aegis.exe`, `m365-aegis-gui.exe`, `m365-aegis.dll`, `m365-aegis-gui.dll` and `M365Aegis.Core.dll`
  (the names `m365-aegis*.exe|dll` and `M365Aegis.*.dll`): an apphost alone would leave the managed code unsigned.
  The packages' assemblies keep their publishers' signatures; the sidecar script stays unsigned, since a signature
  block would change the hash the doctor compares with the Core's copy. `signtool sign /fd SHA256` from a PFX whose
  password is `M365AEGIS_SIGNING_PASSWORD` (never a parameter, never printed; PSScriptAnalyzer's plain-text password
  rule holds), `/tr` with `/td SHA256` against `http://timestamp.digicert.com` by default, three tries. The
  verification: the signer is the given certificate, the hash holds, the timestamp is there when asked, and
  `signtool verify /pa`; a self-signed certificate the machine does not trust (a runner, a contributor's machine) is
  checked against itself with an `X509Chain` whose only trusted root is the certificate. Without a certificate the
  `signing:` line says the zip is unsigned and the notes say the exes are not signed; `-RequireSignature` fails before
  anything is built without a certificate or a timestamp server. The release notes get one line naming the signer and
  the thumbprint.
- 2026-10-30 — the `release` job: `environment: release`, the secrets `SIGNING_CERTIFICATE` (the PFX in base64) and
  `SIGNING_CERTIFICATE_PASSWORD`, the PFX written to `RUNNER_TEMP` and removed in a `finally`, `release.ps1
  -RequireSignature` (a missing secret fails the release). The `windows` job: the Pester signing tests and the dry run
  signed with a throwaway certificate made in the step (no secret, so a fork's pull request runs it); the `sidecar`
  job's PSScriptAnalyzer also reads `tests/release`. zizmor and actionlint clean.
- 2026-10-30 — the doctor's `signature` row (feasible, so done): `Diagnostics/Authenticode.cs` calls `WinVerifyTrust`
  (Authenticode policy, no UI, no revocation, cache-only retrieval, so `--offline` stays offline) over the same product
  files in `AppContext.BaseDirectory` and reads the signer's subject from the state through
  `WTHelperProvDataFromStateData` and `WTHelperGetProvSignerFromChain` (`X509Certificate.CreateFromSignedFile` is
  obsolete, SYSLIB0057). Pass: every file signed by a trusted certificate, with the signer. Warn: unsigned (a build
  from source, an unsigned zip), or an untrusted, distrusted or expired-without-timestamp certificate, with the signer
  and the `WinVerifyTrust` code. Fail (not required): a signature that does not hold. Skipped off Windows and without
  a product file. The expected publisher is not pinned: it waits for the decision below (a follow-up). The interop is
  excluded from the coverage and listed in docs/development.md, Coverage; the names and the code verdicts are tested.
- 2026-10-30 — the certificate decision, drafted for the main session to file as a `proposed` D- record (the PR text
  carries the same):
  - **Context.** The release job signs the product's exes and assemblies with a certificate from the `release`
    environment's secret; which certificate is open. The readers are the support team, on the employer's managed
    Windows 11 machines, plus anyone who downloads a release of the public repository. Windows trusts a signature only
    through a root in the machine's store; SmartScreen judges a downloaded file by the reputation of its signer; Smart
    App Control, where it is on, runs only code signed through Microsoft's trusted root program. The signing script
    takes a PFX file, which every option below can produce except the hardware-bound key of a bought certificate.
  - **Decision (proposed).** A self-signed code-signing certificate, made once by the maintainer (RSA 3072, SHA-256,
    code signing only, not a CA, three years, e.g. `New-SelfSignedCertificate -Type CodeSigningCert -Subject
    "CN=m365-aegis release signing" -KeyLength 3072 -NotAfter (Get-Date).AddYears(3) -CertStoreLocation
    Cert:\CurrentUser\My`, exported once as a PFX into the secret and as a `.cer` for the team), timestamped by a
    public RFC 3161 server so the signatures outlive it. Its public part is not committed (`.gitignore` keeps `*.cer`
    out, and a trust anchor shipped next to what it authenticates proves nothing); the maintainer hands it to the
    team's machines, imported into Trusted Root Certification Authorities and Trusted Publishers by the administrator
    (by hand once per machine, or by Intune or a group policy where the machines are managed), and the release notes
    carry its thumbprint. Cost: none. On a team machine that trusts it: the file's Digital Signatures tab and
    `Get-AuthenticodeSignature` say valid, `signtool verify /pa` passes, and the doctor's `signature` row passes; on any
    other machine the signature still proves the file unchanged and names the signer, but reads as untrusted
    (the doctor warns with the signer), SmartScreen treats the download as unrecognized (the guide has the checksum,
    the attestation and `Unblock-File`), and Smart App Control would block it. The doctor can check it fully: valid,
    and, as a follow-up, the expected thumbprint pinned in the Core. Risk: whoever holds the key can sign code those
    machines trust, so the key lives only in the `release` environment's secret and a leak means a new certificate and
    a removal from the machines.
  - **Not chosen.** A bought OV or EV code-signing certificate from a public CA (roughly USD 200 to 600 a year, more
    for EV; trusted on every Windows machine): since June 2023 the CA/Browser Forum's rules put the private key on a
    hardware token or the CA's cloud HSM, so it cannot be a PFX in a GitHub secret; CI signing needs the CA's paid
    cloud signing service and its own tool instead of the PFX path, the identity validated is the employer's or the
    maintainer's, and SmartScreen reputation still builds over time (EV no longer skips it). Azure Trusted Signing
    (Microsoft-managed keys, about USD 10 a month for the basic tier, short-lived certificates under a root Windows
    trusts, signtool through its dlib or Microsoft's GitHub action with OIDC and no long-lived secret, Smart App Control
    compatible): the best technical fit and the upgrade path if the tool goes beyond the team, but it needs an Azure
    subscription and an identity validation, an organization's (the employer's legal name would then be the publisher
    of every release of a public personal repository, which the no-tenant-data rule keeps out of the repo) or an
    individual's, which Microsoft offers in a few countries only; the maintainer checks both before choosing it.
    No signature at all: the zip's checksum and attestation already prove the build, but the doctor and the team's
    machines could tell nothing about a changed exe.

## Deviations from the plan

- Step 1 named "its public part in the repo for the team to trust"; the draft keeps it out of the repo (`*.cer` is
  gitignored, and a trust anchor next to what it authenticates proves nothing) and hands it out with its thumbprint in
  the release notes.
- Step 2's signing and verification live in their own script, `scripts/sign-release.ps1`, which `release.ps1`
  calls, so the Pester tests run them over a small folder without a full publish; it signs the product's own
  assemblies as well as the two exes (the ReadyToRun third-party assemblies keep their own signatures).
- Step 5: the `windows` job's dry run itself now signs, with a throwaway certificate made in the step, instead of
  staying unsigned; the unsigned path (no certificate) is the default of a contributor's `-DryRun` and was run
  locally. The tests are Pester (`tests/release`), with the doctor run over a signed and a changed folder.
- The doctor's `signature` row passes on any trusted signer; pinning the expected publisher waits for the decision
  (follow-up).

## Verification

- 2026-10-30, Windows 11, pwsh 7.6.9, signtool 10.0.22621: `Invoke-Pester tests/release` (Pester 5.7.1) 7 passed:
  the product files signed by the throwaway certificate and verified against it, `System.CommandLine.dll` keeping
  Microsoft's signature and the sidecar script unsigned, the doctor of a signed copy of the command line folder
  answering `signature` warn "m365-aegis.dll (0x800B0109), … signed by CN=m365-aegis test signing (throwaway), which
  this machine does not trust", and after one DOS-stub byte of `m365-aegis.dll` changed, fail "m365-aegis.dll
  (0x80096010): the signature does not hold, …"; a wrong password and a missing file refused; `release.ps1
  -RequireSignature` refused without a certificate and without a timestamp server.
- 2026-10-30: `scripts/release.ps1 -DryRun` without a certificate (baseline) green; `scripts/release.ps1 -Out
  <scratch> -CertificatePath <throwaway pfx> -TimestampUrl ''` green: `signing: CN=m365-aegis test signing (throwaway)
  (…), no timestamp, self-signed and not trusted here, verified against itself; m365-aegis-gui.dll, m365-aegis-gui.exe,
  m365-aegis.dll, m365-aegis.exe, M365Aegis.Core.dll`, 11.6 MB unpacked, 4.7 MB zipped, the notes' last line naming the
  signer and the thumbprint, `GUIDE.md` the usage guide. From the unzipped folder `m365-aegis.exe --table doctor
  --offline` exit 0 with the `signature` warn for the untrusted throwaway signer; `signtool verify /pa` there fails
  with the untrusted root, as expected for a certificate nothing trusts.
- 2026-10-30, the final tree: `scripts/release.ps1 -DryRun -CertificatePath <throwaway pfx> -TimestampUrl ''`, as the
  `windows` job runs it, green with the five files signed and verified; `scripts/release.ps1 -DryRun` without a
  certificate green with `signing:  none: no -CertificatePath, so the zip is unsigned`.
- 2026-10-30: the doctor of the dev build (`src\M365Aegis.Cli\bin\Release\net10.0\m365-aegis.exe --table doctor
  --offline`): `signature` warn "m365-aegis.dll, m365-aegis.exe, M365Aegis.Core.dll: not signed (a build from source,
  or an unsigned zip)".
- 2026-10-30: the local matrix (see the PR's checklist): restore `--locked-mode`, `dotnet format
  --verify-no-changes`, the Release build, `dotnet test` with the coverage gate at 100% lines and branches, the
  changelog check, the fixture check, PSScriptAnalyzer over `scripts`, `tests/sidecar`, `tests/release` and the
  sidecar (0 findings), `uvx zizmor .github` (no findings), actionlint (clean), the dependency checks.
- Not verified here: a real tag release with the `release` environment's secret, and `signtool verify /pa` on a clean
  Windows 11 machine that trusts the chosen certificate; both are the main session's after the merge and the decision.

## Closing checklist

- [x] every follow-up is listed in the pull request under `## Follow-ups`, the main session proposes and files them
- [x] every doc under *Docs that move* changed in this change
- [x] Verification written, dated
- [x] `tasks_check` green (the stale index is the main session's)
- [ ] moved to `done/` with `closed: YYYY-MM-DD` by the main session after the merge
