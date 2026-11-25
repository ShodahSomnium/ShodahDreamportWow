#!/usr/bin/env bash
# =============================================================================================
# Dreamport ServerKit - setup-server.sh
#
# Sets up an AzerothCore 3.3.5a realm (WotLK, client build 12340) on a FRESH Ubuntu 24.04 server:
#   - Oracle Cloud Always Free, Ampere A1 (arm64): AzerothCore is compiled on the server (Docker Hub
#     has amd64 images only), firewall ports go into Oracle's /etc/iptables/rules.v4 (no ufw);
#   - DigitalOcean droplet or any other x64 Ubuntu: AzerothCore's prebuilt images, ufw.
# Run as root (sudo). Safe to run again: every step checks what is already done.
#
#   sudo PUBLIC_IP=203.0.113.7 bash setup-server.sh   # Oracle: required (the instance only knows its private IP)
#   sudo bash setup-server.sh                         # DigitalOcean: public IP detected automatically
#
# What it does (each step is explained where it happens):
#   1. swap file (small servers)   4. clone azerothcore-wotlk (shallow) to /opt/azerothcore
#   2. git, Docker                 5. config: Warden OFF, database/SOAP only on 127.0.0.1
#   3. firewall: 22, 3724, 8085    6. AzerothCore's own docker compose (prebuilt images, or compiled on arm64)
#                                  7. realm address = this server's public IP, wait, short status
#
# It NEVER creates game accounts or passwords (you do that yourself in the worldserver console,
# see README.md). Everything is logged to /var/log/wow-setup.log.
#
# Sources this follows (azerothcore-wotlk master, read 2026-09-27):
#   docker-compose.yml, conf/dist/env.docker, apps/docker/entrypoint.sh,
#   src/common/Configuration/Config.cpp, src/server/apps/worldserver/worldserver.conf.dist,
#   azerothcore.org wiki "Install with Docker", docs.docker.com "Install Docker Engine on Ubuntu",
#   conf/dist/config.cmake + src/tools/CMakeLists.txt (TOOLS_BUILD db-only), docs.oracle.com
#   "Known Issues for Compute" (UFW) and "Always Free Resources".
# =============================================================================================
set -Eeuo pipefail

LOG=/var/log/wow-setup.log
AC_DIR=/opt/azerothcore
AC_REPO=https://github.com/azerothcore/azerothcore-wotlk.git
SWAPFILE=/swapfile
SWAP_MB=2048
SWAP_BELOW_MB=8000                    # only servers with less RAM than this get the swap file
# The images run as uid/gid 1000 (apps/docker/Dockerfile: ARG USER_ID=1000, GROUP_ID=1000).
AC_UID=1000
AC_GID=1000
# Compiled builds (arm64): src/tools/CMakeLists.txt - TOOLS_BUILD "db-only" builds only dbimport (the
# db-import container needs it); the map extractors (ac-tools, not used here) are skipped.
BUILD_TOOLS=db-only
WAIT_DATA_MIN=${WAIT_DATA_MIN:-90}    # client data download (1.2 GB) + database import
WAIT_WORLD_MIN=${WAIT_WORLD_MIN:-30}  # worldserver start after that
MARK="# Managed by Dreamport ServerKit (setup-server.sh). Remove this line to keep your own edits."

# ---------------------------------------------------------------------------------------------
# Logging: everything (stdout + stderr) goes to the screen AND to /var/log/wow-setup.log
# ---------------------------------------------------------------------------------------------
if [ "$(id -u)" -ne 0 ]; then
    echo "Run this as root: sudo bash setup-server.sh" >&2
    exit 1
fi
touch "$LOG"
chmod 600 "$LOG"
exec > >(tee -a "$LOG") 2>&1

log()  { printf '[%s] %s\n' "$(date -u +%Y-%m-%dT%H:%M:%SZ)" "$*"; }
step() { printf '\n'; log "===== $* ====="; }
die()  { log "ERROR: $*"; log "SETUP FAILED - full log: $LOG"; exit 1; }
trap 'rc=$?; log "ERROR: command failed (exit $rc) at line $LINENO: $BASH_COMMAND"; log "SETUP FAILED - full log: $LOG"' ERR

# Only one run at a time.
exec 9>/run/wow-setup.lock
flock -n 9 || die "another setup-server.sh run is still in progress (see $LOG)"

