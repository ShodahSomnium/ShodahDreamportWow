#!/usr/bin/env bash
# =============================================================================================
# Dreamport ServerKit - status.sh : one-shot health check of the AzerothCore realm on the server.
# Run as root on the server:   sudo bash status.sh   (Oracle: ~/status.sh, DigitalOcean: /root/status.sh)
# Read-only: changes nothing, prints no passwords. Exit code 0 = realm is up, 1 = problem found.
# Also checks the automatic account service (port 8086) when setup-accounts.sh has installed it.
# =============================================================================================
set -uo pipefail

AC_DIR=/opt/azerothcore
problems=()
bad() { problems+=("$1"); echo "  PROBLEM: $1"; }

if [ "$(id -u)" -ne 0 ]; then echo "Run as root: sudo bash status.sh" >&2; exit 1; fi
if ! docker info >/dev/null 2>&1; then echo "STATUS PROBLEM: docker is not running (systemctl status docker)"; exit 1; fi

state_of() { docker inspect -f '{{.State.Status}}' "$1" 2>/dev/null || echo missing; }
# SQL runs inside ac-database with its own MYSQL_ROOT_PASSWORD environment; no password here.
db() {
    docker exec ac-database sh -c 'exec mysql -uroot -p"$MYSQL_ROOT_PASSWORD" -N -s -e "$1"' sh "$1" 2>&1 \
        | { grep -v 'Using a password on the command line' || true; }
}

echo "== AzerothCore realm status ($(date -u +%Y-%m-%dT%H:%M:%SZ), $(hostname)) =="
echo "-- containers"
docker ps -a --filter "name=ac-" --format 'table {{.Names}}\t{{.Status}}\t{{.Ports}}'
for c in ac-database ac-worldserver ac-authserver; do
    st=$(state_of "$c")
    [ "$st" = "running" ] || bad "$c is $st"
    rc=$(docker inspect -f '{{.RestartCount}}' "$c" 2>/dev/null || echo 0)
    [ "${rc:-0}" -gt 0 ] && echo "  note: $c restarted $rc time(s) - see: docker logs --tail 50 $c"
done
for c in ac-client-data-init ac-db-import; do
    st=$(state_of "$c"); ec=$(docker inspect -f '{{.State.ExitCode}}' "$c" 2>/dev/null || echo "?")
    if [ "$st" = "exited" ] && [ "$ec" != "0" ]; then bad "$c exited with code $ec"; fi
done

echo "-- servers"
# Port checks run inside each container (bash /dev/tcp); a host-side connect would hit Docker's proxy.
if docker exec ac-authserver bash -c '</dev/tcp/127.0.0.1/3724' >/dev/null 2>&1; then
    echo "  authserver:  listening on 3724"
else bad "authserver is not listening on 3724"; fi
if docker exec ac-worldserver bash -c '</dev/tcp/127.0.0.1/8085' >/dev/null 2>&1; then
    echo "  worldserver: listening on 8085"
else bad "worldserver is not listening on 8085 (still starting? docker logs --tail 50 ac-worldserver)"; fi
# worldserver Main.cpp: "<version> (worldserver-daemon) ready..."
ready=$(docker logs ac-worldserver 2>&1 | grep -F "(worldserver-daemon) ready" | tail -1 | tr -d '\r')
[ -n "$ready" ] && echo "  last ready line: $ready"

warden=$(docker exec ac-worldserver printenv AC_WARDEN_ENABLED 2>/dev/null || true)
if [ "$warden" = "0" ]; then echo "  Warden:      OFF (AC_WARDEN_ENABLED=0)"
else bad "Warden is not switched off (AC_WARDEN_ENABLED='$warden') - see $AC_DIR/docker-compose.override.yml"; fi

