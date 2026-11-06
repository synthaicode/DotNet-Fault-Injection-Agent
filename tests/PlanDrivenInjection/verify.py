"""Exercise the built profiler against a deployment unrelated to business code."""
import copy
import json
import pathlib
import shutil
import subprocess
import sys
import uuid

ROOT = pathlib.Path(__file__).resolve().parent
REPO = ROOT.parents[1]
NATIVE = REPO / "src/Native/Linux/bin"
IMAGE = sys.argv[1] if len(sys.argv) > 1 else "dotnet-fault-injection-runtime:local"
OUTPUT = ROOT / "artifacts" / ("verify-" + uuid.uuid4().hex)
OUTPUT.mkdir(parents=True)
PLANNER = REPO / "src/Planner/bin/Release/net8.0/Planner.dll"
ASSEMBLY = ROOT / "Probe/bin/publish/Probe.dll"
SOURCE = ROOT / "Probe/Program.cs"
LINE = next(i for i, line in enumerate(SOURCE.read_text().splitlines(), 1)
            if 'File.WriteAllText(path, "selected")' in line)


def command(args, expected=0):
    result = subprocess.run(args, capture_output=True, encoding="utf-8", timeout=120)
    if expected is not None and result.returncode != expected:
        raise AssertionError((args, result.returncode, result.stdout, result.stderr))
    return result


for project in (REPO / "src/Planner/Planner.csproj", REPO / "src/Runtime/Runtime.csproj", ROOT / "Probe/Probe.csproj"):
    command(["dotnet", "build", str(project), "-c", "Release", "--nologo"])
command(["dotnet", "publish", str(ROOT / "Probe/Probe.csproj"), "-c", "Release",
         "--no-restore", "-o", str(ASSEMBLY.parent)])
runtime = REPO / "src/Runtime/bin/Release/net8.0"
shutil.copy2(runtime / "DotNetFaultInjectionAgent.Runtime.dll", ASSEMBLY.parent)
shutil.copy2(runtime / "DotNetFaultInjectionAgent.Runtime.deps.json", NATIVE)
deps = json.loads((ASSEMBLY.parent / "Probe.deps.json").read_text(encoding="utf-8"))
assert not any(name.startswith("DotNetFaultInjectionAgent.Runtime/") for name in deps["libraries"])
base_file = OUTPUT / "base.json"
command(["dotnet", str(PLANNER), "plan", str(ASSEMBLY), str(SOURCE), str(LINE), str(base_file)])
BASE = json.loads(base_file.read_text())
BASE["target"]["path"] = "/app/Probe.dll"
BASE["conditions"]["environment"] = {"CHAOS_TEST_CASE": "enabled"}
BASE["fault"] = {"kind": "throw", "exceptionType": "System.IO.IOException",
                 "message": "planned file failure", "delayMs": 0}
results = []


def execute(name, plan, environment="enabled", profiling=True, raw=None, mutate=False):
    directory = OUTPUT / name
    directory.mkdir()
    plan_path = directory / "plan.json"
    plan_path.write_text(raw if raw is not None else json.dumps(plan), encoding="utf-8")
    args = ["docker", "run", "--rm",
            "--mount", f"type=bind,source={ASSEMBLY.parent.as_posix()},target=/app,readonly",
            "--mount", f"type=bind,source={NATIVE.as_posix()},target=/profiler,readonly",
            "--mount", f"type=bind,source={directory.as_posix()},target=/test", "-w", "/app"]
    if profiling:
        for value in ["CORECLR_ENABLE_PROFILING=1",
                      "CORECLR_PROFILER={324F817A-7420-4E6D-B3C1-143FBED6D855}",
                      "CORECLR_PROFILER_PATH=/profiler/libInstrumentationEngine.so",
                      "MicrosoftInstrumentationEngine_ConfigPath64_DotNetFaultInjectionAgent=/profiler/Rewrite.xml",
                      "MicrosoftInstrumentationEngine_DisableCodeSignatureValidation=1",
                      "DOTNET_ADDITIONAL_DEPS=/profiler/DotNetFaultInjectionAgent.Runtime.deps.json",
                      "DOTNET_FAULT_INJECTION_PLAN=/test/plan.json", f"CHAOS_TEST_CASE={environment}"]:
            args.extend(["-e", value])
    args.extend([IMAGE, "timeout", "30s", "dotnet", "Probe.dll", "/test/files", "12"])
    if mutate:
        args.append("mutate-plan")
    result = command(args)
    (directory / "process.log").write_text(result.stdout + result.stderr, encoding="utf-8")
    rows = [json.loads(line) for line in result.stdout.splitlines() if line.startswith('{"index"')]
    events = [json.loads(line) for line in result.stderr.splitlines() if line.startswith('{"event"')]
    assert len(rows) == 12
    assert (directory / "files/other.txt").read_text() == "other"
    results.append({"case": name, "errors": sum(row["error"] is not None for row in rows),
                    "firings": len(events), "passed": True})
    return rows, events, result.stderr


