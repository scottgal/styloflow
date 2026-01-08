You want **one “store item”** that can drop capabilities into three places:

1. **runtime (.NET)** – pipelines, atoms/molecules, detectors, etc
2. **edge/client (JS)** – snippet/widget/captcha/collector
3. **dashboard (UI widgets)** – panels, config screens, charts
   …and it must be **installable, discoverable, licensable, and upgradable** without people doing archaeology.

Here’s a packaging shape that tends to work well in practice.

---

## The core idea: a single sfpkg is a *bundle* with a manifest

Treat `sfpkg` as a **zip bundle** with a strict manifest that points at artifacts:

* `.nupkg` (or just a DLL) for server/runtime
* `npm` package *or* prebuilt JS assets for client injection
* dashboard widget bundle (static assets + a small “widget manifest”)
* docs + defaults + migrations

### Minimal bundle layout

```
my-company.antispam.sfpkg
  /sfpkg.json
  /LICENSE.txt
  /README.md

  /dotnet/
    MyCompany.Styloflow.AntiSpam.nupkg
    /symbols/ (optional)

  /js/
    package.tgz          (optional, if you want npm install)
    /dist/               (always allowed: prebuilt, immutable)
      widget.js
      widget.css
      snippet.js

  /dashboard/
    /dist/
      remoteEntry.js     (or other module format)
      assets/...
    widgets.json

  /migrations/
    001_add_rules.sql
    002_add_indexes.sql

  /defaults/
    config.json
    rules.json
```

The dashboard “store” only needs to understand `sfpkg.json` plus a couple conventions.

---

## Don’t use MEF as your primary extensibility mechanism

MEF works, but it optimizes for “load assemblies and discover exports” and you’ll fight:

* versioning/binding issues
* trimming/AOT/linker friendliness
* security posture (“load arbitrary assemblies” is a red flag)
* testability (DI tends to be cleaner)

Use **regular DI + a small plugin contract**, and keep dynamic loading as an *opt-in*.

### Recommended .NET plugin contract

1. A tiny shared abstractions package (you own):

* `Styloflow.Abstractions`

    * `IStyloflowModule`
    * `IAtom`, `IMolecule` (or your existing taxonomy)
    * `ISignalSink` etc
    * `ILicenseContext` / `ILicenseGate` (important)

2. Each plugin assembly exposes *one* entry type:

```csharp
public interface IStyloflowModule
{
    string Id { get; }
    Version Version { get; }
    void ConfigureServices(IServiceCollection services, IStyloflowModuleContext ctx);
    void MapEndpoints(IEndpointRouteBuilder endpoints, IStyloflowModuleContext ctx); // optional
}
```

3. Discovery options (pick the simplest that meets your needs):

* **Compile-time registration (best UX)**: NuGet package + `services.AddMyPlugin()`
* **Attribute scan**: scan assemblies already referenced (no dynamic load)
* **Dynamic load** (store-installed): load from a plugins folder, but only if the assembly is **signed + allowlisted** by the sfpkg manifest hash.

If you want “install in dashboard then it appears at runtime”, you probably do need dynamic load, but keep it constrained:

* plugin directory = data volume
* only load assemblies whose SHA256 matches manifest
* optional strong-name / Authenticode signature checks
* run in-proc only if you trust it; otherwise isolate (see below)

---

## Make “SUPER easy”: one NuGet gets you 80% + optional store adds features

Have a base package that *everyone* installs once:

* `Styloflow.Runtime` (host + plugin loader + license gate)
* `Styloflow.DashboardHost` (if you ship the dashboard yourself)
* `Styloflow.TagHelpers` (or included in runtime web package)

Then sfpkg becomes a “store-delivered feature pack”.

### Installation flows

**Flow A (simple / dev / OSS):**

* `dotnet add package MyPlugin`
* `app.UseStyloflow(); services.AddStyloflow().AddMyPlugin();`
* done

**Flow B (store / paid / “click to install”):**

* Dashboard: Store → Install
* Runtime sees new sfpkg in mounted volume → validates → activates
* UI widgets appear (remote module)
* License can be purchased/attached inside the dashboard

