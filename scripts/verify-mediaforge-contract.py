"""Execute real 1.6.0 AutoSync/auth/DB functions against a temporary SQLite DB.
No web server, provider network call or download worker is started.
"""
import argparse
import ast
import json
import os
import sqlite3
import sys
import types
from pathlib import Path
from functools import wraps
from flask import jsonify, request, session, redirect, url_for

root = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(root / 'Tests'))
from test_workflow import AutosyncTests

parser = argparse.ArgumentParser(description=__doc__)
parser.add_argument('--source', type=Path, required=True, help='MediaForge v1.6.0 source checkout')
core = parser.parse_args().source.resolve() / 'src/mediaforge/web'

def load_nodes(relative, names, namespace):
    tree = ast.parse((core / relative).read_text(encoding='utf-8'))
    nodes = [n for n in tree.body if isinstance(n, (ast.FunctionDef, ast.Assign))
             and (getattr(n, 'name', None) in names
                  or isinstance(n, ast.Assign) and any(isinstance(t, ast.Name) and t.id in names for t in n.targets))]
    exec(compile(ast.Module(body=nodes, type_ignores=[]), str(core / relative), 'exec'), namespace)

env = AutosyncTests()
env.setUp()
try:
    db_path = Path(env.temp.name) / 'core.sqlite3'
    def get_db():
        conn = sqlite3.connect(db_path)
        conn.row_factory = sqlite3.Row
        return conn
    ns = dict(get_db=get_db, jsonify=jsonify, request=request, session=session,
              wraps=wraps, redirect=redirect, url_for=url_for, json=json, os=os)
    load_nodes('db/autosync.py', {'_CREATE_AUTOSYNC_TABLE','add_autosync_job','get_autosync_job','find_autosync_by_url'}, ns)
    with get_db() as db:
        db.execute(ns['_CREATE_AUTOSYNC_TABLE'])
        db.execute('CREATE UNIQUE INDEX test_url_unique ON autosync_jobs(series_url)')
    env.db.get_autosync_job = ns['get_autosync_job']
    env.db.find_autosync_by_url = ns['find_autosync_by_url']
    load_nodes('auth.py', {'login_required','adult_required','get_current_user'}, ns)
    load_nodes('autosync_worker.py', {'_normalize_episode_filter'}, ns)
    load_nodes('routes/autosync.py', {'_language_group_error','_normalize_extra_languages'}, ns)
    baseline = []
    class Thread:
        def __init__(self, *, target, args, kwargs, daemon):
            self.job, self.options = args[0], kwargs
        def start(self):
            baseline.append((self.job['id'], self.options))
    ns.update(get_setting=lambda key: None, is_group_ref=lambda value: False,
              _get_current_user_info=lambda: (None, False),
              threading=types.SimpleNamespace(Thread=Thread), _run_autosync_for_job=None)
    tree = ast.parse((core/'routes/autosync.py').read_text(encoding='utf-8'))
    registrar = next(n for n in tree.body if isinstance(n, ast.FunctionDef) and n.name == 'register_autosync_routes')
    create = next(n for n in registrar.body if isinstance(n, ast.FunctionDef) and n.name == 'api_autosync_create')
    create.decorator_list = []
    exec(compile(ast.Module(body=[create],type_ignores=[]), str(core/'routes/autosync.py'),'exec'),ns)
    raw = ns['api_autosync_create']
    env.app.secret_key = 'isolated-test-only'
    env.app.extensions['mediaforge_raw_views'] = {'api_autosync_create': raw}
    env.app.view_functions['api_autosync_create'] = ns['login_required'](ns['adult_required'](raw))
    for _ in range(2):
        result = env.post()
        assert result.status_code == 200, result.get_json()
        assert result.get_json()['job_id'] == 1
    job = ns['get_autosync_job'](1)
    assert job['enabled'] == 1 and job['custom_path_id'] == 11
    assert job['episode_filter'] is None and job['last_check']
    assert baseline == [(1, {'queue_downloads': False})]
    assert env.download_calls == 0
    assert env.post(key='library:read-key').status_code == 401
    print('Real MediaForge v1.6.0 route + auth wrappers + SQLite: one persisted job; silent initial baseline; no downloads; invalid scope denied.')
finally:
    env.doCleanups()