echo "-- realm"
if [ "$(state_of ac-database)" = "running" ]; then
    realm=$(db "SELECT CONCAT_WS(' | ', id, name, address, port, flag, gamebuild) FROM acore_auth.realmlist ORDER BY id;")
    echo "  realmlist (id | name | address | port | flag | build):"
    printf '%s\n' "$realm" | sed 's/^/    /'
    addr=$(db "SELECT address FROM acore_auth.realmlist WHERE id = 1;")
    flag=$(db "SELECT flag FROM acore_auth.realmlist WHERE id = 1;")
    # Realm.h: REALM_FLAG_OFFLINE = 0x02 (the worldserver clears it for its realm once ready).
    if [[ "$flag" =~ ^[0-9]+$ ]] && [ $(( flag & 2 )) -ne 0 ]; then bad "realm 1 is flagged OFFLINE (flag $flag)"; fi
    # DigitalOcean's metadata knows the public IP; Oracle's does not (setup-server.sh got it as PUBLIC_IP).
    pub=$(curl -fsS -m 3 http://169.254.169.254/metadata/v1/interfaces/public/0/ipv4/address 2>/dev/null || true)
    if [ -n "$pub" ] && [ "$pub" != "$addr" ]; then
        bad "realm address '$addr' is not this droplet's public IP '$pub' (re-run setup-server.sh)"
    fi
    case "$addr" in
        127.*|10.*|192.168.*|172.1[6-9].*|172.2[0-9].*|172.3[01].*)
            bad "realm address '$addr' is not a public IP (sudo PUBLIC_IP=<public ip> bash setup-server.sh)";;
    esac
    online=$(db "SELECT COUNT(*) FROM acore_characters.characters WHERE online <> 0;")
    accounts=$(db "SELECT COUNT(*) FROM acore_auth.account;")
    echo "  accounts: $accounts   characters online: $online"
    echo "  clients:  set realmlist $addr    (Dreamport login panel: $addr, port 3724)"
fi

echo "-- automatic accounts (setup-accounts.sh)"
ACC_PORTS=""
if [ -f /etc/systemd/system/wow-accounts.service ]; then
    ACC_PORTS=8086
    if systemctl is-active --quiet wow-accounts; then
        echo "  service:  wow-accounts running"
    else bad "the account service wow-accounts is not running (sudo journalctl -u wow-accounts -n 30)"; fi
    health=$(curl -fsS -m 5 http://127.0.0.1:8086/health 2>/dev/null || true)
    if [ "$health" = '{"status":"ok","service":"wow-accounts","version":1}' ]; then
        echo "  port 8086: answers on this server"
    else bad "the account service does not answer on port 8086 (sudo journalctl -u wow-accounts -n 30)"; fi
    allowed=$(grep -cvE '^[[:space:]]*(#|$)' /etc/wow-accounts/allow.txt 2>/dev/null || true)
    made=$( { wc -l < /var/lib/wow-accounts/players.tsv; } 2>/dev/null || echo 0)
    echo "  allow list: ${allowed:-0} entries; automatic accounts so far: ${made// /}   (manage: sudo wow-account)"
    # The service's last results (it never logs passwords, salts or verifiers).
    journalctl -u wow-accounts -o cat --no-pager -n 300 2>/dev/null | grep -E '^register ' | tail -3 | sed 's/^/    last: /'
else
    echo "  not installed (optional - README 'Automatic accounts')"
fi

echo "-- server"
if [ -f /etc/iptables/rules.v4 ] && grep -qE '^-A INPUT .*-j REJECT' /etc/iptables/rules.v4; then
    # Oracle-style image: game ports must be ACCEPTed in INPUT before its REJECT rule (setup-server.sh step 3).
    if [ -n "$ACC_PORTS" ]; then fw_ports="3724, 8085 and 8086"; else fw_ports="3724 and 8085"; fi
    echo "  firewall: iptables (/etc/iptables/rules.v4); the Oracle security list must allow $fw_ports too"
    for port in 22 3724 8085 $ACC_PORTS; do
        if iptables -C INPUT -p tcp -m state --state NEW -m tcp --dport "$port" -j ACCEPT 2>/dev/null; then
            echo "    tcp $port allowed"
        elif [ "$port" = 8086 ]; then bad "iptables does not allow tcp 8086 (re-run setup-accounts.sh)"
        else bad "iptables does not allow tcp $port (re-run setup-server.sh)"; fi
    done
else
    echo "  firewall: $(ufw status 2>/dev/null | head -1)"
    ufw status 2>/dev/null | grep -E '^(22|3724|8085|8086)/tcp' | awk '{print "    " $1 " " $2}' | sort -u
fi
echo "  memory:   $(free -m | awk '/^Mem:/{printf "%d/%d MB used", $3, $2} /^Swap:/{printf ", swap %d/%d MB", $3, $2}')"
echo "  disk /:   $(df -h --output=used,size,pcent / | tail -1 | awk '{print $1 " used of " $2 " (" $3 ")"}')"
echo "  load:     $(cut -d' ' -f1-3 /proc/loadavg)   uptime: $(uptime -p)"
docker stats --no-stream --format '    {{.Name}}: cpu {{.CPUPerc}}, mem {{.MemUsage}}' ac-database ac-worldserver ac-authserver 2>/dev/null

echo
if [ "${#problems[@]}" -eq 0 ]; then
    echo "STATUS OK"
    exit 0
fi
echo "STATUS PROBLEM: ${#problems[@]} issue(s): ${problems[*]}"
exit 1