log "=== wow-setup run started (host $(hostname)) ==="

# ---------------------------------------------------------------------------------------------
# 0. Checks
# ---------------------------------------------------------------------------------------------
step "0/7 checks"
[ -r /etc/os-release ] || die "/etc/os-release missing - this script is for Ubuntu 24.04"
# shellcheck disable=SC1091
. /etc/os-release
[ "${ID:-}" = "ubuntu" ] || die "this script is for Ubuntu 24.04 (found ${PRETTY_NAME:-unknown})"
if [ "${VERSION_ID:-}" != "24.04" ]; then
    log "WARNING: written and checked for Ubuntu 24.04, this is ${PRETTY_NAME:-unknown} - continuing"
fi
ARCH=$(dpkg --print-architecture)
# AzerothCore's prebuilt images on Docker Hub are published for amd64 only (checked 2026-09-27), so
# arm64 servers (Oracle Ampere A1) compile them from the cloned source with AzerothCore's own
# Dockerfile (docker-compose.yml has a build: section for every service). FORCE_BUILD=1 compiles on
# amd64 as well.
case "$ARCH" in
    amd64) BUILD=${FORCE_BUILD:-0} ;;
    arm64) BUILD=1 ;;
    *) die "CPU architecture $ARCH is not supported (amd64 or arm64)" ;;
esac

# Where we run. Oracle's instance metadata (IMDS v2) answers only with the "Authorization: Bearer
# Oracle" header; DigitalOcean's metadata service has /metadata/v1/.
PLATFORM=other
if curl -fsS -m 3 -H 'Authorization: Bearer Oracle' http://169.254.169.254/opc/v2/instance/ >/dev/null 2>&1; then
    PLATFORM=oracle
elif curl -fsS -m 3 http://169.254.169.254/metadata/v1/id >/dev/null 2>&1; then
    PLATFORM=digitalocean
fi

# Firewall. Oracle (docs.oracle.com, Known Issues for Compute): "Do not use UFW to edit firewall rules
# ... UFW may remove these rules so that during a reboot the instance is not able to connect to the
# boot and block volumes. To modify or add new firewall rules, update the /etc/iptables/rules.v4 file
# instead." Any image that ships that file with a REJECT rule gets the same treatment; others use ufw.
if [ -f /etc/iptables/rules.v4 ] && grep -qE '^-A INPUT .*-j REJECT' /etc/iptables/rules.v4; then
    FW=iptables
else
    FW=ufw
fi

# Oracle instances only know their private address (10.x), and the metadata has no public IP, so the
# public IP must be given - checked now, not after a long compile.
if [ "$PLATFORM" = "oracle" ] && [ -z "${PUBLIC_IP:-}" ]; then
    die "on Oracle Cloud the public IP must be given: sudo PUBLIC_IP=<Public IP address from the instance page> bash $0"
fi

if command -v cloud-init >/dev/null 2>&1; then
    log "waiting for cloud-init (first boot) to finish"
    cloud-init status --wait >/dev/null 2>&1 || true
fi
MEM_MB=$(free -m | awk '/^Mem:/{print $2}')
log "ok: ${PRETTY_NAME:-Ubuntu}, $ARCH, $(nproc) vCPU, $MEM_MB MB RAM, $(df -BG --output=avail / | tail -1 | tr -d ' ') free on /"
log "ok: platform $PLATFORM, firewall via $FW, AzerothCore $( [ "$BUILD" = 1 ] && echo "compiled on this server" || echo "prebuilt images")"

# ---------------------------------------------------------------------------------------------
# 1. Swap (2 GB) - headroom for MySQL + worldserver on small servers (skipped with >= 8 GB RAM)
# ---------------------------------------------------------------------------------------------
step "1/7 swap"
if swapon --show=NAME --noheadings | grep -qx "$SWAPFILE"; then
    log "swap: $SWAPFILE already active"
elif [ "$MEM_MB" -ge "$SWAP_BELOW_MB" ]; then
    log "swap: not needed ($MEM_MB MB RAM)"
else
    if [ ! -f "$SWAPFILE" ]; then
        log "swap: creating $SWAPFILE (${SWAP_MB} MB)"
        fallocate -l "${SWAP_MB}M" "$SWAPFILE" || dd if=/dev/zero of="$SWAPFILE" bs=1M count="$SWAP_MB" status=none
    fi
    chmod 600 "$SWAPFILE"
    if ! swapon "$SWAPFILE" 2>/dev/null; then
        mkswap "$SWAPFILE" >/dev/null
        swapon "$SWAPFILE"
    fi
    log "swap: $SWAPFILE active"
