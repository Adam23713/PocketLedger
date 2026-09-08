# Caddy and CrowdSec operations

## Architecture and limits

Cloudflare → Caddy (trusted client IP → CrowdSec HTTP bouncer → rate limiter) → existing landing:5053, web:5050, api:5051 and identity:5052 upstreams. Caddy JSON access logs feed CrowdSec's `crowdsecurity/caddy` collection and `pocketledger/http-flood` scenario. There is no host firewall bouncer: at that layer the peer is a Cloudflare edge, not the visitor.

One shared sliding-window limiter allows 600 requests per 60 seconds across all four HTTPS hosts. Identity POST requests additionally have a 30-per-60-second limit. Rejection is HTTP 429 with Retry-After. IPv4 is grouped per address, IPv6 per /64, using Caddy's `{client_ip}`. Shared NATs and IPv6 household networks share these budgets; tune only after inspecting legitimate traffic, especially login/refresh activity. Limits are in-memory for this single Caddy instance and reset on restart. They do not protect bandwidth, TLS handshakes or the automatic HTTP→HTTPS redirect. Keep Cloudflare's edge protection enabled.

The flood detector groups parsed `evt.Meta.source_ip` (Caddy `request.client_ip`) across hosts, includes 429s, and excludes 403s to avoid extending bans from already rejected requests. Its leaky bucket holds 1000 events and leaks one every 60 ms: **1000/minute is the sustained drain rate, not a fixed-window threshold**. A burst above capacity can trigger immediately; 2000/minute fills an empty bucket in about one minute, 1100/minute in about ten minutes. Traffic at or below 1000/minute does not steadily fill it. Rate-limited requests are still logged, so the 600 limiter does not prevent flood detection. Overflow creates a one-hour IP ban; other remediable IP alerts create four-hour bans. `blackhole: 5m` suppresses repeated overflows. CrowdSec bans individual IPv6 addresses; /64 rotation is constrained by Caddy, not a CrowdSec range ban.

## Client-IP trust boundary

Only the explicit Cloudflare IPv4/IPv6 ranges are trusted. `trusted_proxies_strict` is enabled. Only `CF-Connecting-IP` is accepted; there is deliberately no `X-Forwarded-For` fallback. An untrusted direct origin connection cannot choose its client IP using either header. Caddy's limiter and HTTP bouncer use the same native resolved IP.

Cloudflare must preserve CF-Connecting-IP. Disable **Pseudo IPv4 / Overwrite Headers** (Off or Add Header preserves the actual IPv6 identity); do not enable transforms that remove visitor-IP headers. Review any Workers that can change the visitor identity: trusting Cloudflare's networks also trusts the Cloudflare processing before this origin. A missing/invalid header falls back to the peer and can incorrectly share an edge's rate limit. The local `pocketledger/cloudflare-whitelist` parser prevents such edge addresses from generating CrowdSec decisions. It does not correct a broken header configuration, and manual decisions can still ban any address. Never manually ban Cloudflare networks.

Keep VPS firewall / `DOCKER-USER` rules restricted to Cloudflare origin-facing ranges on 80/443, including IPv6 and UDP/443 if used. Check Docker's published-port rules, not just a host INPUT policy. Consider Cloudflare Authenticated Origin Pulls to restrict origin access further. No host-firewall configuration exists in this repository, so this integration changes none. Compare both Caddy's trust list and the CrowdSec whitelist with the official ranges when maintaining them.

## Versions and storage

Caddy 2.11.4 builder/runtime and CrowdSec 1.8.1 images are pinned by multi-platform digest. `xcaddy build v2.11.4` explicitly pins the binary too. The rate limiter is pinned to `5625512f24f6f59d6f64fb3aafe5eecff0b286db`, the HTTP bouncer to v0.14.0. Update version and image digest together, rebuild, and rerun verification. Hub collections and enrichment data are downloaded at bootstrap and are **not immutable dependency locks**; first startup needs outbound access. Persistent `crowdsec-config` preserves installed Hub items. Review and test explicit Hub upgrades separately.

`caddy-logs` holds `/var/log/caddy/access.log`, writable by Caddy and read-only for CrowdSec. Default image users can share it without host chmod/chown. Rotation keeps up to five 100 MiB archives, at most seven days old, plus the active log. `force_inotify` watches the existing volume directory even before Caddy creates the first file. `crowdsec-config` contains credentials and installed Hub content; `crowdsec-data` contains decisions and the database. Existing `caddy-data` and `caddy-config` retain their names and certificates. Protect all these volumes and backups; logs include client IPs and URLs, which can include authentication query parameters. Do not enable `log_credentials` or publish logs.

CrowdSec's online API is disabled: this deployment uses local detections, without automatically registering or sharing signals with the community service. LAPI and the Caddy admin API have no published host ports. The HTTP bouncer polls every 15 seconds, so ban addition/removal is eventual. Caddy waits for healthy LAPI at Compose startup. The bouncer retains its default soft-failure behavior: API outages can leave decisions stale or protection unavailable after restart; rate limiting continues. Monitor CrowdSec health and bouncer polling, not merely Caddy uptime.

## Deployment

