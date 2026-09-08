#!/usr/bin/env python3
"""Exercise the production edge configuration in disposable, loopback-only containers."""
import http.client
import json
import os
from pathlib import Path
import subprocess
import tempfile
import time
import uuid

ROOT = Path(__file__).resolve().parents[1]
PROJECT = 'pl-edge-test-' + uuid.uuid4().hex[:8]
IMAGE = os.environ.get('PL_CADDY_TEST_IMAGE', 'pocketledger-caddy')
ENV = dict(os.environ, CROWDSEC_API_KEY='isolated-test-key-not-for-production')
COMPOSE = ['docker', 'compose', '-p', PROJECT, '--env-file', str(ROOT / '.env.example'), '-f', str(ROOT / 'compose.yaml')]
CONTAINERS = []


def run(*args, capture=True):
    result = subprocess.run(args, cwd=ROOT, env=ENV, text=True, stdout=subprocess.PIPE if capture else None, stderr=subprocess.PIPE if capture else None)
    if result.returncode:
        raise RuntimeError(f'{args}:\n{result.stdout}\n{result.stderr}')
    return result.stdout or ''


def compose(*args):
    return run(*COMPOSE, *args)


def cs(*args):
    return compose('exec', '-T', 'crowdsec', 'cscli', *args)


def wait_for(check, timeout=90):
    deadline = time.monotonic() + timeout
    while time.monotonic() < deadline:
        try:
            if check():
                return
        except (RuntimeError, OSError):
            pass
        time.sleep(1)
    raise AssertionError('Timed out waiting for condition')


def container(name, *args):
    name = PROJECT + '-' + name
    CONTAINERS.append(name)
    run('docker', 'run', '-d', '--name', name, '--network', PROJECT + '_default', *args)
    return name


