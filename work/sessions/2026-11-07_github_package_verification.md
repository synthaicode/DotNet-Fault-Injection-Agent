# 2026-11-07: GitHub Packages test delivery

The user selected GitHub Packages under synthaicode and requested GITHUB_TOKEN-based publication through Actions. Local gh authentication lacked packages scopes, and local NuGet publication returned HTTP 401. No credentials were changed or committed.

Implemented a Windows Actions workflow with contents:read/packages:write, native source build, local verification, publishing, and a second isolated installation from only https://nuget.pkg.github.com/synthaicode/index.json. The token is provided by Actions and supplied to NuGet through environment credentials; no token is stored in repository artifacts.

[Run 42304683913](https://github.com/synthaicode/DotNet-Fault-Injection-Agent/actions/runs/42304683913) completed successfully. Test version: `0.1.0-github.42304683913.1`. The publish step reported success, and the GitHub-feed install passed guide/help, capabilities, payload export, no-overwrite, SHA-256 equality, plan generation/verification and actual Framework/CoreCLR injection tests. The build used GitHub's clean windows-2022 environment and .NET Framework SDK headers, without the previous local private header directory.

Downloaded test artifact SHA-256: `1F043B5D475479BCC4F4C0A61304105F7A38315A5463046C8A02A653D96DF81B`, matching both workflow package-verification summaries. JSON results are stored in [verification evidence](2026-11-07_github-package-verification/summary.json). Raw execution output remains unchanged. No NuGet.org push or repository/package visibility change was performed. Local installation directly from the private GitHub feed still requires read:packages-capable user authentication; the successful remote installation used GITHUB_TOKEN in Actions.