def valid(name, plan, error_count, environment="enabled", profiling=True):
    path = OUTPUT / (name + "-verified.json")
    path.write_text(json.dumps(plan), encoding="utf-8")
    command(["dotnet", str(PLANNER), "verify", str(ASSEMBLY), str(SOURCE), str(LINE), str(path)])
    rows, events, log = execute(name, plan, environment, profiling)
    assert sum(row["error"] is not None for row in rows) == error_count
    assert sum(not row["exists"] for row in rows) == error_count
    assert len(events) == (error_count if plan["fault"]["kind"] == "throw" else plan["maxFirings"])
    return rows, events


valid("disabled", BASE, 0, profiling=False)
rows, events, log = execute("plan-changed-after-load", BASE, mutate=True)
assert not events and all("plan changed after instrumentation" in row["error"] for row in rows)
valid("environment-mismatch", BASE, 0, environment="different")
valid("one-fault-12-concurrent-calls", BASE, 1)
# Optional upgrade check: an old runtime beside the new DLL must not take over.
if len(sys.argv) > 2:
    legacy = ASSEMBLY.parent / "WindowsChaosAgent.Runtime.dll"
    if legacy.exists():
        raise AssertionError("Use a clean deployment for the legacy coexistence check")
    shutil.copy2(pathlib.Path(sys.argv[2]).resolve(), legacy)
    try:
        valid("legacy-runtime-beside-new-runtime", BASE, 1)
    finally:
        legacy.unlink()
plan = copy.deepcopy(BASE)
plan["maxFirings"] = 0
valid("zero-budget", plan, 0)
plan = copy.deepcopy(BASE)
plan["conditions"]["startAtInvocation"] = 3
plan["maxFirings"] = 2
_, events = valid("start-third-two-firings", plan, 2)
assert all(event["invocation"] >= 3 for event in events)
plan = copy.deepcopy(BASE)
plan["fault"] = {"kind": "throw", "exceptionType": "System.TimeoutException",
                 "message": "a different planned message", "delayMs": 0}
rows, _ = valid("timeout-from-plan", plan, 1)
assert any(row["error"] == "System.TimeoutException: a different planned message" for row in rows)
plan = copy.deepcopy(BASE)
plan["fault"] = {"kind": "delay", "exceptionType": "", "message": "", "delayMs": 200}
rows, _ = valid("delay-from-plan", plan, 0)
assert max(row["elapsedMs"] for row in rows) >= 190

for case, mutate in [
    ("wrong-hash", lambda p: p["target"].update(sha256="0" * 64)),
    ("wrong-mvid", lambda p: p["target"].update(mvid="00000000-0000-0000-0000-000000000001")),
    ("unsupported-fault", lambda p: p["fault"].update(exceptionType="System.NotSupportedException")),
    ("unknown-field", lambda p: p.update(unknown=True)),
    ("missing-budget", lambda p: p.pop("maxFirings")),
    ("wrong-member-name", lambda p: p["site"].update(memberName="AnotherMethod")),
]:
    plan = copy.deepcopy(BASE)
    mutate(plan)
    candidate = OUTPUT / (case + ".json")
    candidate.write_text(json.dumps(plan), encoding="utf-8")
    assert command(["dotnet", str(PLANNER), "verify", str(ASSEMBLY), str(SOURCE), str(LINE), str(candidate)],
                   expected=None).returncode != 0
    rows, events, log = execute(case, plan)
    assert not events and all(row["error"] is None for row in rows)
    assert "plan rejected" in log

duplicate = '{"version":3,' + json.dumps(BASE)[1:]
candidate = OUTPUT / "duplicate.json"
candidate.write_text(duplicate, encoding="utf-8")
assert command(["dotnet", str(PLANNER), "verify", str(ASSEMBLY), str(SOURCE), str(LINE), str(candidate)],
               expected=None).returncode != 0
_, events, log = execute("duplicate-key", BASE, raw=duplicate)
assert not events and "plan rejected" in log
duplicate_environment = json.dumps(BASE).replace(
    '"CHAOS_TEST_CASE": "enabled"', '"CHAOS_TEST_CASE": "enabled", "CHAOS_TEST_CASE": "enabled"')
candidate = OUTPUT / "duplicate-environment.json"
candidate.write_text(duplicate_environment, encoding="utf-8")
assert command(["dotnet", str(PLANNER), "verify", str(ASSEMBLY), str(SOURCE), str(LINE), str(candidate)],
               expected=None).returncode != 0
_, events, log = execute("duplicate-environment-key", BASE, raw=duplicate_environment)
assert not events and "plan rejected" in log
changed_source = OUTPUT / "Program.cs"
changed_source.write_bytes(SOURCE.read_bytes() + b"\n// changed after deployment\n")
assert command(["dotnet", str(PLANNER), "verify", str(ASSEMBLY), str(changed_source), str(LINE), str(base_file)],
               expected=None).returncode != 0
results.append({"case": "changed-source-rejected", "passed": True})
(OUTPUT / "summary.json").write_text(json.dumps(results, indent=2), encoding="utf-8")
print(json.dumps({"passed": len(results), "evidence": str(OUTPUT)}))