try:
    compose('config', '--quiet')
    modules = run('docker', 'run', '--rm', IMAGE, 'caddy', 'list-modules').splitlines()
    assert {'crowdsec', 'http.handlers.crowdsec', 'http.handlers.rate_limit'} <= set(modules)
    assert run('docker', 'run', '--rm', IMAGE, 'caddy', 'version').startswith('v2.11.4 ')
    compose('up', '-d', 'crowdsec')
    wait_for(lambda: 'successfully interact' in cs('lapi', 'status') or bool(cs('collections', 'list', '-o', 'json')))
    assert 'crowdsecurity/caddy' in cs('collections', 'list', '-o', 'json')
    assert 'pocketledger_caddy' in cs('bouncers', 'list', '-o', 'json')
    compose('exec', '-T', 'crowdsec', 'crowdsec', '-t')
    print('PASS Compose, modules, Caddy version, CrowdSec config/collection/bootstrap', flush=True)

    with tempfile.TemporaryDirectory(prefix='pl-edge-') as directory:
        config = Path(directory) / 'Caddyfile'
        production = (ROOT / 'Caddyfile.example').read_text()
        domains = {'LANDING': 'landing.test', 'WEB': 'web.test', 'API': 'api.test', 'IDENTITY': 'identity.test'}
        env_args = ['-e', 'CROWDSEC_API_KEY=' + ENV['CROWDSEC_API_KEY']]
        for key, value in domains.items():
            env_args += ['-e', 'POCKETLEDGER_' + key + '_DOMAIN=' + value]
        config.write_text(production)
        common = ['--network', PROJECT + '_default', *env_args, '-v', str(config) + ':/etc/caddy/Caddyfile:ro', '-v', PROJECT + '_caddy-logs:/var/log/caddy']
        run('docker', 'run', '--rm', *common, IMAGE, 'caddy', 'validate', '--config', '/etc/caddy/Caddyfile')
        print('PASS production Caddyfile validation with actual custom image', flush=True)

        for service, port in [('landing', 5053), ('web', 5050), ('api', 5051), ('identity', 5052)]:
            container(service, '--network-alias', service, IMAGE, 'caddy', 'respond', '--listen', ':' + str(port), '--body', service)
        site = '{$POCKETLEDGER_LANDING_DOMAIN}, {$POCKETLEDGER_WEB_DOMAIN}, {$POCKETLEDGER_API_DOMAIN}, {$POCKETLEDGER_IDENTITY_DOMAIN}'
        local = production.replace(site + ' {', ':8080 {').replace('{\n', '{\n\tauto_https off\n', 1)
        config.write_text(local)
        proxy = container('proxy', *env_args, '-p', '127.0.0.1::8080', '-v', str(config) + ':/etc/caddy/Caddyfile:ro', '-v', PROJECT + '_caddy-logs:/var/log/caddy', IMAGE)
        port = int(run('docker', 'port', proxy, '8080/tcp').strip().rsplit(':', 1)[1])

        def request(host='landing.test', method='GET', ip=None, xff=None):
            headers = {'Host': host, 'User-Agent': 'PocketLedgerSecurityVerification/1.0'}
            if ip:
                headers['CF-Connecting-IP'] = ip
            if xff:
                headers['X-Forwarded-For'] = xff
            connection = http.client.HTTPConnection('127.0.0.1', port, timeout=5)
            connection.request(method, '/', headers=headers)
            response = connection.getresponse()
            result = response.status, response.read().decode()
            connection.close()
            return result

        wait_for(lambda: request()[0] == 200)
        for service in ['landing', 'web', 'api', 'identity']:
            assert request(service + '.test') == (200, service)
        request(ip='8.8.4.4', xff='1.1.1.1')
        entry = json.loads(run('docker', 'exec', proxy, 'tail', '-n', '1', '/var/log/caddy/access.log'))
        assert entry['request']['client_ip'] == entry['request']['remote_ip']
        assert entry['request']['client_ip'] not in ['8.8.4.4', '1.1.1.1']
        print('PASS all four routes and untrusted forwarded-header spoof rejection', flush=True)

        # Only the disposable fixture trusts the container gateway to simulate a CDN.
        config.write_text(local.replace('trusted_proxies static ', 'trusted_proxies static ' + entry['request']['remote_ip'] + ' '))
        run('docker', 'exec', proxy, 'caddy', 'reload', '--config', '/etc/caddy/Caddyfile')
        assert request(ip='8.8.4.4', xff='1.1.1.1')[0] == 200
        entry = json.loads(run('docker', 'exec', proxy, 'tail', '-n', '1', '/var/log/caddy/access.log'))
        assert entry['request']['client_ip'] == '8.8.4.4'
        for i in range(600):
            assert request(['landing.test', 'web.test'][i % 2], ip='11.12.13.14')[0] == 200, i
        assert request('api.test', ip='11.12.13.14')[0] == 429
        for _ in range(30):
            assert request('identity.test', 'POST', '11.12.13.15')[0] == 200
        assert request('identity.test', 'POST', '11.12.13.15')[0] == 429
        assert request('identity.test', 'GET', '11.12.13.15')[0] == 200
        assert request('web.test', 'POST', '11.12.13.15')[0] == 200
        for i in range(30):
            assert request('identity.test', 'POST', f'2001:4860:1234:5678::{i + 1:x}')[0] == 200
        assert request('identity.test', 'POST', '2001:4860:1234:5678::ffff')[0] == 429
        assert request('identity.test', 'POST', '2001:4860:1234:5679::1')[0] == 200
        print('PASS trusted client IP, shared 600 limit, Identity 30 POST limit, IPv6 /64 isolation', flush=True)

        cs('decisions', 'add', '--ip', '11.12.13.16', '--duration', '1m', '--reason', 'isolated-test')
        wait_for(lambda: request(ip='11.12.13.16')[0] == 403, 35)
        cs('decisions', 'delete', '--ip', '11.12.13.16')
        wait_for(lambda: request(ip='11.12.13.16')[0] == 200, 35)
        print('PASS LAPI decision propagation, HTTP ban and removal', flush=True)

        # Replay bounded synthetic access logs; no flood is sent to any HTTP server.
        now = time.time()
        records = []
        for ip in ['11.12.13.17', '173.245.48.1']:
            for i in range(1200):
                records.append(json.dumps({'level': 'info', 'ts': now + i / 10000, 'logger': 'http.log.access', 'request': {'remote_ip': '173.245.48.1', 'client_ip': ip, 'proto': 'HTTP/1.1', 'method': 'GET', 'host': 'landing.test', 'uri': '/', 'headers': {'User-Agent': ['PocketLedgerSecurityVerification/1.0']}}, 'status': 429, 'size': 0, 'duration': 0.001}))
        fixture = Path(directory) / 'flood.json'
        fixture.write_text('\n'.join(records) + '\n')
        run('docker', 'cp', str(fixture), proxy + ':/tmp/flood.json')
        run('docker', 'exec', proxy, 'sh', '-c', 'cat /tmp/flood.json >> /var/log/caddy/access.log')
        wait_for(lambda: '11.12.13.17' in cs('decisions', 'list', '-o', 'json'), 45)
        decisions = json.loads(cs('decisions', 'list', '-o', 'json'))
        serialized = json.dumps(decisions)
        assert 'pocketledger/http-flood' in serialized
        assert '173.245.48.1' not in serialized
        print('PASS cold-start log acquisition, 429 flood alert/decision, Cloudflare edge exclusion', flush=True)
        print(cs('alerts', 'list'))
        print(cs('decisions', 'list'))
        print(cs('metrics'))
        compose('up', '-d', '--force-recreate', 'crowdsec')
        wait_for(lambda: '11.12.13.17' in cs('decisions', 'list', '-o', 'json'))
        assert 'pocketledger_caddy' in cs('bouncers', 'list', '-o', 'json')
        print('PASS CrowdSec recreation preserves credentials, bouncer and decisions', flush=True)
finally:
    for name in reversed(CONTAINERS):
        subprocess.run(['docker', 'rm', '-f', name], stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)
    subprocess.run([*COMPOSE, 'down', '--volumes'], cwd=ROOT, env=ENV, stdout=subprocess.DEVNULL)
