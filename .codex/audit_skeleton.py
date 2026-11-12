from pathlib import Path
import hashlib
import json

root = Path(__file__).resolve().parents[1]
state = json.loads((root / '.codex/skeleton-work-state.json').read_text(encoding='utf-8'))
checkpoint = Path(state['checkpoint'])
manifest = json.loads((checkpoint / 'manifest.json').read_text(encoding='utf-8'))
allowed = {
    'Assets/Scripts/Enemy/AI/EnemyAttackState.cs',
    'Assets/Scripts/Enemy/AI/EnemyBrain.cs',
    'Assets/Scripts/Enemy/AI/EnemyChaseState.cs',
    'Assets/Scripts/Enemy/Animation/EnemyAnimationController.cs',
    'Assets/Scripts/Enemy/Networking/EnemyNetworkSync.cs',
    'Assets/Scripts/Enemy/Setup/EnemySetup.cs',
}
changed, unexpected = [], []
for relative, expected in manifest.items():
    source = root / relative
    actual = hashlib.sha256(source.read_bytes()).hexdigest() if source.is_file() else None
    if actual != expected:
        changed.append(relative)
        if relative not in allowed:
            unexpected.append(relative)
report = {
    'checkpoint': str(checkpoint),
    'original_file_count': len(manifest),
    'unchanged_original_file_count': len(manifest) - len(changed),
    'expected_runtime_integration_changes': changed,
    'unexpected_original_changes': unexpected,
    'original_model_controller_prefabs_preserved': not any(
        p.startswith(('Assets/Prefabs/', 'Assets/WorstKnights/Models/')) for p in changed),
}
(root / '.codex/diagnostics/skeleton-source-preservation.json').write_text(json.dumps(report, indent=2), encoding='utf-8')
print(json.dumps(report, indent=2))
assert not unexpected