fi
if swapon --show=NAME --noheadings | grep -qx "$SWAPFILE" && ! grep -qE "^${SWAPFILE}[[:space:]]" /etc/fstab; then
    echo "$SWAPFILE none swap sw 0 0" >> /etc/fstab
    log "swap: added to /etc/fstab"
fi

# ---------------------------------------------------------------------------------------------
# 2. Packages: git, ufw (not on Oracle), Docker Engine + compose plugin
#
# Docker comes from DOCKER'S OFFICIAL apt repository (download.docker.com), not Ubuntu's docker.io:
#  - AzerothCore's wiki ("Install with Docker") links exactly that guide for Linux
#    (docs.docker.com/install/linux/docker-ce/ubuntu) and needs the `docker compose` plugin;
#  - Docker's guide lists Ubuntu Noble 24.04 as supported and installs the current Engine +
#    docker-compose-plugin, which matches what AzerothCore's compose file is written against.
# The commands below are the ones from docs.docker.com/engine/install/ubuntu ("Install using the
# apt repository"), made non-interactive.
# ---------------------------------------------------------------------------------------------
step "2/7 packages"
export DEBIAN_FRONTEND=noninteractive
apt_q() { apt-get -y -q -o DPkg::Lock::Timeout=600 "$@"; }
apt_q update
if [ "$FW" = "ufw" ]; then
    apt_q install ca-certificates curl git ufw
else
    apt_q install ca-certificates curl git
fi
if docker compose version >/dev/null 2>&1; then
    log "docker: already installed ($(docker --version); $(docker compose version))"
else
    # Docker's guide: remove conflicting packages first (only the ones actually installed).
    for p in docker.io docker-compose docker-compose-v2 docker-doc podman-docker containerd runc; do
        if dpkg -s "$p" >/dev/null 2>&1; then
            log "docker: removing conflicting package $p"
            apt_q remove "$p"
        fi
    done
    install -m 0755 -d /etc/apt/keyrings
    curl -fsSL https://download.docker.com/linux/ubuntu/gpg -o /etc/apt/keyrings/docker.asc
    chmod a+r /etc/apt/keyrings/docker.asc
    cat > /etc/apt/sources.list.d/docker.sources <<EOF
Types: deb
URIs: https://download.docker.com/linux/ubuntu
Suites: $(. /etc/os-release && echo "${UBUNTU_CODENAME:-$VERSION_CODENAME}")
Components: stable
Architectures: $(dpkg --print-architecture)
Signed-By: /etc/apt/keyrings/docker.asc
EOF
    apt_q update
    apt_q install docker-ce docker-ce-cli containerd.io docker-buildx-plugin docker-compose-plugin
    log "docker: installed ($(docker --version); $(docker compose version))"
fi
systemctl enable --now docker >/dev/null 2>&1 || systemctl start docker
docker info >/dev/null 2>&1 || die "docker daemon is not running (systemctl status docker)"

