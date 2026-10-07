# InkGuide — THIRD_PARTY_NOTICES

> Generated from actual dependencies on 2026-10-07 by the host/tools/packaging
> workstream. Regenerate when dependencies change (see docs/PUBLISHING.md).
> Only MIT / Apache-2.0 / BSD / ISC / MS-PL licensed packages are permitted
> (MASTER_PROMPT R-24).

## .NET (NuGet, central versions in Directory.Packages.props)

| Package | Version | License |
|---|---|---|
| Microsoft.Web.WebView2 | 1.0.3296.44 | MIT |
| Microsoft.Extensions.Hosting | 9.0.8 | MIT |
| Microsoft.Extensions.DependencyInjection | 9.0.8 | MIT |
| Microsoft.Extensions.Options | 9.0.8 | MIT |
| Microsoft.Extensions.Logging | 9.0.8 | MIT |
| Microsoft.Extensions.Http | 9.0.8 | MIT |
| Microsoft.EntityFrameworkCore.Sqlite | 9.0.8 | MIT |
| Microsoft.EntityFrameworkCore.Design | 9.0.8 | MIT |
| Google.Apis.Drive.v3 | 1.69.0.3608 | Apache-2.0 |
| Google.Apis.Auth | 1.69.0 | Apache-2.0 |
| Serilog | 4.3.0 | Apache-2.0 |
| Serilog.Extensions.Logging | 9.0.1 | Apache-2.0 |
| Serilog.Sinks.File | 6.0.0 | Apache-2.0 |
| Polly | 8.6.2 | BSD-3-Clause |
| Polly.Extensions | 8.6.2 | BSD-3-Clause |
| DiffPlex | 1.7.2 | Apache-2.0 |
| xunit | 2.9.3 | Apache-2.0 |
| xunit.runner.visualstudio | 3.1.2 | Apache-2.0 |
| NSubstitute | 5.3.0 | BSD-3-Clause |
| Shouldly | 4.3.0 | BSD-3-Clause |
| NetArchTest.Rules | 1.3.2 | Apache-2.0 |
| coverlet.collector | 6.0.4 | MIT |
| Microsoft.NET.Test.Sdk | 17.12.0 | MIT |

License texts: see each package's NuGet page / repository. WebView2 runtime
itself is distributed by Microsoft under its own terms; the SDK package above
is MIT.

## npm (ui/)

> Refreshed 2026-10-08 for the Vite 8 upgrade (see docs/DECISIONS.md D-017).
> Licenses read from each installed package's `package.json` (`license` field).

| Package | Version (installed) | License |
|---|---|---|
| react | 19.1.1 | MIT |
| react-dom | 19.1.1 | MIT |
| zustand | 5.0.8 | MIT |
| codemirror | 6.0.1 | MIT |
| @codemirror/state | 6.5.2 | MIT |
| @codemirror/view | 6.38.1 | MIT |
| @codemirror/language | 6.12.1 | MIT |
| @codemirror/lang-javascript | 6.2.5 | MIT |
| marked | 18.1.0 | MIT |
| dompurify | 3.4.16 | Apache-2.0 (dual licensed MPL-2.0 OR Apache-2.0; Apache-2.0 terms apply) |
| vite | 8.3.3 | MIT |
| @vitejs/plugin-react | 5.2.0 | MIT |
| typescript | 5.9.2 | Apache-2.0 |
| typescript-eslint | 8.71.1 | MIT |
| eslint | 9.34.0 | MIT |
| eslint-plugin-jsx-a11y | 6.10.2 | MIT |
| eslint-plugin-react-hooks | 5.2.0 | MIT |
| vitest | 4.1.11 | MIT |
| @vitest/coverage-v8 | 4.1.11 | MIT |
| jsdom | 26.1.0 | MIT |
| @testing-library/react | 16.3.0 | MIT |
| @testing-library/jest-dom | 6.8.0 | MIT |
| @testing-library/user-event | 14.6.1 | MIT |
| @types/react | 19.1.13 | MIT |
| @types/react-dom | 19.1.9 | MIT |
| @types/node | 26.6.4 | MIT |

No UI kits, CSS frameworks, icon packs, web fonts, animation libraries, or CDN
assets are used (MASTER_PROMPT R-25). No GPL/AGPL/SSPL packages.
