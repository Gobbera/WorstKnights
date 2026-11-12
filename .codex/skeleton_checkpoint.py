from pathlib import Path
import datetime
import hashlib
import json
import shutil
import subprocess

root = Path.cwd().resolve()
stamp = datetime.datetime.now().strftime('%Y%m%d-%H%M%S')
destination = root / '.codex' / 'backups' / ('skeleton-variants-' + stamp)
destination.mkdir(parents=True, exist_ok=False)
sources = [
    'Assets/Scripts/Enemy',
    'Assets/Editor/Enemy',
    'Assets/Prefabs/Enemies',
    'Assets/WorstKnights/Models/Enemies/Skeleton',
    'Assets/WorstKnights/Models/Enemies/New Skeleton.meta',
    'ProjectSettings/ProjectVersion.txt',
    'ProjectSettings/TagManager.asset',
]
files = []
for relative in sources:
    path = root / relative
    if path.is_dir():
        files.extend(p for p in path.rglob('*') if p.is_file())
    elif path.is_file():
        files.append(path)
files.extend((root / 'Docs').glob('*Enemy*.md'))
manifest = {}
for source in sorted(set(files)):
    relative = source.relative_to(root)
    target = destination / relative
    target.parent.mkdir(parents=True, exist_ok=True)
    shutil.copy2(source, target)
    manifest[relative.as_posix()] = hashlib.sha256(source.read_bytes()).hexdigest()
(destination / 'manifest.json').write_text(json.dumps(manifest, indent=2), encoding='utf-8')
status = subprocess.run(['git', 'status', '--short'], capture_output=True, text=True, check=True)
(destination / 'git-status-before.txt').write_text(status.stdout, encoding='utf-8')
(root / '.codex' / 'skeleton-work-state.json').write_text(json.dumps({
    'checkpoint': str(destination),
    'asset_directory': 'Assets/WorstKnights/Models/Enemies/New Skeleton',
    'source_prefab': 'Assets/Prefabs/Enemies/Enemy.prefab',
}, indent=2), encoding='utf-8')
print(json.dumps({'checkpoint': str(destination), 'saved_file_count': len(manifest)}))