# ---------------------------------------------------------------------------------------------
# 3. Firewall: SSH 22, authserver 3724, worldserver 8085
# NOTE (docs.docker.com): "when you expose container ports using Docker, these ports bypass your
# firewall rules." So the host firewall alone cannot hide Docker's published ports - step 5 therefore
# binds the database (3306) and SOAP (7878) ports to 127.0.0.1 only.
#
# Oracle (FW=iptables): the image's INPUT chain ends with a REJECT rule and allows only SSH before it.
# The two game ports are added the way Oracle's known-issue note says - in /etc/iptables/rules.v4
# (loaded at boot), in the same form as the image's own SSH line, just before the first INPUT REJECT -
# and inserted live at the same place. Not via `iptables-restore`, which would flush Docker's rules.
# Oracle's VCN security list must allow 3724 and 8085 too (README, Oracle step 3); that part is in the
# Oracle console, not on the server.
# ---------------------------------------------------------------------------------------------
step "3/7 firewall"
if [ "$FW" = "iptables" ]; then
    if command -v ufw >/dev/null 2>&1 && ufw status 2>/dev/null | grep -q "Status: active"; then
        log "WARNING: ufw is active on an image whose provider says not to use it (see step 3 notes) - leaving it alone"
    fi
    RULES=/etc/iptables/rules.v4
    for port in 3724 8085; do
        spec="-p tcp -m state --state NEW -m tcp --dport $port -j ACCEPT"
        if grep -qxF -- "-A INPUT $spec" "$RULES"; then
            log "iptables: $RULES already allows tcp $port"
        else
            [ -e "$RULES.before-wow-setup" ] || cp -p "$RULES" "$RULES.before-wow-setup"
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
            log "iptables: added 'tcp $port ACCEPT' to $RULES (backup: $RULES.before-wow-setup)"
        fi
        # shellcheck disable=SC2086
        if iptables -C INPUT $spec 2>/dev/null; then
            log "iptables: tcp $port already allowed (live)"
        else
            n=$(iptables -L INPUT --line-numbers -n | awk '$2 == "REJECT" { print $1; exit }')
            # shellcheck disable=SC2086
            if [ -n "$n" ]; then iptables -I INPUT "$n" $spec; else iptables -A INPUT $spec; fi
            log "iptables: tcp $port allowed (live, INPUT position ${n:-end})"
        fi
    done
    FW_SUMMARY="iptables ($RULES): 22, 3724, 8085 allowed; the Oracle security list must allow 3724 and 8085 too"
else
    ufw allow 22/tcp comment 'SSH' >/dev/null
    ufw allow 3724/tcp comment 'WoW authserver' >/dev/null
    ufw allow 8085/tcp comment 'WoW worldserver' >/dev/null
    ufw --force enable >/dev/null
    FW_SUMMARY="ufw $(ufw status | head -1 | sed 's/^Status: //'): 22, 3724, 8085 allowed"
fi
log "firewall: $FW_SUMMARY"

# ---------------------------------------------------------------------------------------------
# 4. AzerothCore repository (shallow clone, as the wiki does: git clone ... --depth 1)
# The repo provides docker-compose.yml; the programs come as prebuilt images (step 6).
# ---------------------------------------------------------------------------------------------
step "4/7 azerothcore-wotlk"
if [ -d "$AC_DIR/.git" ]; then
    log "repo: $AC_DIR already cloned (commit $(git -C "$AC_DIR" rev-parse --short HEAD)); not updating it (README: Update)"
else
    if [ -e "$AC_DIR" ] && [ -n "$(ls -A "$AC_DIR" 2>/dev/null)" ]; then
        die "$AC_DIR exists, is not empty and is not a git checkout - move it away and re-run"
    fi
    git clone --depth 1 "$AC_REPO" "$AC_DIR"
    log "repo: cloned $AC_REPO (commit $(git -C "$AC_DIR" rev-parse --short HEAD))"
fi
cd "$AC_DIR"
[ -f docker-compose.yml ] || die "$AC_DIR/docker-compose.yml missing - repository layout changed?"

# ---------------------------------------------------------------------------------------------
# 5. Configuration (only through the mechanisms AzerothCore documents; tracked files untouched)
#
# a) .env in the repo root - conf/dist/env.docker: "Create a .env file in the root folder and use
#    the following variables to configure your docker compose". docker-compose.yml maps
#      - ${DOCKER_DB_EXTERNAL_PORT:-3306}:3306      (MySQL, root password default "password")
#      - ${DOCKER_SOAP_EXTERNAL_PORT:-7878}:7878    (SOAP, disabled in worldserver.conf.dist)
#    Setting them to 127.0.0.1:<port> gives Compose's "[IP:]HOST:CONTAINER" form, so neither port is
#    reachable from the internet. Auth 3724 and world 8085 stay public (defaults unchanged).
# b) docker-compose.override.yml - docker-compose.yml: "Don't make changes this file! make a
#    `docker-compose.override.yml` and make your changes there instead."
#    Warden OFF: worldserver.conf.dist "Warden.Enabled = 1" (Default 1). Config.cpp reads the env var
#    GetEnvVarName(name) = "AC_" + IniKeyToEnvVarKey(name) before the file, and IniKeyToEnvVarKey turns
#    "Warden.Enabled" into "WARDEN_ENABLED" ('.' -> '_', upper case) => AC_WARDEN_ENABLED = 0.
# Both files are git-ignored by AzerothCore (.gitignore: /.env*  and  /*.override.yml), so updates
# (git pull) keep them.
# ---------------------------------------------------------------------------------------------
step "5/7 configuration"
set_env() {  # set_env KEY VALUE  -> sets/replaces KEY=VALUE in .env, leaves every other line alone
    local k=$1 v=$2 f="$AC_DIR/.env"
    touch "$f"
    if grep -q "^${k}=" "$f"; then
        sed -i "s|^${k}=.*|${k}=${v}|" "$f"
    else
        printf '%s=%s\n' "$k" "$v" >> "$f"
    fi
}
set_env DOCKER_DB_EXTERNAL_PORT "127.0.0.1:3306"
set_env DOCKER_SOAP_EXTERNAL_PORT "127.0.0.1:7878"
chmod 600 "$AC_DIR/.env"
log "config: .env -> MySQL 3306 and SOAP 7878 bound to 127.0.0.1 only"

