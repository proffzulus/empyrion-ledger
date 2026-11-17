"""Synthetic publication-boundary and source-completeness checks; no game needed."""
import importlib.util, pathlib, unittest, sys, xml.etree.ElementTree as ET
ROOT=pathlib.Path(__file__).resolve().parents[1]
spec=importlib.util.spec_from_file_location("source_audit",ROOT/"scripts/audit-source.py")
audit=importlib.util.module_from_spec(spec);spec.loader.exec_module(audit)
class SourceTests(unittest.TestCase):
 def test_release_version(self):
  self.assertIn('AssemblyVersion("1.0.0.0")',(ROOT/"src/TradeLedgerProbeB/AssemblyInfo.cs").read_text())
  self.assertIn('AssemblyFileVersion("1.0.0.0")',(ROOT/"src/TradeLedgerProbeB/AssemblyInfo.cs").read_text())
  self.assertIn("Version: 1.0\n",(ROOT/"src/TradeLedgerProbeB/TradeLedger_Info.yaml").read_text())
  self.assertIn("version='1.0.0'",(ROOT/"scripts/build.ps1").read_text())
  for rel in ["README.md","docs/release/README.md"]:self.assertIn("**Version:** 1.0 ",(ROOT/rel).read_text())
 def test_public_owner_identity(self):
  self.assertEqual((ROOT/"LICENSE").read_text().splitlines()[2],"Copyright (c) 2026 proffzulus")
  self.assertIn("Author: proffzulus",(ROOT/"src/TradeLedgerProbeB/TradeLedger_Info.yaml").read_text())
  for rel in ["README.md","docs/release/README.md"]:
   self.assertIn("Made by **proffzulus**.",(ROOT/rel).read_text())
  for rel in ["THIRD-PARTY-NOTICES.md","docs/release/RELEASE-NOTICES.md"]:
   self.assertIn("MIT, copyright 2026 proffzulus.",(ROOT/rel).read_text())
 def test_package_owner_guard(self):
  spec=importlib.util.spec_from_file_location("release",ROOT/"scripts/prepare-trade-ledger-release.py")
  release=importlib.util.module_from_spec(spec);spec.loader.exec_module(release)
  text=(ROOT/"docs/release/RELEASE-NOTICES.md").read_text()
  release.validate_owner_notice(text)
  release.validate_owner_notice(text+"\nThird-party copyright attribution is preserved.\n")
  with self.assertRaises(AssertionError):release.validate_owner_notice(text.replace("proffzulus","example-author"))
 def test_plain_source(self): self.assertGreater(audit.inspect("README.md",b"# Source only"),0)
 def test_forbidden_directories(self):
  for p in ["Research/output.json","logs/session.txt","data/records.json","blueprints/source.cs","artifacts/file.cs"]:
   with self.subTest(path=p),self.assertRaises(ValueError):audit.inspect(p,b"{}")
 def test_binary_and_personal_extensions(self):
  for p in ["game.dll","records.sqlite","private.epb","private.bpyaml","picture.png","config.env"]:
   with self.subTest(path=p),self.assertRaises(ValueError):audit.inspect(p,b"not for publication")
 def test_secrets(self):
  for value in [("gh"+"p_"+"A"*36),("AK"+"IA"+"A"*16),("-----BEGIN "+"PRIVATE KEY-----")]:
   with self.assertRaises(ValueError):audit.inspect("source.cs",value.encode())
 def test_machine_path(self):
  with self.assertRaises(ValueError):audit.inspect("source.cs",("X"+":/"+"Users/example").encode())
 def test_observed_data(self):
  with self.assertRaises(ValueError):audit.inspect("source.json",b'{"WorldKey":"not public state"}')
 def test_only_owned_settings_yaml(self):
  for p in ["private/TradeLedger.settings","my.yaml"]:
   with self.assertRaises(ValueError):audit.inspect(p,b"value")
 def test_project_source_completeness(self):
  ns={"m":"http://schemas.microsoft.com/developer/msbuild/2003"}
  for project in (ROOT/"src").rglob("*.csproj"):
   for tag in ["Compile","EmbeddedResource"]:
    for node in ET.parse(project).findall(".//m:"+tag,ns):
     name=node.attrib["Include"]
     if "$(" in name:continue
     with self.subTest(project=project.name,input=name):self.assertTrue((project.parent/name.replace("\\","/")).is_file())
 def test_game_and_research_not_tracked(self):
  import subprocess
  names=subprocess.check_output(["git","-C",str(ROOT),"ls-files","-z"]).decode().split("\0")
  for name in filter(None,names):self.assertNotIn(pathlib.PurePosixPath(name).suffix,{".dll",".sqlite",".epb",".png",".nupkg"})
if __name__=="__main__":unittest.main(testRunner=unittest.TextTestRunner(stream=sys.stdout))
