# 2026-11-07: MIT, English documentation and AI planning guidance

Added the MIT LICENSE and NuGet SPDX license expression, included LICENSE and the AI planning guide in DotNetFaultInjectionAgent.Tool, and translated public/development documents and historical session narratives to English. Preserved raw diagnostic evidence as captured rather than translating original log strings. Replaced the Japanese overview image and generation instructions with the reviewed English version, generated with the built-in image tool.

The AI guide includes a copyable request, required target/source/PDB/environment inputs, capability selection, CLI binding and verification, supported fault/condition boundaries and the distinction between plan verification and observed execution. Both repository and package READMEs describe this workflow.

Verification: Markdown and project references passed; no Japanese prose remains in tracked Markdown/image instructions. The generated diagram was visually checked. The package verifier requires LICENSE, AI-plan-guide.md and MIT SPDX metadata, then installs from a local-only feed with an empty cache and creates/verifies a plan against Probe. All checks passed. No runtime implementation changed, so prior runtime suites were not repeated.

Candidate version: 0.1.0-preview.4. Package SHA-256: 91B44A438E16B3A0D7CDCA1BCAB5A0D9F70DB6A34AF6FC5BB008502275F81E8A.

NuGet.org's flat-container endpoint returned HTTP 404 for the ID at verification time; that does not establish ID ownership or publishing authorization. No NuGet push occurred. Publishing credentials, final release version and public access to documentation remain release decisions; GitHub visibility was not changed.
