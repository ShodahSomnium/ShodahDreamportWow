# ServerKit - the lead's SSH commands

For the lead (Claude) or anyone else who runs the kit from the user's PC (Git Bash on Windows, or any
POSIX shell). Put the server's public IPv4 in `IP`. The key is `~/.ssh/wow_droplet`
(`C:/Users/derii/.ssh/wow_droplet` in Git Bash). Its public half (`wow_droplet.pub`) must have been
added to the server when it was created (README step 1).

```bash
IP=203.0.113.7                      # the server's public IPv4 (Oracle: instance page; DigitalOcean: droplet list)
U=ubuntu; H=/home/ubuntu            # Oracle Cloud (Ubuntu image user, passwordless sudo)
# U=root; H=/root                   # DigitalOcean
KEY=~/.ssh/wow_droplet
KIT="/d/Dreamport/WoW/World Of Warcraft/ServerKit"
SSH="ssh -i $KEY -o StrictHostKeyChecking=accept-new -o ServerAliveInterval=30 $U@$IP"
```

## Rules

- Only the user creates game accounts and types passwords (worldserver console, README step 3).
  The lead never creates accounts, never logs in to the realm, never handles a password.
  Automatic accounts are made by the account service when an allowed player presses "Play online" in the
  Dreamport; the lead only installs and checks that service (section "Automatic accounts" below).
- No protocol tests against the server: those run against the fake server in the harness on localhost.
- Nothing here changes the user's WoW install (`D:/WOTLK/...`).

## First setup

Oracle (arm64): compiles AzerothCore first - roughly 1-3 hours, then 15-40 minutes of data download and
database import. DigitalOcean (x64, prebuilt images): 15-40 minutes.

The script is copied up and started detached (`nohup`), so a dropped SSH connection does not stop it.
Do not pipe it through `ssh ... bash -s`: apt and docker could read the rest of the script from stdin.

```bash
scp -i $KEY -o StrictHostKeyChecking=accept-new "$KIT/setup-server.sh" "$KIT/status.sh" $U@$IP:$H/
$SSH "sudo PUBLIC_IP=$IP nohup bash $H/setup-server.sh >/dev/null 2>&1 </dev/null &"
```

(`PUBLIC_IP` is required on Oracle - the instance only knows its private 10.x address - and harmless
on DigitalOcean.)

Follow it (Ctrl+C only stops watching, not the setup):

```bash
$SSH 'sudo tail -n 40 -f /var/log/wow-setup.log'
```

Or check once whether it finished (the last line of a finished run is `SETUP OK` or `SETUP FAILED ...`):

```bash
$SSH 'sudo tail -n 25 /var/log/wow-setup.log'
```

Running the script again is safe (each step checks what is already done; it does not update AzerothCore).

From the PC, check that the game ports are reachable from outside (Oracle: needs the security-list rules
from README Oracle step 3 AND the iptables lines the script adds):

```bash
for p in 3724 8085; do timeout 5 bash -c "</dev/tcp/$IP/$p" && echo "$p open" || echo "$p CLOSED"; done
```

## Health check

```bash
$SSH "sudo bash $H/status.sh"          # last line: STATUS OK / STATUS PROBLEM: ...
```

## Logs

```bash
$SSH 'cd /opt/azerothcore && sudo docker compose logs --tail 80 ac-worldserver'
$SSH 'cd /opt/azerothcore && sudo docker compose logs --tail 80 ac-authserver'
$SSH 'sudo ls -l /opt/azerothcore/env/dist/logs'                    # Server.log, Errors.log, ...
```

## Restart / stop / start

```bash
$SSH 'cd /opt/azerothcore && sudo docker compose restart -t 60 ac-worldserver'   # worldserver only
$SSH 'cd /opt/azerothcore && sudo docker compose restart -t 60'                  # everything
$SSH 'cd /opt/azerothcore && sudo docker compose stop -t 60'                     # stop
$SSH 'cd /opt/azerothcore && sudo docker compose up -d --no-build'               # start again
```

## Update AzerothCore (only when the user asks)

Backup first (auth + characters = accounts and characters; add `acore_world` if world data was changed in-game), then update:

```bash
$SSH 'sudo mkdir -p /root/backups && sudo docker exec ac-database sh -c '"'"'exec mysqldump -uroot -p"$MYSQL_ROOT_PASSWORD" --databases acore_auth acore_characters'"'"' | sudo tee /root/backups/acore-$(date +%F-%H%M).sql >/dev/null && sudo ls -lh /root/backups | tail -3'
# x64 (prebuilt images):
$SSH 'cd /opt/azerothcore && sudo git pull --ff-only && sudo docker compose pull && sudo docker compose up -d --no-build'
# arm64 (compiled; ccache makes later compiles faster):
$SSH 'cd /opt/azerothcore && sudo git pull --ff-only && sudo docker compose build ac-worldserver && sudo docker compose build ac-authserver ac-db-import ac-client-data-init && sudo docker compose up -d --no-build'
$SSH "sudo bash $H/status.sh"
```