OVR="$AC_DIR/docker-compose.override.yml"
if [ -f "$OVR" ] && ! grep -qF "$MARK" "$OVR"; then
    if grep -qE 'AC_WARDEN_ENABLED:[[:space:]]*"?0"?' "$OVR"; then
        log "config: keeping your own $OVR (it sets AC_WARDEN_ENABLED to 0)"
    else
        die "$OVR exists (not written by this script) and does not set AC_WARDEN_ENABLED: \"0\" under services.ac-worldserver.environment - add that, then re-run"
    fi
else
    cat > "$OVR" <<EOF
$MARK
# Warden OFF. worldserver.conf.dist: "Warden.Enabled = 1" (Default 1).
# AzerothCore's ConfigMgr (src/common/Configuration/Config.cpp) reads the environment variable
# "AC_" + IniKeyToEnvVarKey("Warden.Enabled") = AC_WARDEN_ENABLED before the config file.
services:
  ac-worldserver:
    environment:
      AC_WARDEN_ENABLED: "0"
EOF
    if [ "$BUILD" = 1 ]; then
        # Compiled here: the three services that share the Dockerfile's "build" stage get the SAME
        # build args, so BuildKit compiles once and reuses that stage for the other two.
        cat >> "$OVR" <<EOF
    build:
      args:
        CTOOLS_BUILD: "$BUILD_TOOLS"
  ac-authserver:
    build:
      args:
        CTOOLS_BUILD: "$BUILD_TOOLS"
  ac-db-import:
    build:
      args:
        CTOOLS_BUILD: "$BUILD_TOOLS"
EOF
    fi
    log "config: wrote $OVR (AC_WARDEN_ENABLED: \"0\"$( [ "$BUILD" = 1 ] && echo ", CTOOLS_BUILD: $BUILD_TOOLS"))"
fi

# apps/docker/entrypoint.sh warns that etc/logs must be writable by the container user and that this
# "is usually caused by cloning the repository as root" - we cloned as root, so hand them to uid 1000.
mkdir -p "$AC_DIR/env/dist/etc" "$AC_DIR/env/dist/logs"
chown -R "$AC_UID:$AC_GID" "$AC_DIR/env/dist/etc" "$AC_DIR/env/dist/logs"
log "config: env/dist/etc and env/dist/logs owned by $AC_UID:$AC_GID"

# ---------------------------------------------------------------------------------------------
# 6. Start with AzerothCore's own docker compose
# docker-compose.yml names images acore/ac-wotlk-{db-import,worldserver,authserver,client-data}:
# ${DOCKER_IMAGE_TAG:-master}.
#  - amd64: AzerothCore publishes these on Docker Hub; we pull them (nothing is compiled).
#  - arm64 (BUILD=1): no arm64 images exist, so they are built from the clone with AzerothCore's own
#    Dockerfile (apps/docker/Dockerfile, the build: sections of docker-compose.yml) under the same
#    names. One compile, first run only: roughly 1-3 hours on 2 Ampere cores (not measured yet).
#    MySQL comes from the official multi-arch mysql:8.4 image either way.
# Both start with --no-build afterwards.
# Wiki: "The ac-client-data container will automatically download the client data to a volume,
# and the dbimport will execute database migrations. Both of these operations can take 10 to 15
# minutes on the first run." The worldserver waits for both (depends_on in docker-compose.yml).
# ---------------------------------------------------------------------------------------------
step "6/7 docker compose"
# Pull/build only when an image is missing, so running this script again never silently updates the
# server (README "Update" does that on purpose).
docker compose config --quiet || die "docker compose rejects the configuration (docker-compose.yml + override + .env)"
missing_images=""
for img in $(docker compose config --images | grep -v -e ac-wotlk-tools -e ac-wotlk-dev-server); do
    docker image inspect "$img" >/dev/null 2>&1 || missing_images="$missing_images $img"
