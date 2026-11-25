#!/usr/bin/env bash
# =============================================================================================
# Dreamport ServerKit - setup-accounts.sh : automatic WoW accounts for Somnium players
#
# Installs the account service the WoW Dreamport calls when a player presses "Play online as ...":
#   /opt/wow-accounts/wow_accounts.py          the service (python3, standard library only)
#   /usr/local/bin/wow-account                 the admin tool (menu: sudo wow-account)
#   /etc/systemd/system/wow-accounts.service   runs the service on TCP 8086, restarts it always
#   /etc/wow-accounts/allow.txt, gm.txt        who may get an account / who becomes Game Master
#                                              (written only if missing - your edits are kept)
#   /var/lib/wow-accounts/                     unlock markers, players.tsv (who owns which account)
# and opens TCP 8086 in this server's firewall (the same way setup-server.sh step 3 opens 3724/8085).
#
# Run it as root AFTER setup-server.sh, with the kit's account-service folder next to this script:
#   sudo bash setup-accounts.sh
# Safe to run again: it updates the program files, keeps allow.txt and gm.txt, restarts the service
# and checks it. The last line is ACCOUNTS OK or ACCOUNTS FAILED. Log: /var/log/wow-accounts-setup.log
#
# Not done here (Oracle console): the security list must allow TCP 8086 as well - README.md,
# "Automatic accounts", step 2. It never creates accounts and never handles a password.
# =============================================================================================
set -Eeuo pipefail

LOG=/var/log/wow-accounts-setup.log
PORT=8086
SERVICE=wow-accounts
APP_DIR=/opt/wow-accounts
ETC_DIR=/etc/wow-accounts
STATE_DIR=/var/lib/wow-accounts
UNIT=/etc/systemd/system/wow-accounts.service
TOOL=/usr/local/bin/wow-account
HEALTH_BODY='{"status":"ok","service":"wow-accounts","version":1}'
# PLAN8 2.5: the first players (Shodah's Somnium ID is from the user's Player.log).
FIRST_ALLOWED=(Shodah Wildy cf5c553c-61f8-43f4-a290-4985ca328eb4)
FIRST_GMS=(cf5c553c-61f8-43f4-a290-4985ca328eb4)

if [ "$(id -u)" -ne 0 ]; then
    echo "Run this as root: sudo bash setup-accounts.sh" >&2
    exit 1
fi
SRC=$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)/account-service
touch "$LOG"
chmod 600 "$LOG"
exec > >(tee -a "$LOG") 2>&1

log()  { printf '[%s] %s\n' "$(date -u +%Y-%m-%dT%H:%M:%SZ)" "$*"; }
step() { printf '\n'; log "===== $* ====="; }
die()  { log "ERROR: $*"; log "ACCOUNTS FAILED - full log: $LOG"; exit 1; }
trap 'rc=$?; log "ERROR: command failed (exit $rc) at line $LINENO: $BASH_COMMAND"; log "ACCOUNTS FAILED - full log: $LOG"' ERR

exec 9>/run/wow-accounts-setup.lock
flock -n 9 || die "another setup-accounts.sh run is still in progress (see $LOG)"

log "=== setup-accounts run started (host $(hostname)) ==="

# ---------------------------------------------------------------------------------------------
# 0. Checks - nothing is installed if the new program does not pass its own self-test
# ---------------------------------------------------------------------------------------------
step "0/5 checks"
for f in wow_accounts.py wow-account wow-accounts.service; do
    [ -f "$SRC/$f" ] || die "$SRC/$f is missing - copy the whole account-service folder next to this script (README, 'Automatic accounts')"
    # Files saved by a Windows editor may have CRLF line ends, which break scripts and the unit file.
    if grep -qU $'\r' "$SRC/$f"; then
        die "$SRC/$f has Windows line ends (CRLF) - copy the original kit files again (scp does not change them)"
    fi
done
command -v systemctl >/dev/null 2>&1 || die "systemd is missing - this script is for Ubuntu 24.04"
[ -x /usr/bin/python3 ] || die "/usr/bin/python3 is missing (sudo apt-get install python3)"
/usr/bin/python3 -c 'import sys; sys.exit(0 if sys.version_info >= (3, 8) else 1)' \
    || die "python3 is too old ($(/usr/bin/python3 --version 2>&1)); 3.8 or newer is needed (Ubuntu 24.04 has 3.12)"
command -v docker >/dev/null 2>&1 || die "docker is missing - run setup-server.sh first"
command -v curl >/dev/null 2>&1 || die "curl is missing (sudo apt-get install curl)"
DB_STATE=$(docker inspect -f '{{.State.Status}}' ac-database 2>/dev/null || echo missing)
log "ok: $(/usr/bin/python3 --version 2>&1), $(docker --version), realm database container ac-database: $DB_STATE"

