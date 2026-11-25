#!/usr/bin/env bash
# =============================================================================================
# Dreamport ServerKit - setup-hold.sh
#
# Installs mod-dreamport-hold (the folder "dreamport-hold" next to this script) into your AzerothCore
# realm set up by setup-server.sh: immersive VR casting for Dreamport players who switched Immersive
# casting on (the cast starts at once, waits at its end until the hand is pushed forward). Normal WoW
# clients and desktop Dreamport players are not affected: only a client that asks for a hold, cast by
# cast, gets one (see dreamport-hold/README.md).
#
#   scp -r -i ~/.ssh/wow_droplet setup-hold.sh dreamport-hold ubuntu@IP:
#   ssh -i ~/.ssh/wow_droplet ubuntu@IP
#   sudo bash setup-hold.sh
#
# What it does:
#   1. checks: /opt/azerothcore from setup-server.sh, docker compose, the module sources
#   2. copies dreamport-hold to /opt/azerothcore/modules/mod-dreamport-hold (AzerothCore's .gitignore
#      ignores /modules/*, so "git pull" never touches it)
#   3. rebuilds the worldserver image with AzerothCore's own compose build (docker compose build
#      ac-worldserver, the Dockerfile's "COPY modules /azerothcore/modules" + CMODULES static) - the
#      same build setup-server.sh runs on ARM; on x64 it is the first local compile (the prebuilt
#      images have no modules)
#   4. restarts ONLY the worldserver (players online are disconnected for about a minute)
#   5. waits until the worldserver is ready and checks that it logged the module's start line
# Safe to run again: when the installed module is identical and the running worldserver loaded it,
# nothing is rebuilt. If the compile fails, the running worldserver is not touched and the module is
# taken out again (so a later AzerothCore update still builds). Last line: HOLD OK or HOLD FAILED.
# Everything is logged to /var/log/wow-hold.log.
# =============================================================================================
set -Eeuo pipefail

LOG=/var/log/wow-hold.log
AC_DIR=/opt/azerothcore
MOD_NAME=mod-dreamport-hold
SRC_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)/dreamport-hold"
DST_DIR="$AC_DIR/modules/$MOD_NAME"
BACKUP_DIR=/var/lib/dreamport-hold/previous   # outside modules/ (every folder there with a src/ is built as a module)
BUILD_TOOLS=db-only                   # the same as setup-server.sh (src/tools/CMakeLists.txt: TOOLS_BUILD db-only)
WAIT_WORLD_MIN=${WAIT_WORLD_MIN:-30}
# dreamport_hold.cpp, DreamportHoldWorld::OnStartup (Logger.module = 4 Console in worldserver.conf.dist 710)
LOADED_LINE="Dreamport hold: module loaded (protocol 1,"

if [ "$(id -u)" -ne 0 ]; then
    echo "Run this as root: sudo bash setup-hold.sh" >&2
    echo "HOLD FAILED" >&2
    exit 1
fi
touch "$LOG"
chmod 600 "$LOG"
exec > >(tee -a "$LOG") 2>&1

log()  { printf '[%s] %s\n' "$(date -u +%Y-%m-%dT%H:%M:%SZ)" "$*"; }
step() { printf '\n'; log "===== $* ====="; }
die()  { log "ERROR: $*"; log "HOLD FAILED - full log: $LOG"; exit 1; }
trap 'rc=$?; log "ERROR: command failed (exit $rc) at line $LINENO: $BASH_COMMAND"; log "HOLD FAILED - full log: $LOG"' ERR

exec 8>/run/wow-hold.lock
flock -n 8 || die "another setup-hold.sh run is still in progress (see $LOG)"
if [ -e /run/wow-setup.lock ] && ! flock -n /run/wow-setup.lock true; then
    die "setup-server.sh is running right now - wait until it ends with SETUP OK, then run this again"
fi

log "=== wow-hold run started (host $(hostname)) ==="

# ---------------------------------------------------------------------------------------------
step "1/5 checks"
[ -d "$AC_DIR/.git" ] && [ -f "$AC_DIR/docker-compose.yml" ] || die "$AC_DIR is not the AzerothCore checkout of setup-server.sh - run setup-server.sh first"
[ -f "$SRC_DIR/src/dreamport_hold.cpp" ] && [ -f "$SRC_DIR/src/dreamport_hold_loader.cpp" ] \
    || die "the folder dreamport-hold (with src/dreamport_hold.cpp and src/dreamport_hold_loader.cpp) must be next to this script: scp -r setup-hold.sh dreamport-hold ..."
docker compose version >/dev/null 2>&1 || die "docker compose is missing - run setup-server.sh first"
cd "$AC_DIR"
docker compose config --quiet || die "docker compose rejects the configuration in $AC_DIR"
ARCH=$(dpkg --print-architecture)
log "ok: $ARCH, AzerothCore commit $(git -C "$AC_DIR" rev-parse --short HEAD), module sources in $SRC_DIR"