done
if [ -z "$missing_images" ]; then
    log "compose: all images already present ($(docker compose config --images | grep -v -e ac-wotlk-tools -e ac-wotlk-dev-server | tr '\n' ' '))"
elif [ "$BUILD" = 1 ]; then
    log "compose: missing:$missing_images"
    case "$missing_images" in
        *mysql:*) log "compose: pulling mysql (official multi-arch image)"; docker compose pull ac-database ;;
    esac
    if printf '%s' "$missing_images" | grep -q 'acore/'; then
        export BUILDKIT_PROGRESS=plain
        t0=$(date +%s)
        log "compose: compiling AzerothCore for $ARCH (first run only; the full compiler output follows)"
        # The worldserver first (it runs the one compile); then the others reuse the cached build stage.
        docker compose build ac-worldserver || die "the AzerothCore compile failed (compiler errors above)"
        docker compose build ac-authserver ac-db-import ac-client-data-init || die "building the remaining images failed (see above)"
        log "compose: images built in $(( ($(date +%s) - t0) / 60 )) min"
    fi
else
    log "compose: pulling prebuilt images:$missing_images"
    docker compose pull
fi
show_diag() {
    docker ps -a --filter "name=ac-" --format 'table {{.Names}}\t{{.Status}}' || true
    for c in "$@"; do
        log "--- last 30 log lines of $c ---"
        docker logs --tail 30 "$c" 2>&1 || true
    done
}
log "compose: starting (first run: client data 1.2 GB download + database import, 10-15+ minutes)"
if ! docker compose up -d --no-build; then
    show_diag ac-client-data-init ac-db-import ac-database
    die "docker compose up failed (see the container logs above)"
fi