selftest_out=$(/usr/bin/python3 -I "$SRC/wow_accounts.py" --selftest 2>&1) || {
    printf '%s\n' "$selftest_out" | grep -E 'FAIL|SELFTEST' || true
    die "the self-test of $SRC/wow_accounts.py failed - nothing was installed"
}
printf '%s\n' "$selftest_out" | grep -E '^  verifier  |^SELFTEST OK' | sed 's/^ */selftest: /' || true

# ---------------------------------------------------------------------------------------------
# 1. Program files (root-owned; replaced on every run so a re-run installs the kit's current version)
# ---------------------------------------------------------------------------------------------
step "1/5 program files"
install_file() {  # install_file MODE SOURCE TARGET
    local mode=$1 src=$2 dst=$3
    if [ -f "$dst" ] && cmp -s "$src" "$dst"; then
        chmod "$mode" "$dst"; chown root:root "$dst"
        log "files: $dst unchanged"
    else
        install -m "$mode" -o root -g root "$src" "$dst"
        log "files: $dst installed"
    fi
}
install -d -m 0755 -o root -g root "$APP_DIR"
install_file 0755 "$SRC/wow_accounts.py" "$APP_DIR/wow_accounts.py"
install_file 0755 "$SRC/wow-account" "$TOOL"
install_file 0644 "$SRC/wow-accounts.service" "$UNIT"

# ---------------------------------------------------------------------------------------------
# 2. Lists and state folders (only root can read them)
# ---------------------------------------------------------------------------------------------
step "2/5 allow list, GM list, state"
install -d -m 0700 -o root -g root "$ETC_DIR" "$STATE_DIR" "$STATE_DIR/unlocked" "$STATE_DIR/docker"
if [ -f "$ETC_DIR/allow.txt" ]; then
    log "lists: keeping $ETC_DIR/allow.txt ($(grep -cvE '^[[:space:]]*(#|$)' "$ETC_DIR/allow.txt" || true) entries)"
else
    {
        echo "# Who may get a WoW account by pressing 'Play online' in the WoW Dreamport."
        echo "# One Somnium nickname or Somnium ID per line; upper/lower case does not matter; lines starting with # are notes."
        echo "# A nickname lets in one Somnium account: the first one that gets an account with it."
        echo "# Easiest way to change this list: sudo wow-account   (menu: 'Let a Somnium player in')."
        printf '%s\n' "${FIRST_ALLOWED[@]}"
    } > "$ETC_DIR/allow.txt"
    chmod 600 "$ETC_DIR/allow.txt"
    log "lists: wrote $ETC_DIR/allow.txt (${FIRST_ALLOWED[*]})"
fi
if [ -f "$ETC_DIR/gm.txt" ]; then
    log "lists: keeping $ETC_DIR/gm.txt ($(grep -cvE '^[[:space:]]*(#|$)' "$ETC_DIR/gm.txt" || true) entries)"
else
    {
        echo "# Somnium IDs whose automatic WoW account becomes a Game Master (level 3, all realms) when it is created."
        echo "# One Somnium ID per line. For an account that already exists: sudo wow-account gm NAME on"
        printf '%s\n' "${FIRST_GMS[@]}"
    } > "$ETC_DIR/gm.txt"
    chmod 600 "$ETC_DIR/gm.txt"
    log "lists: wrote $ETC_DIR/gm.txt (${FIRST_GMS[*]})"
fi

# ---------------------------------------------------------------------------------------------
# 3. Firewall: TCP 8086, exactly the way setup-server.sh step 3 adds 3724/8085
# Oracle-style images (INPUT ends with a REJECT rule in /etc/iptables/rules.v4, no ufw): the line goes
# into rules.v4 just before the first INPUT REJECT (loaded at boot) and is inserted live at the same
# place - not via iptables-restore, which would flush Docker's rules. Other servers: ufw.
# The service is a normal program on the server (not a Docker container), so this firewall applies to it.
# ---------------------------------------------------------------------------------------------
step "3/5 firewall"
if [ -f /etc/iptables/rules.v4 ] && grep -qE '^-A INPUT .*-j REJECT' /etc/iptables/rules.v4; then
    RULES=/etc/iptables/rules.v4
    spec="-p tcp -m state --state NEW -m tcp --dport $PORT -j ACCEPT"
    if grep -qxF -- "-A INPUT $spec" "$RULES"; then
        log "iptables: $RULES already allows tcp $PORT"
    else
        [ -e "$RULES.before-wow-accounts" ] || cp -p "$RULES" "$RULES.before-wow-accounts"
        tmp=$(mktemp)
        # Before the first INPUT REJECT line; if there is none, before the *filter table's COMMIT.
        awk -v r="-A INPUT $spec" '
            BEGIN { done = 0; infilter = 0 }
            /^\*filter/ { infilter = 1 }
            !done && /^-A INPUT .*-j REJECT/ { print r; done = 1 }
            !done && infilter && /^COMMIT/ { print r; done = 1 }
            { print }' "$RULES" > "$tmp"
        cat "$tmp" > "$RULES"   # keeps the file's owner and mode
        rm -f "$tmp"
        log "iptables: added 'tcp $PORT ACCEPT' to $RULES (backup: $RULES.before-wow-accounts)"
    fi
    # shellcheck disable=SC2086
    if iptables -C INPUT $spec 2>/dev/null; then
        log "iptables: tcp $PORT already allowed (live)"
    else
        n=$(iptables -L INPUT --line-numbers -n | awk '$2 == "REJECT" { print $1; exit }' || true)
        # shellcheck disable=SC2086
        if [ -n "$n" ]; then iptables -I INPUT "$n" $spec; else iptables -A INPUT $spec; fi
        log "iptables: tcp $PORT allowed (live, INPUT position ${n:-end})"
    fi
    FW_SUMMARY="iptables ($RULES): tcp $PORT allowed; the Oracle security list must allow tcp $PORT too"
