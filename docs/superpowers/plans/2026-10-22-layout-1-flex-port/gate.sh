#!/bin/zsh
# usage: GATE_OUT=<dir> gate.sh <worktree> <label>
# writes <dir>/<label>-failures.txt (failing test names), <label>-warnings.txt (non-external), <label>-build.log
set -u
S=${GATE_OUT:?set GATE_OUT to an output directory}; mkdir -p $S
W=$1; L=$2
export DOTNET_ROOT=$HOME/.dotnet PATH=$HOME/.dotnet:$PATH
cd $W
dotnet build AcDream.slnx -c Release 2>&1 | tee $S/$L-build.log | grep -E "Warn|Error|error" | tail -3
grep -E "warning [A-Z]+[0-9]+" $S/$L-build.log | grep -v "/external/" | sed -E 's/ \[.*//' | sort -u > $S/$L-warnings.txt
rm -rf $S/$L-trx; mkdir -p $S/$L-trx
for p in AcDream.App.Tests AcDream.Core.Tests AcDream.Plugin.Tests AcDream.UI.Abstractions.Tests; do
  dotnet test tests/$p -c Release --no-build --logger "trx;LogFileName=$p.trx" --results-directory $S/$L-trx >/dev/null 2>&1
done
python3 -I - "$S/$L-trx" > $S/$L-failures.txt <<'PY'
import sys,glob,xml.etree.ElementTree as ET
ns={'t':'http://microsoft.com/schemas/VisualStudio/TeamTest/2010'}
names=set()
for f in glob.glob(sys.argv[1]+'/*.trx'):
    for r in ET.parse(f).getroot().iterfind('.//t:UnitTestResult',ns):
        if r.get('outcome')=='Failed': names.add(r.get('testName'))
print('\n'.join(sorted(names)))
PY
echo "warnings(non-external): $(wc -l < $S/$L-warnings.txt)  failures: $(wc -l < $S/$L-failures.txt)"
git -C $W checkout -q -- '*.lock.json' 2>/dev/null