You can support both with the same plugin package; store just automates distribution and licensing.

---

## Dashboard widgets: ship as a remote module + widget manifest

You want “install package → new screens appear” without rebuilding the dashboard.

Two workable patterns:

### 1) Module Federation style (remoteEntry)

* Dashboard loads installed remotes from `widgets.json`
* Each widget declares:

    * routes it provides
    * panels it provides (cards, charts, tables)
    * config schema

### 2) Pure JSON schema widgets (fastest to ship)

* Widgets are “form + queries + chart types”
* The package ships `widgets.json` and the dashboard renders it generically

You can start with (2) for speed, and later allow (1) for richer UI.

---

## TagHelpers and JS snippets: make it copy/paste easy

For third-party websites, give them **one line** to add.

### TagHelper approach (ASP.NET Core)

Ship `Styloflow.TagHelpers` that supports:

```html
<styloflow-script />
<styloflow-widget name="captcha" />
<styloflow-signal endpoint="..." />
```

Under the hood it:

* injects correct script URLs (self-hosted, versioned)
* injects a public “site key”
* injects CSP-friendly nonces if present
* supports `mode="free|licensed"` but never trusts it; server enforces

### JS distribution

Even if you support npm, always provide:

* a **prebuilt `dist/`** for people who don’t want a toolchain
* a **single snippet** they can paste into any site

---

## Licensing: bind to *capabilities*, not binaries

Your “free but throughput-limited” requirement becomes clean if every plugin must call the same gate:

* `ILicenseGate.Require("myplugin.feature", costUnits: X)`
* if not licensed, gate enforces:

    * rate limit / concurrency limit / node count
    * watermarking / delayed processing / reduced fidelity

This prevents plugins “forgetting” to enforce licensing.

Also: license should be validated offline via signature, and optionally checked online for revocation/seat changes.

---

## One more key decision: in-proc vs sidecar isolation

Because you’re talking about a mesh of nodes and “components could be anything”, decide early:

### In-proc plugins (fast, simplest)

* plugin DLL loaded into host process
* best perf, easiest DI
* higher risk if third-party code is untrusted

### Sidecar plugins (safer, more ops)

* sfpkg installs a container/service
* communicate via gRPC/HTTP + your signal contract
* dashboard still installs widgets + config, runtime routes calls

Given your “store” ambition, a hybrid is strong:

* first-party + trusted partners: in-proc allowed
* everyone else: sidecar only

---

## What should `sfpkg.json` contain?

Keep it boring and strict. Example fields:

* identity: `id`, `name`, `publisher`, `version`
* compatibility: `styloflowVersionRange`, `dotnet`, `os/arch`
* artifacts:

    * `dotnet`: sha256 + path + entry module type
    * `js`: dist paths + integrity hashes
    * `dashboard`: remote module url/path + widgets manifest
* permissions/capabilities:

    * needsSignals: list
    * needsStorage: tables/buckets
    * needsNetwork: outbound domains (optional)
* licensing:

    * features list + limits for free tier
    * metering unit definitions (requests/min, nodes, molecules, etc.)
* migrations + defaults

This is what enables “click install” to be deterministic and auditable.

---

## My recommendation for your first iteration (fast, shippable)

1. **Base Runtime NuGet**: `Styloflow.Runtime`

    * plugin folder watcher
    * sfpkg validator (hash + signature)
    * DI module loader (your `IStyloflowModule`)
    * license gate + rate limiter primitives

2. **Dashboard Host** loads `widgets.json` only (no fancy remotes yet)

    * generic renderer for schema widgets
    * store page can show README + changelog

3. **sfpkg is zip** with:

    * a `.nupkg` inside (or just DLLs)
    * `/js/dist` assets
    * `/dashboard/widgets.json`

4. Add **remote modules later** when you want richer UI.

This gets you to “store → install → appears” quickly, without MEF pain.

---

If you want, I can sketch:

* the `IStyloflowModule` + loader code shape
* a concrete `sfpkg.json` schema
* how the dashboard “store/install” flow should persist packages + activate them
* how node-count / molecule-run limits map onto your mesh runtime cleanly
