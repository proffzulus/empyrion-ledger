"""Create a deterministic, allowlisted NEW release, never an old-DLL backup."""
import argparse, hashlib, json, re, shutil, zipfile
from pathlib import Path
ROOT=Path(__file__).resolve().parents[1]
STAGE=ROOT/'artifacts/release/stage/TradeLedger'
def sha(p): return hashlib.sha256(p.read_bytes()).hexdigest().upper()
def validate_owner_notice(text):
 # Only the project's own attribution is prescribed here; third-party names/rights remain intact.
 assert re.search(r'^MIT, copyright 2026 proffzulus\.',text,re.M), 'unexpected public project attribution'
 assert re.search(r'^Copyright \(c\) 2026 proffzulus$',text,re.M), 'unexpected public project MIT copyright'

def main():
 p=argparse.ArgumentParser();p.add_argument('--check',action='store_true');a=p.parse_args()
 m=json.loads((STAGE/'build-manifest.json').read_bytes())
 for name in ('README.md','RELEASE-NOTICES.md'):
  if not a.check: shutil.copyfile(ROOT/'docs/release'/name,STAGE/name)
 validate_owner_notice((STAGE/'RELEASE-NOTICES.md').read_text(encoding='utf-8-sig'))
 assert re.search(r'^Author: proffzulus$',(STAGE/'TradeLedger_Info.yaml').read_text(encoding='utf-8-sig'),re.M), 'unexpected public package author'
 files=['TradeLedger.dll','runtime/TradeLedger.Runtime.dll','runtime/0Harmony.dll','TradeLedger_Info.yaml','TradeLedger.settings','Harmony-LICENSE.txt','THIRD-PARTY-LICENSES.txt','README.md','RELEASE-NOTICES.md']
 actual={x.relative_to(STAGE).as_posix() for x in STAGE.rglob('*') if x.is_file()}
 assert actual==set(files+['build-manifest.json']),('unexpected package files',actual)
 for name in files:
  assert not (STAGE/name).is_symlink()
  b=(STAGE/name).read_bytes()
  for text in (b.decode('utf-8',errors='ignore'),b.decode('utf-16le',errors='ignore')):
   assert not re.search(r'[A-Za-z]:[\\/](?:Projects|Users)[\\/]',text),('personal path in package',name)
  assert not name.lower().endswith(('.sqlite','.db','.log','.pdb','.ecf','.csv'))
 release={k:m[k] for k in ('version','target','fingerprint','gameHashes','compilerVersion','publicRelease','nativeCertified','packageName','sqlite')}
 release.update(schema='TradeLedger.package.v1',files={n.replace('/','\\'):sha(STAGE/n) for n in files},ui={'referenceHashes':m['ui']['referenceHashes']},settingsDefaults={'RuntimeObservation':'on','Diagnostics':'Warn','Profiler':'off; session-only opt-in'},paths={'logs':'mod-relative logs','authority':'current guarded save/Mods/TradeLedger','privateCache':'game Saves/TradeLedgerCache'},publication='NOT UPLOADED; original code MIT, third-party/content rights in RELEASE-NOTICES.md; native certification is separate from live acceptance')
 # Only dependency facts are required by the installer, not prototype implementation prose.
 release['sqlite']={k:m['sqlite'][k] for k in ('provider','nativeRelative','nativeHash')}
 encoded=(json.dumps(release,ensure_ascii=True,sort_keys=True,indent=2)+'\n').encode()
 if a.check: assert (STAGE/'build-manifest.json').read_bytes()==encoded,'noncanonical release manifest'
 else: (STAGE/'build-manifest.json').write_bytes(encoded)
 build=release['target'].split()[0]
 assert build.isdigit(), 'invalid public build identity'
 suffix='' if release['nativeCertified'] else '-CANDIDATE'
 out=ROOT/f"artifacts/release/TradeLedger-{release['version']}-empyrion{build}-re2{suffix}.zip"
 def archive(dest):
  with zipfile.ZipFile(dest,'w',compression=zipfile.ZIP_DEFLATED,compresslevel=9) as z:
   entries={f'TradeLedger/{n}':STAGE/n for n in files+['build-manifest.json']}
   entries['Install-TradeLedger.ps1']=ROOT/'scripts/deploy-trade-ledger.ps1'
   for name,path in sorted(entries.items()):
    i=zipfile.ZipInfo(name,(1980,1,1,0,0,0));i.compress_type=zipfile.ZIP_DEFLATED;i.external_attr=0o100644<<16;z.writestr(i,path.read_bytes(),compresslevel=9)
 if not a.check: archive(out)
 import io
 memory=io.BytesIO();archive(memory)
 assert out.read_bytes()==memory.getvalue(),'archive not byte-reproducible'
 with zipfile.ZipFile(out) as z:
  assert z.testzip() is None
  assert len(z.namelist())==11
 record={'status':'PREPARATION ONLY' if not release['nativeCertified'] else 'NATIVE CERTIFIED; LIVE UNITY UNVERIFIED','version':release['version']+'.0','target':release['target'],'archive':str(out.relative_to(ROOT)),'sha256':sha(out),'bytes':out.stat().st_size,'entries':11,'payload':release['files'],'manifestSha256':sha(STAGE/'build-manifest.json'),'deterministicZip':True,'personalPathScan':True,'publicationHolds':['broader binary/content redistribution review; see RELEASE-NOTICES.md']+([] if release['nativeCertified'] else ['native certification']),'installed':False}
 (ROOT/'artifacts/release/verification/package-verification.json').write_text(json.dumps(record,indent=2)+'\n',encoding='utf-8')
 print(json.dumps(record,indent=2))
if __name__=='__main__':main()