state_of()   { docker inspect -f '{{.State.Status}}' "$1" 2>/dev/null || echo missing; }
exitcode_of(){ docker inspect -f '{{.State.ExitCode}}' "$1" 2>/dev/null || echo -1; }
# wait_until SECONDS DESCRIPTION COMMAND... - polls every 10 s
wait_until() {
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
oneshot_done() {  # one-shot container finished; dies at once if it failed
    local c=$1 st rc
    st=$(state_of "$c")
    [ "$st" = "exited" ] || return 1
    rc=$(exitcode_of "$c")
    if [ "$rc" != "0" ]; then show_diag "$c"; die "$c exited with code $rc"; fi
    return 0
}
wait_until $((WAIT_DATA_MIN * 60)) "client data in volume (ac-client-data-init)" oneshot_done ac-client-data-init \
    || { show_diag ac-client-data-init; die "client data not ready after $WAIT_DATA_MIN min"; }
wait_until $((WAIT_DATA_MIN * 60)) "database import (ac-db-import)" oneshot_done ac-db-import \
    || { show_diag ac-db-import; die "database import not finished after $WAIT_DATA_MIN min"; }

# ---------------------------------------------------------------------------------------------
# 7. Realm address, readiness, status
# ---------------------------------------------------------------------------------------------
step "7/7 realm address and readiness"
# SQL runs INSIDE the ac-database container with the password from its own MYSQL_ROOT_PASSWORD
# environment (set by docker-compose.yml), so this script never sees or logs a password.
db() {
    docker exec ac-database sh -c 'exec mysql -uroot -p"$MYSQL_ROOT_PASSWORD" -N -s -e "$1"' sh "$1" 2>&1 \
        | { grep -v 'Using a password on the command line' || true; }
}
is_ipv4() { [[ $1 =~ ^([0-9]{1,3}\.){3}[0-9]{1,3}$ ]]; }

if [ -n "${PUBLIC_IP:-}" ]; then
    log "ip: using PUBLIC_IP=$PUBLIC_IP from the environment"
else
    # DigitalOcean metadata service (docs.digitalocean.com "Retrieve Droplet Metadata").
    PUBLIC_IP=$(curl -fsS -m 5 http://169.254.169.254/metadata/v1/interfaces/public/0/ipv4/address 2>/dev/null || true)
    if is_ipv4 "$PUBLIC_IP"; then
        log "ip: $PUBLIC_IP (DigitalOcean metadata)"
    else
        # Fallback: source address of the default route (on a droplet eth0 carries the public IPv4).
        PUBLIC_IP=$(ip -4 route get 1.1.1.1 2>/dev/null | awk '{for (i = 1; i < NF; i++) if ($i == "src") print $(i + 1)}' | head -1)
        log "ip: $PUBLIC_IP (default route source address)"
    fi
fi
is_ipv4 "$PUBLIC_IP" || die "could not determine the public IPv4 - re-run with: sudo PUBLIC_IP=<public ip> bash $0"
case "$PUBLIC_IP" in
    10.*|192.168.*|172.1[6-9].*|172.2[0-9].*|172.3[01].*|127.*)
        log "WARNING: $PUBLIC_IP is a private address - players outside this network cannot reach it (set PUBLIC_IP)";;
esac

# RealmID = 1 in worldserver.conf.dist ("RealmID must match the realmlist inside the auth database").
rows=$(db "SELECT COUNT(*) FROM acore_auth.realmlist WHERE id = 1;")
[ "$rows" = "1" ] || die "acore_auth.realmlist has no row id 1 (got: $rows)"
db "UPDATE acore_auth.realmlist SET address = '$PUBLIC_IP' WHERE id = 1;" >/dev/null
got=$(db "SELECT address FROM acore_auth.realmlist WHERE id = 1;")
[ "$got" = "$PUBLIC_IP" ] || die "realmlist address is '$got', expected '$PUBLIC_IP'"
log "realm: acore_auth.realmlist id 1 address = $got (authserver re-reads it every RealmsStateUpdateDelay = 20 s)"

# worldserver Main.cpp logs "<version> (worldserver-daemon) ready..." once it accepts players.
# The port checks run inside each container (bash /dev/tcp on 127.0.0.1): a host-side connect would
# succeed on Docker's proxy even when the server inside is not listening yet.
world_ready() {
    [ "$(state_of ac-worldserver)" = "running" ] || return 1
    docker logs ac-worldserver 2>&1 | grep -F "(worldserver-daemon) ready" >/dev/null || return 1
    docker exec ac-worldserver bash -c '</dev/tcp/127.0.0.1/8085' >/dev/null 2>&1
}
auth_ready() {
    [ "$(state_of ac-authserver)" = "running" ] || return 1
    docker exec ac-authserver bash -c '</dev/tcp/127.0.0.1/3724' >/dev/null 2>&1
}
wait_until $((WAIT_WORLD_MIN * 60)) "worldserver listening on 8085" world_ready \
    || { show_diag ac-worldserver; die "worldserver not ready after $WAIT_WORLD_MIN min"; }
wait_until 300 "authserver listening on 3724" auth_ready \
    || { show_diag ac-authserver; die "authserver not ready after 5 min"; }

WARDEN=$(docker exec ac-worldserver printenv AC_WARDEN_ENABLED 2>/dev/null || true)
[ "$WARDEN" = "0" ] || die "AC_WARDEN_ENABLED inside ac-worldserver is '$WARDEN', expected 0 - check $OVR"
# Config.cpp logs this when the env value differs from the file ("Found config value ... from environment variable ...").
WLINE=$(docker logs ac-worldserver 2>&1 | grep -F "AC_WARDEN_ENABLED" | tail -1 | tr -d '\r' || true)

printf '\n'
log "================= AzerothCore realm status ================="
docker ps -a --filter "name=ac-" --format 'table {{.Names}}\t{{.Status}}'
echo "Realm address (acore_auth.realmlist id 1): $got"
echo "Warden:      OFF (AC_WARDEN_ENABLED=$WARDEN)${WLINE:+  log: ${WLINE}}"
echo "Auth 3724:   listening    World 8085: listening"
echo "Firewall:    $FW_SUMMARY; MySQL/SOAP on 127.0.0.1 only"
echo "Memory:      $(free -m | awk '/^Mem:/{printf "%d/%d MB used", $3, $2} /^Swap:/{printf ", swap %d/%d MB", $3, $2}')"
echo "Disk /:      $(df -h --output=used,size / | tail -1 | awk '{print $1 " used of " $2}')"
echo
echo "Next: create your account in the worldserver console (README.md, 'Create your account')."
echo "Normal 3.3.5a clients: set realmlist $got"
echo "Dreamport login panel: server $got (port 3724)"
log "SETUP OK"
