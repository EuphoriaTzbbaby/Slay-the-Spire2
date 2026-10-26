"""Offline repository and common message-field checks (stdlib only)."""
import configparser
import json
from pathlib import Path

root = Path(__file__).resolve().parents[1]
for directory in ['game', 'voice', 'protocol/schemas', 'protocol/examples', 'docs', 'scripts', '.github/workflows']:
    if not (root / directory).is_dir():
        raise SystemExit(f'Missing directory: {directory}')
modules = configparser.ConfigParser()
modules.read(root / '.gitmodules')
for name in ['spire-buddy', 'STS2-Agent', 'CombatSolver']:
    section = modules[f'submodule "{name}"']
    if section['path'] != f'game/{name}' or not section['url'].startswith('https://github.com/'):
        raise SystemExit(f'Invalid submodule mapping: {name}')
json.loads((root / 'protocol/schemas/message.schema.json').read_text(encoding='utf-8'))
for path in (root / 'protocol/examples').glob('*.json'):
    value = json.loads(path.read_text(encoding='utf-8'))
    assert value['version'] == 1, path
    kind = value['kind']
    if kind == 'request':
        assert value['id'] and value['method'] and isinstance(value['params'], dict), path
    elif kind == 'response':
        assert value['id'] and type(value['ok']) is bool, path
        field = 'result' if value['ok'] else 'error'
        other = 'error' if value['ok'] else 'result'
        assert isinstance(value[field], dict) and other not in value, path
        if not value['ok']:
            assert isinstance(value['error']['code'], str), path
            assert isinstance(value['error']['message'], str), path
            assert type(value['error']['retryable']) is bool, path
    elif kind == 'event':
        assert value['event_id'] and value['session_id'] and value['name'], path
        assert type(value['seq']) is int and value['seq'] >= 0 and isinstance(value['data'], dict), path
    else:
        raise SystemExit(f'Unknown message kind: {path}')
print('Repository layout, submodule mappings and protocol examples: OK')
