"""Behavioral regressions for subscriptions and crash-safe handoffs."""
import importlib.util
import json
import sys
import tempfile
import types
import unittest
from concurrent.futures import ThreadPoolExecutor
from pathlib import Path

from flask import Blueprint, Flask, jsonify, request
from test_mediaforge_module import _load_routes_module

MODULE = Path(__file__).parents[1] / "MediaForge.Module/marshmello_jellyfin_connector"
spec = importlib.util.spec_from_file_location("connector_operations_test", MODULE / "operations.py")
operations = importlib.util.module_from_spec(spec)
spec.loader.exec_module(operations)


class LedgerTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        self.path = Path(self.temp.name) / "receipts.db"
        self.ledger = operations.OperationLedger(self.path)
        self.body = {"title": "Series", "series_url": "https://example/series", "episodes": ["ep1"], "language": "de", "provider": "VOE", "upscale": False}

    def test_reservation_survives_restart_and_does_not_repeat_write(self):
        self.assertEqual(self.ledger.reserve("a", self.body, 10), ("new", None))
        restarted = operations.OperationLedger(self.path)
        self.assertEqual(restarted.reserve("a", self.body), ("uncertain", None))
        restarted.confirm("a", 11)
        self.assertEqual(self.ledger.reserve("a", self.body), ("confirmed", 11))
        self.assertEqual(self.ledger.reserve("a", dict(self.body, language="en")), ("conflict", None))

    def test_concurrent_reservations_have_one_winner(self):
        with ThreadPoolExecutor(max_workers=8) as pool:
            results = list(pool.map(lambda _: self.ledger.reserve("a", self.body), range(16)))
        self.assertEqual(sum(state == "new" for state, _ in results), 1)

    def test_reconciliation_requires_one_exact_post_reservation_match(self):
        self.ledger.reserve("a", self.body, 10)
        row = dict(self.body, id=11, episodes=json.dumps(["ep1"]))
        self.assertEqual(self.ledger.reconcile("a", [dict(row, id=9)])["state"], "uncertain")
        self.assertEqual(self.ledger.reconcile("a", [dict(row, language="en")])["state"], "uncertain")
        self.assertEqual(self.ledger.reconcile("a", [row, dict(row, id=12)])["state"], "uncertain")
        self.assertEqual(self.ledger.reconcile("a", [row]), {"state": "confirmed", "queue_id": 11})