1. Back up the deployed Caddyfile, Compose file and `.env`, and record the previous Caddy image ID before rollout. Preserve local routing customizations when merging `Caddyfile.example` into the deployed `Caddyfile`; copying the template blindly can erase them.
2. Generate a private bouncer key with `openssl rand -hex 32`, add `CROWDSEC_API_KEY=<value>` to `.env`, and restrict that file to its owner (`chmod 600 .env`). Never use the example or CI key. The same value bootstraps `BOUNCER_KEY_pocketledger_caddy` and configures Caddy. Missing/empty keys fail Compose interpolation. Environment-based secrets are visible to Docker administrators; do not publish expanded `docker compose config` output.
3. Check Cloudflare settings and origin firewall rules described above. Merge/copy the new Caddy template before starting Caddy.
4. Validate and build:

   ```bash
   docker compose config --quiet
   docker compose build caddy
   docker compose run --rm --no-deps caddy caddy list-modules
   docker compose up -d --wait crowdsec
   docker compose exec crowdsec crowdsec -t
   docker compose run --rm --no-deps caddy caddy validate --config /etc/caddy/Caddyfile
   docker compose up -d --no-deps caddy
   ```

   Expect `crowdsec`, `http.handlers.crowdsec`, and `http.handlers.rate_limit`. Config validation alone does not establish bouncer authentication; check polling below. For a fresh installation, follow the root README's application/bootstrap steps before starting the complete stack. Existing application, PostgreSQL and Valkey configuration is unchanged.
5. Verify production traffic through Cloudflare before testing bans. A fresh non-cached HTTPS request should produce `request.remote_ip` in Cloudflare's ranges and `request.client_ip` equal to your actual public IPv4/IPv6 address:

   ```bash
   docker compose exec caddy tail -n 10 /var/log/caddy/access.log
   docker compose ps
   docker compose logs --tail 100 caddy crowdsec
   docker compose exec crowdsec cscli collections list
   docker compose exec crowdsec cscli bouncers list
   docker compose exec crowdsec cscli metrics
   docker compose exec crowdsec cscli alerts list
   docker compose exec crowdsec cscli decisions list
   ```

   Expect the Caddy collection, `pocketledger_caddy` with recent API pull, parsed access logs and acquisition counters increasing. Exercise all four routes and a normal login/refresh flow. An empty alerts/decisions list is normal without attacks.

## Safe verification and troubleshooting

For regression checks with Docker and Python 3:

```bash
docker compose --env-file .env.example build caddy
python3 tools/verify-edge-security.py
```

Set `PL_CADDY_TEST_IMAGE` if the build uses a different project/image name. The script uses a unique Compose project, isolated volumes and mock upstreams, publishes only a random loopback port, and removes only its own test containers/volumes. It validates the production Caddyfile before substituting a local HTTP listener. Only the test fixture additionally trusts its container gateway to simulate Cloudflare; the production trust list is unchanged. It checks both limits, IPv6 grouping, spoofing, four upstream routes, decision enforcement/removal, a bounded synthetic log flood (including 429s), edge exclusion and CrowdSec recreation. It does not test production TLS, Cloudflare connectivity, real application authentication, or origin firewall rules. CI runs the same script.

For a production ban check, use only an IP you control and retain an alternate administrative path:

```bash
docker compose exec crowdsec cscli decisions add --ip <YOUR_PUBLIC_IP> --duration 1m --reason caddy-test
# Wait up to 15 seconds and request an uncached HTTPS page; expect 403.
docker compose exec crowdsec cscli decisions delete --ip <YOUR_PUBLIC_IP>
```

Do not run production flood tests. A Cloudflare-cached response does not reach Caddy. Empty metrics can mean cached traffic, no fresh requests, missing logs, parser errors, or an intentionally whitelisted private/edge IP. Check the log's client_ip and use `cscli explain --file /var/log/caddy/access.log --type caddy` inside CrowdSec, handling the sensitive output privately. Check disk capacity and log volume permissions if logging stops. Do not chmod volumes globally writable.

Changing `.env` alone does **not** rotate an existing registered bouncer key. To rotate: generate/update the secret, delete `pocketledger_caddy` using `cscli bouncers delete pocketledger_caddy`, recreate CrowdSec with `docker compose up -d --force-recreate --wait crowdsec`, then recreate Caddy with `docker compose up -d --no-deps --force-recreate caddy`. Verify recent polling again. This creates a brief enforcement gap; schedule it. Do not delete CrowdSec volumes to rotate a key.

## Rollback

Restore the saved Caddyfile and Compose configuration and the previous Caddy image, then recreate only Caddy with `docker compose up -d --no-deps caddy`. Ensure the restored Compose selects the saved image rather than building the security image. Verify all four HTTPS routes. Stop CrowdSec using the security Compose configuration if removing the feature; preserve its volumes for diagnosis. Never use `down --volumes` on the deployment: that also deletes PocketLedger databases and certificate storage. Rolling back removes this additional protection, so retain Cloudflare and origin firewall controls.

## Upstream references

- [Caddy trusted proxies and client IP options](https://caddyserver.com/docs/caddyfile/options#trusted-proxies)
- [Cloudflare ranges](https://www.cloudflare.com/ips/) and [request header behavior](https://developers.cloudflare.com/fundamentals/reference/http-headers/)
- [Pinned rate limiter documentation](https://github.com/mholt/caddy-ratelimit/blob/5625512f24f6f59d6f64fb3aafe5eecff0b286db/README.md)
- [Pinned HTTP bouncer documentation](https://github.com/hslatman/caddy-crowdsec-bouncer/blob/v0.14.0/README.md)
- [Caddy parser](https://github.com/crowdsecurity/hub/blob/master/parsers/s01-parse/crowdsecurity/caddy-logs.yaml) and [pinned CrowdSec bootstrap](https://github.com/crowdsecurity/crowdsec/blob/v1.8.1/build/docker/docker_start.sh)
