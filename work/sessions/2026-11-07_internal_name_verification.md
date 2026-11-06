# 2026-11-07: Internal rename and runtime verification

## Changes

Renamed namespaces, runtime, planning tool, Windows services, order Bridge, environment variables, launcher and Linux extension/image. Updated referring code, configuration, sample preparation and instructions together.

Locally packed/installed DotNetFaultInjectionAgent.Tool 0.1.0-preview.4 and used dotnet-fault-injection. Retained SourceMap positional arguments and added verb-first forms used by sample preparation. No NuGet.org publication. See [migration](../../docs/development/naming-compatibility.md).

## Results

| Verification | Result |
| --- | --- |
| Windows/Linux native rebuild | Passed, with existing Engine-header/export warnings |
| New package pack/install | Passed capabilities, legacy listing/generation/verification and relocated sample CLI launch |
| All sample preparation | Passed with external order project |
| Linux x64/.NET 8.0.20 | 17 existing cases plus old-runtime coexistence: 18 passed |
| Deployed Linux launcher | Matching conditions: one IOException across 12 calls; mismatch: zero; acceptance/rewrite confirmed |
| Seven Windows samples | Passed 42→-1→42, CoreFileTarget, file, TCP, SQL, order timeout and delay |
| Order timeout | New Bridge loaded, new exception message matched, zero DB rows |
| Order delay | One delay; nondelayed then delayed completion; app 0→1→2, DB 1→2 |
| VM SQL service | Baseline: two closed-connection errors; injection: one TimeoutException then original error; acceptance/rewrite/Running confirmed |
| VM shared-flag service | Running and false→true for ordinary/volatile |
| Cleanup | Target hashes unchanged, both VM services stopped/unregistered, transfer HTTP server stopped |
| Static checks | PowerShell syntax, relative Markdown links and diff checks passed |

Host SDK 10.0.301. Guest ChaosTest ran Windows 10.0.26100.0. VM screen operations were performed at the user's request.

## Evidence

- [Linux 18 cases](2026-11-07_internal-name-verification/linux-summary.json)
- [Windows seven samples](2026-11-07_internal-name-verification/windows-summary.json)
- [Service raw logs and cleanup](2026-11-07_internal-name-verification/service-result.json)

Detailed logs remained in the verification checkout under work/rename-validation/windows-56a430335d954776a0a9c71c6f647762 and poc/scenarios/PlanDrivenInjection/artifacts/verify-a2d4d8e9cab54ff2891afdf4361a9fba. Generated deployments/binaries were not committed.

| Artifact | SHA-256 |
| --- | --- |
| Windows Rewrite.dll | `C17E470E33FB0981D437F968E5E688204BD863628D6AA9A5808F32ECC735ADA4` |
| Linux libDotNetFaultInjectionAgent.so | `9405534D368638E52A8491FD8AB6C70A879DA608AD005E1518160482DF61A1AB` |
| DotNetFaultInjectionAgent.Runtime.dll | `1C9F0B8F4EA4B3AE4895269495C7F4D090C09E17D5446596CD5C415D251EADED` |
| DotNetFaultInjectionAgent.OrderBridge.dll | `C6B56A4E54F9237ED0D65F4E0986B0E6AE84ABB5EBD127919B660CEF20670430` |

Engine source revision: 0055c6fce2fe16770f5908ef14cfd7f8a42c2fab.

## Test-script corrections and limits

The initial service script failed resolving its default bundle directory. With an explicit path, service tests completed but PowerShell 5.1 serialization of Get-Content-decorated strings stalled result saving. Changed log reading to File.ReadAllText, corrected default directory handling and reran successfully. The linked service result comes from that corrected run.

Service injection used a dedicated closed-connection PoC, not a real DB connection. Real DB injection/delay tests ran as normal host processes against LocalDB. In-place business-service migration, signed extensions, NuGet.org distribution and generic Windows version 3 injection were unverified/unperformed. Old CLI/environment aliases were not provided.