class AutosyncTests(unittest.TestCase):
    modern = True

    def test_official_connector_can_coexist_without_shadowing_fork(self):
        official = Blueprint('mediaforge_jellyfin_connector', __name__)
        official.add_url_rule('/api/v1/connector/health', 'api_connector_health',
                              lambda: jsonify({'module': 'official', 'version': '0.4.3'}))
        official.add_url_rule('/api/v1/connector/autosync', 'api_connector_autosync',
                              lambda: jsonify({'error': 'official handler reached'}), methods=['POST'])
        self.app.register_blueprint(official)
        response = self.post()
        self.assertEqual(200, response.status_code)
        self.assertEqual(1, len(self.creates))
        self.assertEqual('official', self.client.get('/api/v1/connector/health').get_json()['module'])
        fork_rules = [r for r in self.app.url_map.iter_rules()
                      if r.endpoint.startswith('marshmello_jellyfin_connector.')]
        self.assertTrue(fork_rules)
        self.assertTrue(all(r.rule.startswith('/api/v1/marshmello-connector/') for r in fork_rules))

    def setUp(self):
        self.routes = _load_routes_module(modern=self.modern)
        self.temp = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        config = types.ModuleType("mediaforge.config")
        config.MEDIAFORGE_CONFIG_DIR = Path(self.temp.name)
        sys.modules[config.__name__] = config
        sys.modules["mediaforge.web.thirdparties.marshmello_jellyfin_connector"].__path__ = [str(MODULE)]
        self.db = sys.modules["mediaforge.web.db"]
        self.jobs = {}
        self.queue = []
        self.creates = []
        self.is_movie = False
        self.omit_movie_flag = True
        self.metadata_error = False
        self.adult = False
        self.enabled = True
        self.download_calls = 0
        self.db.get_queue = lambda: self.queue
        self.db.find_autosync_by_url = lambda url: next((j for j in self.jobs.values() if j["series_url"].rstrip('/').lower() == url.rstrip('/').lower()), None)
        self.db.get_autosync_job = lambda job_id: self.jobs.get(job_id)
        self.app = Flask(__name__)
        for endpoint in self.routes._ROUTE_NAMES.values():
            self.app.add_url_rule('/internal/' + endpoint, endpoint, self.handler(endpoint), methods=['GET', 'POST'])

        def create():
            data = dict(request.get_json())
            self.creates.append(data)
            job = dict(data, id=1, enabled=1)
            self.jobs[1] = job
            return jsonify({"id": 1, "ok": True})

        self.app.add_url_rule('/internal/autosync', 'api_autosync_create', create, methods=['POST'])
        bp, self.scopes = self.routes.create_blueprint(self.app, 'enabled', 'test')
        self.app.register_blueprint(bp)
        self.client = self.app.test_client()
        self.body = {"title": "Series", "series_url": "https://allowed.invalid/media/series", "language": "German Dub", "provider": "VOE"}

    def handler(self, endpoint):
        def view():
            if endpoint == 'api_search_sources':
                return jsonify({"sources": [{"id": "aniworld", "adult": self.adult, "enabled": self.enabled}]})
            if endpoint == 'api_series':
                if self.metadata_error:
                    return jsonify({"error": "private upstream message"})
                metadata = {"title": "Series"}
                if self.is_movie or not self.omit_movie_flag:
                    metadata['is_movie'] = self.is_movie
                return jsonify(metadata)
            if endpoint == 'api_download':
                self.download_calls += 1
                self.queue.append(dict(request.get_json(), id=42))
                return jsonify({"queue_id": 42})
            return jsonify({})
        return view

    def post(self, body=None, key='queue:write-key'):
        return self.client.post('/api/v1/marshmello-connector/autosync', json=body or self.body, headers={'X-Api-Key': key})

    def test_create_and_repeat_keep_one_job_and_default_path(self):
        self.assertEqual(self.post().status_code, 200)
        self.assertEqual(self.post().get_json()['created'], False)
        self.assertEqual(len(self.creates), 1)
        self.assertEqual(self.creates[0]['custom_path_id'], 11)
        self.assertEqual(self.download_calls, 0)

    def test_existing_paused_filtered_job_is_never_modified_or_exposed(self):
        self.jobs[7] = dict(self.body, id=7, enabled=0, episode_filter='{"seasons":[1]}', custom_path_id=99, added_by='private-user')
        result = self.post().get_json()
        self.assertFalse(result['enabled'])
        self.assertTrue(result['filtered'])
        self.assertEqual(self.jobs[7]['custom_path_id'], 99)
        self.assertEqual(self.creates, [])
        self.assertNotIn('private-user', json.dumps(result))

    def test_explicit_series_flag_and_invalid_metadata(self):
        self.omit_movie_flag = False
        self.assertEqual(self.post().status_code, 200)
        self.jobs.clear()
        self.metadata_error = True
        self.assertEqual(self.post().status_code, 400)
        self.assertEqual(len(self.creates), 1)

    def test_scopes_movies_source_policy_and_injected_paths(self):
        self.assertEqual(self.post(key='library:read-key').status_code, 401)
        self.assertEqual(self.post(dict(self.body, custom_path_id=88)).status_code, 400)
        self.is_movie = True
        self.assertEqual(self.post().status_code, 400)
        self.is_movie = False
        self.adult = True
        self.assertEqual(self.post().status_code, 403)
        self.adult = False
        self.enabled = False
        self.assertEqual(self.post().status_code, 403)
        self.assertEqual(self.creates, [])

    def test_download_receipt_replays_without_second_download(self):
        body = dict(self.body, episodes=['https://allowed.invalid/media/episode1'], upscale=False, operation_id='a' * 32)
        for _ in range(2):
            response = self.client.post('/api/v1/marshmello-connector/download', json=body, headers={'X-Api-Key': 'queue:write-key'})
            self.assertEqual(response.status_code, 200)
            self.assertEqual(response.get_json()['queue_id'], 42)
        self.assertEqual(self.download_calls, 1)
        response = self.client.get('/api/v1/marshmello-connector/operations/' + 'a' * 32, headers={'X-Api-Key': 'queue:read-key'})
        self.assertEqual(response.get_json()['state'], 'confirmed')

    def test_concurrent_autosync_creates_keep_one_job(self):
        def submit(_):
            with self.app.test_client() as client:
                return client.post('/api/v1/marshmello-connector/autosync', json=self.body, headers={'X-Api-Key': 'queue:write-key'}).status_code
        with ThreadPoolExecutor(max_workers=4) as pool:
            self.assertEqual(list(pool.map(submit, range(8))), [200] * 8)
        self.assertEqual(len(self.creates), 1)


class LegacyAutosyncTests(AutosyncTests):
    modern = False


if __name__ == '__main__':
    unittest.main()