state_of() { docker inspect -f '{{.State.Status}}' "$1" 2>/dev/null || echo missing; }
# worldserver Main.cpp logs "<version> (worldserver-daemon) ready..." once it accepts players; the port check runs inside
# the container (a host-side connect would succeed on Docker's proxy even when nothing listens yet) - as setup-server.sh.
world_ready() {
    [ "$(state_of ac-worldserver)" = "running" ] || return 1
    docker logs ac-worldserver 2>&1 | grep -F "(worldserver-daemon) ready" >/dev/null || return 1
    docker exec ac-worldserver bash -c '</dev/tcp/127.0.0.1/8085' >/dev/null 2>&1
}
module_loaded() { docker logs ac-worldserver 2>&1 | grep -F "$LOADED_LINE" >/dev/null; }
wait_until() {  # wait_until SECONDS DESCRIPTION COMMAND... - polls every 10 s
    local limit=$1 what=$2 t0 now
    shift 2
    t0=$(date +%s)
    while true; do
        if "$@"; then log "ready: $what"; return 0; fi
        now=$(date +%s)
        if [ $((now - t0)) -ge "$limit" ]; then return 1; fi
        if [ $(( (now - t0) % 60 )) -lt 10 ]; then log "waiting: $what ($(( (now - t0) / 60 )) min)"; fi
        sleep 10
    done
}
finish_ok() {
    printf '\n'
    log "================= Dreamport hold ================="
    docker ps -a --filter "name=ac-worldserver" --format 'table {{.Names}}\t{{.Status}}\t{{.Image}}'
    echo "Module:   $DST_DIR"
    echo "Log line: $(docker logs ac-worldserver 2>&1 | grep -F "$LOADED_LINE" | tail -1 | tr -d '\r')"
    echo
    echo "Test (VR, Settings > Gameplay > Immersive casting on): pick Fireball - the cast bar runs, the controller"
    echo "vibrates at its end and the spell waits in your hand; push the hand forward to fire it."
    echo "Each client says hello once per login: 'docker compose logs ac-worldserver | grep \"Dreamport hold\"'."
    if [ "$ARCH" = "amd64" ]; then
        echo
        echo "x64 note: your worldserver image is now built here. 'docker compose pull' (README section 5, Update)"
        echo "would bring back AzerothCore's image WITHOUT this module - after an update run setup-hold.sh again."
    fi
    log "HOLD OK"
    exit 0
}

# ---------------------------------------------------------------------------------------------
step "2/5 module files"
same=0
if [ -d "$DST_DIR" ] && diff -r -q "$SRC_DIR" "$DST_DIR" >/dev/null 2>&1; then same=1; fi
if [ "$same" = 1 ] && [ "$(state_of ac-worldserver)" = "running" ] && module_loaded; then
    log "module: $DST_DIR is identical to $SRC_DIR and the running worldserver loaded it - nothing to rebuild"
    wait_until 300 "worldserver listening on 8085" world_ready || die "the worldserver is not ready (docker compose logs --tail 50 ac-worldserver)"
    finish_ok
fi
rm -rf "$BACKUP_DIR"; mkdir -p "$(dirname "$BACKUP_DIR")"
if [ -d "$DST_DIR" ]; then
    mv "$DST_DIR" "$BACKUP_DIR"
    log "module: previous version kept in $BACKUP_DIR until the new one builds"
fi
cp -r "$SRC_DIR" "$DST_DIR"
log "module: installed $DST_DIR ($(find "$DST_DIR" -type f | wc -l) files)"

restore_previous() {
    rm -rf "$DST_DIR"
    if [ -d "$BACKUP_DIR" ]; then mv "$BACKUP_DIR" "$DST_DIR"; log "module: the previous version is back in $DST_DIR"
    else log "module: removed $DST_DIR again (later AzerothCore builds do not include it)"; fi
}

# ---------------------------------------------------------------------------------------------
step "3/5 compile (docker compose build ac-worldserver)"
if [ "$ARCH" = "amd64" ] && ! docker image inspect acore/ac-wotlk-worldserver:master >/dev/null 2>&1; then
    log "note: no worldserver image yet - run setup-server.sh first"
fi
log "compose: compiling the worldserver with the module (ARM: faster than the first setup thanks to the compiler cache;"
log "         x64 servers that used the prebuilt images compile AzerothCore here for the first time - an hour or more)"
export BUILDKIT_PROGRESS=plain
t0=$(date +%s)
if ! docker compose build --build-arg CTOOLS_BUILD="$BUILD_TOOLS" ac-worldserver; then
    restore_previous
    die "the compile failed (compiler errors above) - the running worldserver was not touched"
fi
log "compose: worldserver image built in $(( ($(date +%s) - t0) / 60 )) min"
rm -rf "$BACKUP_DIR"; mkdir -p "$(dirname "$BACKUP_DIR")"

# ---------------------------------------------------------------------------------------------
step "4/5 restart the worldserver"
# --no-deps: only the worldserver (the database, the one-shot import and client-data containers stay as they are).
docker compose up -d --no-build --no-deps --force-recreate ac-worldserver || die "docker compose up ac-worldserver failed"
log "compose: ac-worldserver recreated with the new image"

# ---------------------------------------------------------------------------------------------
step "5/5 wait and check"
wait_until $((WAIT_WORLD_MIN * 60)) "worldserver listening on 8085" world_ready \
    || { docker logs --tail 40 ac-worldserver 2>&1 || true; die "worldserver not ready after $WAIT_WORLD_MIN min"; }
if ! module_loaded; then
    docker logs ac-worldserver 2>&1 | grep -iE "module|dreamport" | tail -20 || true
    die "the worldserver runs but did not log '$LOADED_LINE' - the module was not built in (see the build output above)"
fi
finish_ok