elif command -v ufw >/dev/null 2>&1; then
    ufw allow $PORT/tcp comment 'WoW account service' >/dev/null
    FW_SUMMARY="ufw: tcp $PORT allowed ($(ufw status | head -1 | sed 's/^Status: //' || true))"
else
    FW_SUMMARY="no iptables rules file and no ufw found - make sure tcp $PORT is reachable"
    log "WARNING: $FW_SUMMARY"
fi
log "firewall: $FW_SUMMARY"

# ---------------------------------------------------------------------------------------------
# 4. Service: (re)start and check that it answers
# ---------------------------------------------------------------------------------------------
step "4/5 service"
if command -v systemd-analyze >/dev/null 2>&1; then
    # Information only (it may also mention other units); the start and the checks below decide.
    verify=$(systemd-analyze verify "$UNIT" 2>&1 || true)
    log "unit check (systemd-analyze verify): ${verify:-nothing to report}"
fi
systemctl daemon-reload
systemctl enable "$SERVICE" >/dev/null 2>&1
sleep 1
T0=$(date +%s)
systemctl restart "$SERVICE"
show_service() {
    systemctl status --no-pager --lines=0 "$SERVICE" 2>&1 | head -5 || true
    log "--- last 20 lines of the service log ---"
    journalctl -u "$SERVICE" --since "@$T0" -o cat --no-pager 2>&1 | tail -20 || true
}
body=""
for _ in $(seq 1 20); do
    body=$(curl -fsS -m 3 "http://127.0.0.1:$PORT/health" 2>/dev/null || true)
    [ "$body" = "$HEALTH_BODY" ] && break
    sleep 1
done
if [ "$body" != "$HEALTH_BODY" ]; then
    show_service
    die "the service does not answer on port $PORT (got: ${body:-nothing})"
fi
log "service: running, http://127.0.0.1:$PORT/health answers $body"
LISTEN=$(ss -Hltn "sport = :$PORT" 2>/dev/null | awk '{print $4}' | tr '\n' ' ' || true)
log "service: listening on ${LISTEN:-?}"

# The service logs "database ok: ..." (or a WARNING) right after it starts - that proves the docker
# command works from inside the service's systemd limits.
start_line=""
for _ in $(seq 1 20); do
    start_line=$(journalctl -u "$SERVICE" --since "@$T0" -o cat --no-pager 2>/dev/null \
                 | grep -E '^(database ok:|WARNING: )' | tail -1 || true)
    [ -n "$start_line" ] && break
    sleep 1
done

# ---------------------------------------------------------------------------------------------
# 5. Database (read-only: connection + the table columns the service writes)
# ---------------------------------------------------------------------------------------------
step "5/5 database"
if [ "$DB_STATE" != "running" ]; then
    die "the realm database (container ac-database) is $DB_STATE - start the realm first (cd /opt/azerothcore && sudo docker compose up -d --no-build, or setup-server.sh), then run this again"
fi
dbcheck=$(/usr/bin/python3 -I "$APP_DIR/wow_accounts.py" dbcheck 2>&1) || die "database check failed: $dbcheck"
log "database (as root): $dbcheck"
case "$start_line" in
    "database ok:"*) log "database (from the service): $start_line" ;;
    "")              show_service; die "the service did not report its database check within 20 s (see its log above)" ;;
    *)               show_service; die "the service cannot use the database: $start_line" ;;
esac

printf '\n'
log "================= automatic WoW accounts ================="
echo "Service:     $SERVICE running on tcp $PORT (restarts by itself; log: sudo journalctl -u $SERVICE)"
echo "Firewall:    $FW_SUMMARY"
echo "Allow list:  $ETC_DIR/allow.txt: $(grep -vE '^[[:space:]]*(#|$)' "$ETC_DIR/allow.txt" | tr '\n' ' ' || true)"
echo "GM list:     $ETC_DIR/gm.txt: $(grep -vE '^[[:space:]]*(#|$)' "$ETC_DIR/gm.txt" | tr '\n' ' ' || true)"
echo "Database:    $dbcheck"
echo
echo "Manage accounts: sudo wow-account   (on your PC: double-click 'Manage WoW accounts.cmd')"
echo "Oracle Cloud: the Default Security List needs an ingress rule for TCP $PORT (README, 'Automatic accounts', step 2)."
log "ACCOUNTS OK"
