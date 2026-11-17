"""Audit tracked/staged source and every reachable commit tree before publication.
No game/source mutation, no credential reads; failures report rule/path, not values.
"""
import argparse, json, pathlib, re, subprocess, sys
ROOT = pathlib.Path(__file__).resolve().parents[1]
DENY_PARTS = {"research", "blueprints", "logs", "data", "saves", "artifacts", "bin", "obj", ".vs", "__pycache__"}
EXT = {".cs", ".csproj", ".json", ".ps1", ".py", ".md", ".txt", ".yaml"}
SPECIAL = {"LICENSE", ".gitignore", ".gitattributes", "TradeLedger.settings"}
RULES = {
 "private-key": re.compile(r"-----BEGIN (?:RSA |EC |OPENSSH )?PRIVATE KEY-----"),
 "github-token": re.compile(r"(?:gh[pousr]_[A-Za-z0-9]{30,}|github_pat_[A-Za-z0-9_]{40,})"),
 "aws-access-key": re.compile(r"(?:AKIA|ASIA)[A-Z0-9]{16}"),
 "credential-url": re.compile(r"https?://[^/\s:@]+:[^/\s@]+@"),
 "machine-absolute-path": re.compile(r"(?i)\b[A-Z]:[\\/]"),
 "private-workspace-data": re.compile(r"(?i)(?:Re2Fresh-\d{6}|TradeLedgerProbe-\d{8}T\d{9}|Sentinel_[a-f0-9]{20,}|libfile_[a-f0-9]{20,})"),
}
def git(*args):
 return subprocess.check_output(["git", "-C", str(ROOT), *args])
def inspect(name, blob):
 path=pathlib.PurePosixPath(name)
 if any(x.casefold() in DENY_PARTS for x in path.parts): raise ValueError("excluded-directory: "+name)
 if path.name not in SPECIAL and path.suffix not in EXT: raise ValueError("non-source-file: "+name)
 if path.name=="TradeLedger.settings" and name!="src/TradeLedgerProbeB/TradeLedger.settings": raise ValueError("unexpected-settings: "+name)
 if path.suffix==".yaml" and name!="src/TradeLedgerProbeB/TradeLedger_Info.yaml": raise ValueError("unexpected-yaml: "+name)
 if len(blob)>3*1024*1024 or b"\x00" in blob: raise ValueError("binary-or-oversize: "+name)
 text=blob.decode("utf-8-sig")
 for rule, pattern in RULES.items():
  if pattern.search(text): raise ValueError(rule+": "+name)
 if path.suffix==".json":
  data=json.loads(text)
  def visit(o):
   if isinstance(o,dict):
    if set(o)&{"WorldKey","LocalSaveKey","ObserverPlayer","NativePosition","SaveGame"}:raise ValueError("observed-state-json: "+name)
    for v in o.values():visit(v)
   elif isinstance(o,list):
    for v in o:visit(v)
  visit(data)
  if path.name=="NativeContracts5232.json":
   assert all(m["Body"] is None or re.fullmatch(r"[A-F0-9]{64}",m["Body"]) for m in data["Members"]),"native executable body is forbidden"
 return len(blob)
def main():
 ap=argparse.ArgumentParser();ap.add_argument("--history");ap.add_argument("--staged",action="store_true");a=ap.parse_args()
 files=0;size=0;commits=0;trees=0;seen=set()
 if a.history:
  revisions=git("rev-list","--reverse",a.history).decode().splitlines()
  for revision in revisions:
   commits+=1;trees+=1
   message=git("show","-s","--format=%B",revision).decode("utf-8")
   for rule,p in RULES.items():
    if p.search(message):raise ValueError("commit-message "+rule)
   for row in git("ls-tree","-rz","--full-tree",revision).split(b"\0"):
    if not row:continue
    head,name=row.split(b"\t",1);mode,kind,oid=head.split();name=name.decode("utf-8")
    if kind!=b"blob" or mode not in {b"100644",b"100755"}:raise ValueError("nonregular-tree-entry: "+name)
    size+=inspect(name,git("cat-file","blob",oid.decode()));files+=1;seen.add(name)
 else:
  paths=git("ls-files","-z","--cached").split(b"\0")
  for path in paths:
   if not path:continue
   name=path.decode("utf-8");blob=git("show",":"+name) if a.staged else (ROOT/name).read_bytes()
   size+=inspect(name,blob);files+=1;seen.add(name)
  if not paths or files==0:raise ValueError("no tracked source to audit; stage the allowlisted export first")
 print(json.dumps({"status":"PASS","commits":commits,"trees":trees,"entriesInspected":files,"uniqueFiles":len(seen),"bytesInspected":size,"mode":"history" if a.history else "staged" if a.staged else "tracked-worktree","limits":"pattern and schema audit, not a mathematical proof of absence of every possible secret; curated export and full tree review also required"},indent=2))
if __name__=="__main__":
 try:main()
 except Exception as e:print("SOURCE AUDIT FAILED: "+str(e),file=sys.stderr);sys.exit(1)
