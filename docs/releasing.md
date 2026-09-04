# Releasing

Two workflows, with different jobs.

| Workflow | Trigger | What it decides |
|---|---|---|
| `build.yml` | push, pull request | whether the code works — the suite on Windows **and** Linux |
| `release.yml` | push to `main` | what gets shipped — packages both platforms and publishes a release |

---

## How a release happens

Every push to `main` packages both platforms. Whether it *releases* depends on one thing: the
version.

1. The workflow asks MSBuild for the version the projects declare.
2. If the tag `v<version>` already exists, it packages and stops. This is the normal case — most
   pushes do not change the version — so it is a notice, not a failure.
3. If the tag does not exist, it creates the tag and the release and uploads the packages.

**So publishing a release means bumping one number.** `Version` lives in `Directory.Build.props`
and every project inherits it:

```xml
<Version>0.1.0</Version>
```

The version is read with `dotnet msbuild -getProperty:Version` rather than by reading the XML,
because that resolves the value the compiler actually stamps into the assemblies. An inherited
value would be invisible to a grep, and the tag would then disagree with the binaries inside the
archive it names.

The version must be three dot-separated numbers. Anything else fails the run with that sentence
rather than producing a strange tag.

The tag and the release are created with the GitHub CLI, which is preinstalled on the runners, so
no third-party action is involved — the only actions used are `actions/checkout`,
`actions/setup-dotnet` and `actions/upload-artifact`. Nothing is granted at the workflow level;
the one job that publishes asks for `contents: write` and nothing else.

### Why the actions look like that

Every action is pinned to a **commit SHA**, with the version it corresponds to in a comment
beside it:

```yaml
- uses: actions/checkout@11d5960a326750d5838078e36cf38b85af677262 # v4.4.0
```

A tag is a moving reference: whoever can push to the action's repository can change what `@v4`
means, and a workflow with write access would then run something nobody reviewed. A SHA cannot
move.

The cost is pins that rot quietly, which is what `.github/dependabot.yml` is for — it raises the
SHA and the comment together, weekly, as one grouped pull request. So the comment cannot drift
away from the pin it describes, and a stale pin becomes a pull request rather than a slow
accumulation of unnoticed age. NuGet packages are deliberately not watched yet.

`act` clones by whatever ref it is given, so SHA pins work locally too.

## What is in a release

One archive per platform, each carrying **both** applications — the `sasctl` command line and the
SasWorkbench window — published into one directory so the shared assemblies are carried once.

```
SasWorkbench-0.1.0-win-x64.zip
SasWorkbench-0.1.0-linux-x64.tar.gz
SHA256SUMS
```

Framework-dependent, so the .NET 10 runtime is required. That is a deliberate trade: a
self-contained build of the window alone is 104 MB per platform, which is a lot to produce on
every push for readers who have the runtime. The release notes say the runtime is needed.

Linux is packaged as `tar.gz` because it keeps the executable bit on the launcher; Windows as
`zip` because that opens with nothing installed.

## Why both platforms are built on Linux

`release.yml` runs one job on `ubuntu-latest` and cross-publishes the Windows build with
`-r win-x64`. There is no Windows runner in it.

That is what makes the workflow runnable locally with `act`, which cannot start Windows
containers. Nothing is given up by it: cross-publishing produces the same assemblies, and
Windows-native *testing* still happens on every push in `build.yml`, which is the workflow that
decides whether the code works at all. This one only decides what gets shipped.

---

## Running it locally with act

[`act`](https://github.com/nektos/act) runs the workflow in a container against the working tree.
`.actrc` in the repository root already pins the runner image and architecture, so no flags are
needed:

```bash
act push -W .github/workflows/release.yml
```

To run it as if the branch had been pushed by hand:

```bash
act workflow_dispatch -W .github/workflows/release.yml
```

**What a local run does and does not do.** Two steps detect `act` through the `ACT` environment
variable it sets, and skip:

* **uploading the artifact** — act has no artifact service. The packages are written to `./dist`
  in the working tree either way, which is where a local run should look for them.
* **creating the release** — there is no token to release with, and a local run must not be able
  to tag the repository.

Everything before those two runs for real: the version is read, the tag check runs against your
local tags, the solution is restored, built and tested, both platforms are published, both
archives are built and the checksums are written. So a green local run means the packaging is
sound; it does not mean a release was made.

The first run pulls the runner image and installs the SDK inside it, so expect several minutes.
Later runs reuse the container layers.

### If a local run fails

| Symptom | Cause |
|---|---|
| `zip: command not found` | the runner image is the bundled `micro` one — check `.actrc` is being read, or pass `-P ubuntu-latest=catthehacker/ubuntu:act-latest` |
| the version step prints nothing | the SDK did not install; look further up the log at `setup-dotnet` |
| `Cannot connect to the Docker daemon` | Docker Desktop is not running |
| the release step runs locally | `ACT` was not set — you are not running under act, and you are about to tag the repository |