`.env` and `docker-compose.override.yml` (Warden off, database ports on 127.0.0.1, build args) are
git-ignored by AzerothCore, so `git pull` keeps them.

## Where things are on the server

| What | Where |
|---|---|
| Setup log | `/var/log/wow-setup.log` |
| AzerothCore checkout | `/opt/azerothcore` |
| Our overrides | `/opt/azerothcore/.env`, `/opt/azerothcore/docker-compose.override.yml` |
| Oracle firewall lines | `/etc/iptables/rules.v4` (original: `rules.v4.before-wow-setup`) |
| Config files (generated on first start) | `/opt/azerothcore/env/dist/etc/{worldserver,authserver}.conf` |
| Server log files | `/opt/azerothcore/env/dist/logs/` |
| Databases / client data | Docker volumes `azerothcore_ac-database`, `azerothcore_ac-client-data` |

## Automatic accounts (setup-accounts.sh, README section 8)

The account service (TCP 8086) creates a WoW account when an allowed Somnium player presses "Play online as ..."
in the Dreamport. The lead installs and checks it; the lead never runs `wow-account create`, never types a
password and never presses Play online for the user.

Install or update (seconds; safe to repeat; keeps allow.txt / gm.txt; last line `ACCOUNTS OK` or `ACCOUNTS FAILED ...`):

```bash
scp -i $KEY -o StrictHostKeyChecking=accept-new -r "$KIT/setup-accounts.sh" "$KIT/status.sh" "$KIT/account-service" $U@$IP:$H/
$SSH "sudo bash $H/setup-accounts.sh"
```

It runs the service's self-test before installing, restarts the service, waits for `/health`, and fails
unless the service itself logged `database ok` after the restart (proves `docker exec` works inside its
systemd limits). Full log: `$SSH 'sudo tail -n 60 /var/log/wow-accounts-setup.log'`.

Check (read-only):

```bash
$SSH 'sudo wow-account check'                              # service, port, database columns, firewall, self-test -> CHECK OK
$SSH 'sudo wow-account list'                               # accounts, allow/GM lists, unlocks, players turned away
$SSH 'sudo journalctl -u wow-accounts -n 40 --no-pager'    # one line per request; never passwords/salts/verifiers
$SSH 'sudo wow-account selftest | grep -E "verifier  |SELFTEST"'   # PLAN8 2.3 vector D1396E7B...1749
curl -s -m 5 http://$IP:8086/health; echo                 # from the PC; needs the Oracle ingress rule for 8086
```

Smoke test WITHOUT database writes: a second copy of the service in `--dry-run` on 127.0.0.1:18086 (it reads
the real allow/GM lists, pretends the database is empty, prints the SQL it would run with salt/verifier hidden,
changes no file). Fed the PLAN8 2.3 test vector (public test values, no real password): expect the health JSON,
then `{"status":"created","name":"SCF5C553C61F843F4","gm":true}` with the SQL, then `{"status":"exists",...}`.

```bash
$SSH 'sudo bash -s' <<'EOF'
python3 -I /opt/wow-accounts/wow_accounts.py --dry-run --bind 127.0.0.1 --port 18086 </dev/null & pid=$!
sleep 2
curl -s -m 5 http://127.0.0.1:18086/health </dev/null; echo
body='{"v":1,"somniumId":"cf5c553c-61f8-43f4-a290-4985ca328eb4","nick":"Shodah","salt":"000102030405060708090A0B0C0D0E0F101112131415161718191A1B1C1D1E1F","verifier":"D1396E7B6A7ABF12EC485ED31A9C88955EB3FE30023BB1B301D0A0F3726D1749"}'
for i in 1 2; do curl -s -m 10 -H 'Content-Type: application/json' --data "$body" http://127.0.0.1:18086/register </dev/null; echo; done
kill $pid
EOF
```

Restart / stop: `$SSH 'sudo systemctl restart wow-accounts'`, `$SSH 'sudo systemctl disable --now wow-accounts'`
(accounts stay). allow.txt / gm.txt changes need no restart.

| What (automatic accounts) | Where |
|---|---|
| Setup log | `/var/log/wow-accounts-setup.log` |
| Service / unit | `/opt/wow-accounts/wow_accounts.py`, `/etc/systemd/system/wow-accounts.service` |
| Admin tool | `/usr/local/bin/wow-account` |
| Allow / GM lists | `/etc/wow-accounts/allow.txt`, `/etc/wow-accounts/gm.txt` |
| Unlock markers, owners | `/var/lib/wow-accounts/unlocked/`, `/var/lib/wow-accounts/players.tsv` |
| Firewall line | `/etc/iptables/rules.v4` (before: `rules.v4.before-wow-accounts`) |
