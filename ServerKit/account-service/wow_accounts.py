#!/usr/bin/env python3
# =============================================================================================
# Dreamport ServerKit - wow_accounts.py : automatic WoW accounts bound to the Somnium account
#
# One file, python3 standard library only (Ubuntu 24.04 has python 3.12). It is
#   1. the account service (HTTP on port 8086) the WoW Dreamport calls when a player presses
#      "Play online as <nickname>": it creates that player's WoW account in AzerothCore;
#   2. the code behind the admin tool `wow-account` (create / unlock / allow / gm / list);
#   3. its own test: --selftest (no database, no root needed) and --dry-run (no database writes).
#
# How an automatic account works (PLAN8 section 2):
#   - account name = "S" + the first 16 hex digits of the player's Somnium ID (a GUID), upper case;
#   - the password is a random 16-character secret made by the Dreamport and kept only in the player's
#     own Somnium storage. It never reaches this service: the Dreamport sends only the SRP6 salt and
#     verifier (the one-way values AzerothCore itself stores), so this service never sees a password;
#   - an existing account is NEVER overwritten, except once after the admin ran `wow-account unlock`;
#   - only players in /etc/wow-accounts/allow.txt are served; only Somnium IDs in gm.txt become GM.
#
# Database writes are the ones AzerothCore's AccountMgr::CreateAccount / UpdateAccountAccess /
# ChangePassword make (statements quoted from src/server/database/Database/Implementation/
# LoginDatabase.cpp), run INSIDE the ac-database container with the SQL on stdin:
#   docker exec -i ac-database sh -c 'exec mysql -uroot -p"$MYSQL_ROOT_PASSWORD" -N -B acore_auth'
# so this program never sees or logs the database password. Every value is validated against a strict
# pattern (name [A-Z0-9]{1,17}, hex [0-9A-F]{64}, ids digits) before it is placed in SQL.
#
# Usage (as root on the server; the systemd unit and the admin tool call it like this):
#   wow_accounts.py [--bind 0.0.0.0] [--port 8086]          run the service
#   wow_accounts.py --dry-run [--bind 127.0.0.1] [--port N]  service without database writes: prints
#                                                            the SQL it would run, pretends the
#                                                            database starts empty
#   wow_accounts.py --selftest                               test vector + all offline checks
#   wow_accounts.py create NAME          (password on stdin) normal account for a normal WoW client
#   wow_accounts.py unlock WHO                               allow one re-key within 24 hours
#   wow_accounts.py allow WHO | allow --remove WHO           edit allow.txt
#   wow_accounts.py gm WHO on|off                            GM level 3 on all realms / none
#   wow_accounts.py list                                     accounts, allow list, GM list, unlocks
#   wow_accounts.py dbcheck                                  read-only database test (connection, table columns)
# WHO = an account name, a Somnium ID, or a Somnium nickname this service has registered before.
# --etc-dir / --state-dir move /etc/wow-accounts and /var/lib/wow-accounts (used by the selftest).
# =============================================================================================
"""Dreamport automatic WoW accounts: service, admin commands and self-test (see the header)."""

import argparse
import contextlib
import hashlib
import http.client
import io
import json
import os
import re
import secrets
import shutil
import socket
import socketserver
import stat
import subprocess
import sys
import tempfile
import threading
import time
import unicodedata
from collections import deque
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
from urllib.parse import urlsplit

VERSION = 1
SERVICE_NAME = "wow-accounts"
DEFAULT_PORT = 8086
DEFAULT_BIND = "0.0.0.0"
ETC_DIR = "/etc/wow-accounts"
STATE_DIR = "/var/lib/wow-accounts"
CONTAINER = "ac-database"
DATABASE = "acore_auth"

MAX_BODY = 2048              # PLAN8 2.4: body <= 2048 bytes
READ_DEADLINE = 10.0         # PLAN8 2.4: connection timeout 10 s (whole request must arrive in 10 s)
DB_TIMEOUT = 6.0             # lock wait + database stay under the Dreamport's 10 s read timeout
DB_LOCK_WAIT = 3.0           # registrations run one at a time; a waiting one gives up after this
MAX_WORKERS = 8              # at most 8 connections are handled at once; more are closed at once
MAX_PER_IP = 2               # at most 2 of them from one internet address (the Dreamport uses one at a time),
                             # so one address cannot hold every slot with connections that send nothing
RATE_PER_IP = (5, 600)       # PLAN8 2.4: 5 register calls per source IP per 10 minutes (every call counts)
RATE_GLOBAL = (60, 3600)     # PLAN8 2.4: 60 register calls per hour in total (only correct calls of allowed players)
UNLOCK_SECONDS = 24 * 3600   # PLAN8 2.5: an unlock is valid for 24 hours
MAX_ACCOUNT_STR = 17         # AccountMgr.h: #define MAX_ACCOUNT_STR 17
MAX_PASS_STR = 16            # AccountMgr.h: #define MAX_PASS_STR 16
MAX_NICK = 64
GM_LEVEL = 3                 # SEC_ADMINISTRATOR; README step 3 uses "account set gmlevel NAME 3 -1"
EXPANSION = 2                # worldserver.conf.dist "Expansion = 2" (CONFIG_EXPANSION, AccountMgr::CreateAccount)

# SRP6.cpp: N = HexStrToByteArray<32>("894B...9BB7", true) (hex is big-endian, reversed into the
# little-endian array BigNumber reads), g = { 7 }.
N_HEX = "894B645E89E1535BBDAD5B8B290650530801B18EBFBF5E8FAB3C82872A3E9BB7"
N = int(N_HEX, 16)
G = 7

# PLAN8 2.3 test vector. EXPECTED_VECTOR_VERIFIER was computed independently with .NET
# System.Numerics.BigInteger (not with this file); the Dreamport's C# probe AccountVector must print it too.
VECTOR_SOMNIUM_ID = "cf5c553c-61f8-43f4-a290-4985ca328eb4"
VECTOR_NAME = "SCF5C553C61F843F4"
VECTOR_SECRET = "ABCDEFGHJKLMNPQR"
VECTOR_SALT = bytes(range(32))
EXPECTED_VECTOR_VERIFIER = "D1396E7B6A7ABF12EC485ED31A9C88955EB3FE30023BB1B301D0A0F3726D1749"

_GUID_RE = re.compile(r"[0-9A-Fa-f]{8}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{12}")
_NAME_RE = re.compile(r"[A-Z0-9]{1,17}")
_AUTO_NAME_RE = re.compile(r"S[0-9A-F]{16}")
_HEX64_ANY_RE = re.compile(r"[0-9A-Fa-f]{64}")
_HEX64_RE = re.compile(r"[0-9A-F]{64}")
_DIGITS_RE = re.compile(r"[0-9]{1,10}")
_CONTENT_LENGTH_RE = re.compile(r"[0-9]{1,7}")
_PASSWORD_RE = re.compile(r"[!-~]{1,16}")     # printable ASCII without space, 1-16 (MAX_PASS_STR)
_LONG_HEX_RE = re.compile(r"[0-9A-Fa-f]{32,}")


# ---------------------------------------------------------------------------------------------
# Logging (journald adds the time stamps). Never passwords, salts or verifiers.
# ---------------------------------------------------------------------------------------------
_log_lock = threading.Lock()
_log_capture = None                 # the selftest collects the service's log lines here instead


def log(msg):
    with _log_lock:
        if _log_capture is not None:
            _log_capture.append(msg)
            return
        sys.stderr.write(msg + "\n")
        sys.stderr.flush()


def safe(value, limit=80):
    """A value as a short, quoted, pure-ASCII string (control characters escaped) for log lines."""
    text = value if isinstance(value, str) else repr(value)
    return json.dumps(text[:limit], ensure_ascii=True)


# ---------------------------------------------------------------------------------------------
# PLAN8 2.1 / 2.3: account name and SRP6 registration values (AzerothCore SRP6.cpp)
# ---------------------------------------------------------------------------------------------
def name_for(somnium_id):
    """PLAN8 2.1: GUID -> "S" + first 16 hex digits (dashes removed, upper case); None if not a GUID."""
    if not isinstance(somnium_id, str) or not _GUID_RE.fullmatch(somnium_id):
        return None
    return "S" + somnium_id.replace("-", "").upper()[:16]


def upper_latin(text):
    """AzerothCore Utf8ToUpperOnlyLatin: only a-z become A-Z."""
    return "".join(chr(ord(c) - 32) if "a" <= c <= "z" else c for c in text)


def calculate_verifier(upper_user, upper_pass, salt):
    """SRP6::CalculateVerifier: v = g ^ SHA1(salt, SHA1(user, ":", pass)) mod N, 32 bytes little-endian."""
    salt = bytes(salt)
    if len(salt) != 32:
        raise ValueError("salt must be 32 bytes")
    inner = hashlib.sha1(upper_user.encode("utf-8") + b":" + upper_pass.encode("utf-8")).digest()
    x = int.from_bytes(hashlib.sha1(salt + inner).digest(), "little")
    return pow(G, x, N).to_bytes(32, "little")


def make_registration(upper_user, upper_pass):
    """SRP6::MakeRegistrationData: random 32-byte salt + verifier."""
    salt = secrets.token_bytes(32)
    return salt, calculate_verifier(upper_user, upper_pass, salt)


def srp_login_matches(upper_user, upper_pass, salt, verifier):
    """A full SRP6 exchange: the 3.3.5a client side (password) against AzerothCore's server side
    (salt + verifier, SRP6.cpp _B / VerifyChallengeResponse). True when both reach the same S."""
    v = int.from_bytes(verifier, "little")
    b = int.from_bytes(secrets.token_bytes(32), "little")
    big_b = (pow(G, b, N) + 3 * v) % N                                  # _B: (g^b + v*3) % N
    a = int.from_bytes(secrets.token_bytes(32), "little")
    big_a = pow(G, a, N)
    u = int.from_bytes(hashlib.sha1(big_a.to_bytes(32, "little") + big_b.to_bytes(32, "little")).digest(), "little")
    inner = hashlib.sha1(upper_user.encode("utf-8") + b":" + upper_pass.encode("utf-8")).digest()
    x = int.from_bytes(hashlib.sha1(bytes(salt) + inner).digest(), "little")
    s_client = pow((big_b - 3 * pow(G, x, N)) % N, a + u * x, N)
    s_server = pow(big_a * pow(v, u, N) % N, b, N)                    # (A * v^u)^b mod N
    return s_client == s_server


# ---------------------------------------------------------------------------------------------
# Validation of what the Dreamport sends (PLAN8 2.4)
# ---------------------------------------------------------------------------------------------
class BadRequest(Exception):
    """Malformed request; the message is plain English for the player."""


def hex64(value):
    """64 hex characters (any case) -> upper case; None otherwise."""
    if isinstance(value, str) and _HEX64_ANY_RE.fullmatch(value):
        return value.upper()
    return None


def bad_text(text):
    """True if text has characters that do not belong in a nickname (control, line breaks, surrogates)."""
    for c in text:
        if unicodedata.category(c) in ("Cc", "Cs", "Zl", "Zp"):
            return True
    return False


def clean_nick(text):
    """The nickname the Dreamport sent, made harmless for matching, log lines and players.tsv: control,
    format and line/paragraph separator characters become spaces, lone surrogates U+FFFD, then trimmed.
    (Real Somnium nicknames have none of these; the nickname is only a label and an allow-list key.)"""
    out = []
    for c in text:
        cat = unicodedata.category(c)
        if cat == "Cs":
            out.append("�")
        elif cat in ("Cc", "Cf", "Zl", "Zp"):
            out.append(" ")
        else:
            out.append(c)
    return "".join(out).strip()


def _unique_keys(pairs):
    obj = {}
    for key, value in pairs:
        if key in obj:
            raise ValueError("duplicate key")
        obj[key] = value
    return obj


def _no_constants(name):
    raise ValueError("not JSON: " + name)


def parse_register(body, content_type):
    """Checks a /register request completely. Returns a dict with somniumId (lower case), nick, name,
    salt, verifier (upper-case hex). Raises BadRequest."""
    ctype = (content_type or "").split(";", 1)[0].strip().lower()
    if ctype != "application/json":
        raise BadRequest("Content-Type must be application/json.")
    if len(body) > MAX_BODY:
        raise BadRequest("The request is too big.")
    try:
        text = body.decode("utf-8")
    except UnicodeDecodeError:
        raise BadRequest("The request is not UTF-8 text.")
    try:
        obj = json.loads(text, object_pairs_hook=_unique_keys, parse_constant=_no_constants)
    except (ValueError, RecursionError):
        raise BadRequest("The request is not valid JSON.")
    if not isinstance(obj, dict):
        raise BadRequest("The request must be a JSON object.")
    ver = obj.get("v")
    if type(ver) is not int or ver != VERSION:
        raise BadRequest("Unsupported request version (v must be 1).")
    sid = obj.get("somniumId")
    name = name_for(sid)
    if name is None:
        raise BadRequest("somniumId is not a Somnium ID.")
    nick = obj.get("nick", "")
    if nick is None:
        nick = ""
    if not isinstance(nick, str) or len(nick) > MAX_NICK:
        raise BadRequest("nick is not a valid Somnium nickname.")
    nick = clean_nick(nick)
    salt = hex64(obj.get("salt"))
    if salt is None:
        raise BadRequest("salt must be 64 hex characters.")
    verifier = hex64(obj.get("verifier"))
    if verifier is None:
        raise BadRequest("verifier must be 64 hex characters.")
    vnum = int.from_bytes(bytes.fromhex(verifier), "little")
    if vnum == 0 or vnum >= N:                  # g^x mod N is never 0 and always < N
        raise BadRequest("verifier is not a valid SRP6 verifier.")
    return {"somniumId": sid.lower(), "nick": nick, "name": name, "salt": salt, "verifier": verifier}


# ---------------------------------------------------------------------------------------------
# SQL (values validated here, right before they are placed in the text)
# ---------------------------------------------------------------------------------------------
def q_name(name):
    if not isinstance(name, str) or not _NAME_RE.fullmatch(name):
        raise ValueError("refusing unsafe account name in SQL")
    return "'" + name + "'"


def q_hex(value):
    if not isinstance(value, str) or not _HEX64_RE.fullmatch(value):
        raise ValueError("refusing unsafe hex value in SQL")
    return "UNHEX('" + value + "')"


def sql_register(name, salt_hex, verifier_hex, gm, allow_rekey):
    """One transaction: exists-check, AccountMgr::CreateAccount's two statements, GM (optional),
    re-key (only when unlocked). Prints one row: created, rekeyed, account id."""
    n = q_name(name)
    s = q_hex(salt_hex)
    v = q_hex(verifier_hex)
    sql = [
        "START TRANSACTION;",
        # AccountMgr::CreateAccount: "if (GetId(username)) return AOR_NAME_ALREADY_EXIST;"
        # (LOGIN_GET_ACCOUNT_ID_BY_USERNAME "SELECT id FROM account WHERE username = ?"), here inside the transaction.
        "SET @wa_exists = (SELECT COUNT(*) FROM account WHERE username = %s);" % n,
        # LOGIN_INS_ACCOUNT, only when the name is free (expansion = CONFIG_EXPANSION 2, reg_mail = email = '').
        "INSERT INTO account(username, salt, verifier, expansion, reg_mail, email, joindate) "
        "SELECT %s, %s, %s, %d, '', '', NOW() FROM DUAL WHERE @wa_exists = 0;" % (n, s, v, EXPANSION),
        "SET @wa_created = ROW_COUNT();",
        "SET @wa_id = (SELECT id FROM account WHERE username = %s);" % n,
        # LOGIN_INS_REALM_CHARACTERS_INIT, only when the account was created by this transaction.
        "INSERT INTO realmcharacters (realmid, acctid, numchars) SELECT realmlist.id, account.id, 0 "
        "FROM realmlist, account LEFT JOIN realmcharacters ON acctid=account.id WHERE acctid IS NULL AND @wa_created = 1;",
    ]
    if gm:
        # AccountMgr::UpdateAccountAccess(realmId -1): LOGIN_DEL_ACCOUNT_ACCESS, then LOGIN_INS_ACCOUNT_ACCESS.
        sql.append("DELETE FROM account_access WHERE id = @wa_id AND @wa_created = 1;")
        sql.append("INSERT INTO account_access (id,gmlevel,RealmID) SELECT @wa_id, %d, -1 FROM DUAL WHERE @wa_created = 1;" % GM_LEVEL)
    if allow_rekey:
        # AccountMgr::ChangePassword: LOGIN_UPD_LOGON "UPDATE account SET salt = ?, verifier = ? WHERE id = ?".
        sql.append("UPDATE account SET salt = %s, verifier = %s WHERE id = @wa_id AND @wa_created = 0;" % (s, v))
        sql.append("SET @wa_rekeyed = ROW_COUNT();")
    else:
        sql.append("SET @wa_rekeyed = 0;")
    sql.append("COMMIT;")
    sql.append("SELECT @wa_created, @wa_rekeyed, IFNULL(@wa_id, 0);")
    return "\n".join(sql) + "\n"


def sql_account_id(name):
    return "SELECT IFNULL((SELECT id FROM account WHERE username = %s), 0);\n" % q_name(name)


def sql_set_gm(name, on):
    sql = [
        "START TRANSACTION;",
        "SET @wa_id = (SELECT id FROM account WHERE username = %s);" % q_name(name),
        # AccountMgr::UpdateAccountAccess with realmId -1: LOGIN_DEL_ACCOUNT_ACCESS "DELETE FROM account_access WHERE id = ?"
        "DELETE FROM account_access WHERE id = @wa_id;",
    ]
    if on:
        # LOGIN_INS_ACCOUNT_ACCESS "INSERT INTO account_access (id,gmlevel,RealmID) VALUES (?, ?, ?)"
        sql.append("INSERT INTO account_access (id,gmlevel,RealmID) SELECT @wa_id, %d, -1 FROM DUAL WHERE @wa_id IS NOT NULL;" % GM_LEVEL)
    sql.append("COMMIT;")
    sql.append("SELECT IFNULL(@wa_id, 0);")
    return "\n".join(sql) + "\n"


SQL_LIST = (
    "SELECT a.id, a.username, IFNULL((SELECT MAX(aa.gmlevel) FROM account_access aa WHERE aa.id = a.id), 0), "
    "IFNULL(DATE_FORMAT(a.last_login, '%Y-%m-%d %H:%i'), 'never'), a.online FROM account a ORDER BY a.id;\n"
)
SQL_COUNT = "SELECT COUNT(*) FROM account;\n"

# Read-only check that the tables have every column this service writes or reads (account.sql,
# account_access.sql, realmcharacters.sql of AzerothCore), and that realmlist has at least one realm.
SCHEMA_COLUMNS = (
    ("account", ("id", "username", "salt", "verifier", "expansion", "reg_mail", "email", "joindate", "last_login", "online")),
    ("account_access", ("id", "gmlevel", "RealmID")),
    ("realmcharacters", ("realmid", "acctid", "numchars")),
)
SQL_SCHEMA = "SELECT " + ", ".join(
    "(SELECT COUNT(*) FROM information_schema.columns WHERE table_schema = DATABASE() AND table_name = '%s' "
    "AND column_name IN (%s))" % (table, ", ".join("'%s'" % c for c in cols)) for table, cols in SCHEMA_COLUMNS
) + ", (SELECT COUNT(*) FROM realmlist);\n"


def schema_problem(counts):
    """None when the database looks like AzerothCore's acore_auth; otherwise plain words."""
    for (table, cols), got in zip(SCHEMA_COLUMNS, counts):
        if got != len(cols):
            return "table %s has %d of the %d expected columns (%s)" % (table, got, len(cols), ", ".join(cols))
    if counts[len(SCHEMA_COLUMNS)] < 1:
        return "the realmlist table is empty (no realm)"
    return None


def redact(sql, *values):
    """The SQL with the given salt/verifier hex values replaced by labels (for --dry-run output)."""
    for label, value in values:
        if value:
            sql = sql.replace(value, "<" + label + ": 64 hex, not shown>")
    return sql


# ---------------------------------------------------------------------------------------------
# Database access
# ---------------------------------------------------------------------------------------------
class DbError(Exception):
    pass


DOCKER_SQL_COMMAND = ["docker", "exec", "-i", CONTAINER, "sh", "-c",
                      'exec mysql -uroot -p"$MYSQL_ROOT_PASSWORD" -N -B ' + DATABASE]


def clean_stderr(text):
    """mysql/docker error text for the log: without the password warning, long hex values redacted."""
    out = []
    for line in text.splitlines():
        line = line.strip()
        if not line or "Using a password on the command line" in line:
            continue
        out.append(_LONG_HEX_RE.sub("<hex>", line)[:300])
    return " | ".join(out)[:600]


def docker_mysql(sql):
    """Runs SQL (on stdin) in the ac-database container. Returns (exit code, stdout, cleaned stderr)."""
    try:
        proc = subprocess.run(DOCKER_SQL_COMMAND, input=sql.encode("utf-8"), stdout=subprocess.PIPE,
                              stderr=subprocess.PIPE, timeout=DB_TIMEOUT, check=False)
    except FileNotFoundError:
        return 127, "", "the docker command was not found"
    except subprocess.TimeoutExpired:
        return 124, "", "the database did not answer within %d s" % DB_TIMEOUT
    except OSError as e:
        return 126, "", "could not start docker: %s" % type(e).__name__
    return proc.returncode, proc.stdout.decode("utf-8", "replace"), clean_stderr(proc.stderr.decode("utf-8", "replace"))


def last_row(stdout, columns):
    rows = [line for line in stdout.splitlines() if line.strip()]
    if not rows:
        raise DbError("the database returned no result")
    parts = rows[-1].split("\t")
    if len(parts) != columns or not all(_DIGITS_RE.fullmatch(p) for p in parts):
        raise DbError("the database returned an unexpected result")
    return [int(p) for p in parts]


class MysqlStore:
    """The real database (through docker exec). `runner` is replaceable for the selftest."""

    def __init__(self, runner=docker_mysql):
        self.runner = runner

    def _run(self, sql):
        rc, out, err = self.runner(sql)
        if rc != 0:
            raise DbError(err or ("mysql exit code %d" % rc))
        return out

    def register(self, name, salt_hex, verifier_hex, gm, allow_rekey):
        """-> ("created"|"rekeyed"|"exists", account id). Raises DbError."""
        sql = sql_register(name, salt_hex, verifier_hex, gm, allow_rekey)
        rc, out, err = self.runner(sql)
        if rc != 0:
            # Another program created the same name between our check and insert: the unique key
            # idx_username refused it and the transaction was rolled back -> nothing changed.
            if "ERROR 1062" in err and "idx_username" in err:
                return "exists", 0
            raise DbError(err or ("mysql exit code %d" % rc))
        created, rekeyed, acct_id = last_row(out, 3)
        if created == 1:
            return "created", acct_id
        if rekeyed == 1:
            return "rekeyed", acct_id
        return "exists", acct_id

    def account_id(self, name):
        return last_row(self._run(sql_account_id(name)), 1)[0]

    def set_gm(self, name, on):
        return last_row(self._run(sql_set_gm(name, on)), 1)[0]

    def list_accounts(self):
        rows = []
        for line in self._run(SQL_LIST).splitlines():
            parts = line.split("\t")
            if len(parts) == 5:
                rows.append(parts)
        return rows

    def count_accounts(self):
        return last_row(self._run(SQL_COUNT), 1)[0]

    def schema(self):
        return last_row(self._run(SQL_SCHEMA), len(SCHEMA_COLUMNS) + 1)


class MemoryStore:
    """Pretend database (starts empty) for --dry-run and the selftest. Writes nothing anywhere."""

    def __init__(self, echo=False):
        self.echo = echo
        self.accounts = {}          # name -> [id, gm]
        self.next_id = 1
        self.lock = threading.Lock()

    def _show(self, sql, salt_hex=None, verifier_hex=None):
        if self.echo:
            text = redact(sql, ("salt", salt_hex), ("verifier", verifier_hex))
            sys.stdout.write("--- dry run: SQL that would run in %s (nothing was run) ---\n%s" % (DATABASE, text))
            sys.stdout.flush()

    def register(self, name, salt_hex, verifier_hex, gm, allow_rekey):
        self._show(sql_register(name, salt_hex, verifier_hex, gm, allow_rekey), salt_hex, verifier_hex)
        with self.lock:
            acct = self.accounts.get(name)
            if acct is None:
                acct_id = self.next_id
                self.next_id += 1
                self.accounts[name] = [acct_id, bool(gm)]
                return "created", acct_id
            if allow_rekey:
                return "rekeyed", acct[0]
            return "exists", acct[0]

    def account_id(self, name):
        self._show(sql_account_id(name))
        acct = self.accounts.get(name)
        return acct[0] if acct else 0

    def set_gm(self, name, on):
        self._show(sql_set_gm(name, on))
        acct = self.accounts.get(name)
        if not acct:
            return 0
        acct[1] = bool(on)
        return acct[0]

    def list_accounts(self):
        self._show(SQL_LIST)
        return [[str(a[0]), n, str(GM_LEVEL if a[1] else 0), "never", "0"] for n, a in sorted(self.accounts.items(), key=lambda kv: kv[1][0])]

    def count_accounts(self):
        self._show(SQL_COUNT)
        return len(self.accounts)

    def schema(self):
        self._show(SQL_SCHEMA)
        return [len(cols) for _, cols in SCHEMA_COLUMNS] + [1]


# ---------------------------------------------------------------------------------------------
# Files: allow.txt, gm.txt, unlock markers, players.tsv
# ---------------------------------------------------------------------------------------------
def read_list(path):
    """Entries of allow.txt / gm.txt, case-folded. None if the file is missing or unreadable.
    One entry per line; empty lines and lines starting with # are ignored; ' # text' after an entry too."""
    try:
        with open(path, "r", encoding="utf-8", errors="replace") as f:
            text = f.read(1000000)
    except FileNotFoundError:
        return None
    except OSError as e:
        log("cannot read %s (%s) - treated as empty" % (path, type(e).__name__))
        return None
    entries = []
    for raw in text.splitlines():
        line = raw.strip()
        if not line or line.startswith("#"):
            continue
        line = re.split(r"\s+#", line, maxsplit=1)[0].strip()
        if line:
            entries.append(line.casefold())
    return entries


class RateLimiter:
    """Sliding windows. allow_ip: every register call from a source IP counts (also broken or refused
    ones). allow_global: asked only for correct requests of allowed players, so junk and strangers
    cannot use up the budget of the players on the allow list. Only calls that pass are counted."""

    def __init__(self, per_ip=RATE_PER_IP, overall=RATE_GLOBAL):
        self.ip_max, self.ip_window = per_ip
        self.all_max, self.all_window = overall
        self.lock = threading.Lock()
        self.by_ip = {}
        self.everyone = deque()

    def allow_global(self, now=None):
        now = time.monotonic() if now is None else now
        with self.lock:
            while self.everyone and now - self.everyone[0] >= self.all_window:
                self.everyone.popleft()
            if len(self.everyone) >= self.all_max:
                return False
            self.everyone.append(now)
            return True

    def allow_ip(self, ip, now=None):
        now = time.monotonic() if now is None else now
        with self.lock:
            q = self.by_ip.get(ip)
            if q is not None:
                while q and now - q[0] >= self.ip_window:
                    q.popleft()
            if q is not None and len(q) >= self.ip_max:
                return False
            if q is None:
                q = deque()
                self.by_ip[ip] = q
            q.append(now)
            if len(self.by_ip) > 256:
                for key in list(self.by_ip):
                    d = self.by_ip[key]
                    while d and now - d[0] >= self.ip_window:
                        d.popleft()
                    if not d:
                        del self.by_ip[key]
            return True


class Accounts:
    """The service logic, independent of HTTP (the selftest calls it directly too)."""

    def __init__(self, store, etc_dir=ETC_DIR, state_dir=STATE_DIR, dry_run=False, limiter=None):
        self.store = store
        self.etc_dir = etc_dir
        self.state_dir = state_dir
        self.dry_run = dry_run
        self.limiter = limiter or RateLimiter()
        self.db_lock = threading.Lock()

    # --- paths
    @property
    def allow_path(self):
        return os.path.join(self.etc_dir, "allow.txt")

    @property
    def gm_path(self):
        return os.path.join(self.etc_dir, "gm.txt")

    @property
    def unlock_dir(self):
        return os.path.join(self.state_dir, "unlocked")

    @property
    def players_path(self):
        return os.path.join(self.state_dir, "players.tsv")

    def unlock_path(self, name):
        q_name(name)                                     # [A-Z0-9]{1,17}: no path tricks possible
        return os.path.join(self.unlock_dir, name)

    # --- unlock markers (PLAN8 2.5: written by `wow-account unlock NAME`, valid 24 h, consumed by the next register)
    def unlock_valid(self, name, now=None):
        path = self.unlock_path(name)
        now = time.time() if now is None else now
        try:
            st = os.lstat(path)
        except FileNotFoundError:
            return False
        if not stat.S_ISREG(st.st_mode):
            return False
        age = now - st.st_mtime
        if age < -300 or age > UNLOCK_SECONDS:
            if not self.dry_run:
                try:
                    os.unlink(path)
                    log("unlock for %s expired and was removed" % name)
                except OSError:
                    pass
            return False
        return True

    def write_unlock(self, name):
        os.makedirs(self.unlock_dir, mode=0o700, exist_ok=True)
        path = self.unlock_path(name)
        tmp = path + ".tmp"
        with open(tmp, "w", encoding="ascii") as f:
            f.write("unlocked %s UTC, valid 24 hours, removed by the next registration of %s\n"
                    % (time.strftime("%Y-%m-%d %H:%M:%S", time.gmtime()), name))
        os.chmod(tmp, 0o600)
        os.replace(tmp, path)

    def unlocked_names(self):
        try:
            names = sorted(os.listdir(self.unlock_dir))
        except OSError:
            return []
        return [n for n in names if _NAME_RE.fullmatch(n) and self.unlock_valid(n)]

    # --- players.tsv: which Somnium player owns which automatic account (for `list` and nicknames as WHO)
    def read_players(self):
        rows = []
        try:
            with open(self.players_path, "r", encoding="utf-8", errors="replace") as f:
                for line in f:
                    parts = line.rstrip("\n").split("\t")
                    if len(parts) >= 4 and _NAME_RE.fullmatch(parts[0]):
                        rows.append(parts[:5])
        except OSError:
            pass
        return rows

    def record_player(self, name, somnium_id, nick, event):
        if self.dry_run:
            return
        try:
            rows = {r[0]: r for r in self.read_players()}
            rows[name] = [name, somnium_id, nick, time.strftime("%Y-%m-%d %H:%M", time.gmtime()), event]
            os.makedirs(self.state_dir, mode=0o700, exist_ok=True)
            tmp = self.players_path + ".tmp"
            with open(tmp, "w", encoding="utf-8") as f:
                for key in sorted(rows):
                    f.write("\t".join(rows[key]) + "\n")
            os.chmod(tmp, 0o600)
            os.replace(tmp, self.players_path)
        except (OSError, ValueError) as e:
            log("could not update %s (%s)" % (self.players_path, type(e).__name__))

    # --- allow / gm lists
    def is_allowed(self, somnium_id, nick):
        return self.allow_check(somnium_id, nick)[0]

    def allow_check(self, somnium_id, nick):
        """-> (allowed, how). A Somnium ID in allow.txt is always allowed. A nickname in allow.txt lets in
        ONE Somnium account: the first that got an account here with that nickname (players.tsv). Anyone
        can send any nickname, so without this one allowed nickname would let anybody make accounts."""
        entries = read_list(self.allow_path) or []
        sid_key = somnium_id.casefold()
        if sid_key in entries:
            return True, "by Somnium ID"
        nick_key = nick.strip().casefold()
        if not nick_key or nick_key not in entries:
            return False, "not on the allow list"
        for row in self.read_players():
            if row[2].strip().casefold() == nick_key and row[1].casefold() != sid_key:
                return False, "nickname already belongs to account %s of another Somnium ID" % row[0]
        return True, "by nickname"

    def is_gm_id(self, somnium_id):
        return somnium_id.casefold() in (read_list(self.gm_path) or [])

    # --- POST /register
    def register(self, body, content_type, ip):
        """-> (HTTP status, JSON object). Never raises for bad input."""
        if not self.limiter.allow_ip(ip):
            log("register ip=%s -> slow_down (rate limit)" % ip)
            return 429, {"status": "slow_down"}
        try:
            req = parse_register(body, content_type)
        except BadRequest as e:
            log("register ip=%s -> bad_request (%s)" % (ip, e))
            return 400, {"status": "bad_request", "message": str(e)}
        sid, nick, name = req["somniumId"], req["nick"], req["name"]
        who = "ip=%s id=%s nick=%s name=%s" % (ip, sid, safe(nick), name)
        allowed, how = self.allow_check(sid, nick)
        if not allowed:
            log("register %s -> not_allowed (%s)" % (who, how))
            return 403, {"status": "not_allowed"}
        if not self.limiter.allow_global():             # counted only here: after the checks and the allow list
            log("register %s -> slow_down (rate limit for all players)" % who)
            return 429, {"status": "slow_down"}
        gm = self.is_gm_id(sid)
        if not self.db_lock.acquire(timeout=DB_LOCK_WAIT):
            log("register %s -> slow_down (busy)" % who)
            return 429, {"status": "slow_down"}
        try:
            if how == "by nickname":
                # Checked again under the lock: two players sending the same nickname at the same moment
                # cannot both take it (players.tsv is written under this lock).
                allowed, how = self.allow_check(sid, nick)
                if not allowed:
                    log("register %s -> not_allowed (%s)" % (who, how))
                    return 403, {"status": "not_allowed"}
            return self._register_locked(req, gm, who + " allowed " + how)
        finally:
            self.db_lock.release()

    def _register_locked(self, req, gm, who):
        name = req["name"]
        claimed = None
        if self.unlock_valid(name):
            if self.dry_run:
                claimed = "dry-run"
            else:
                # Claim the marker first (atomic rename): a crash can only lose it, never reuse it.
                claimed = self.unlock_path(name) + ".claimed"
                try:
                    os.replace(self.unlock_path(name), claimed)
                except OSError as e:
                    log("register %s: could not claim the unlock (%s) - treating as locked" % (who, type(e).__name__))
                    claimed = None
        try:
            result, acct_id = self.store.register(name, req["salt"], req["verifier"], gm, claimed is not None)
        except (DbError, ValueError) as e:
            self._release_claim(name, claimed, consume=False)
            log("register %s -> error (database: %s)" % (who, e))
            return 500, {"status": "error"}
        self._release_claim(name, claimed, consume=result in ("created", "rekeyed"))
        if result == "created":
            self.record_player(name, req["somniumId"], req["nick"], "created")
            log("register %s -> created account id %d%s%s" % (who, acct_id, " GM" if gm else "",
                                                              " (dry run)" if self.dry_run else ""))
            return 201, {"status": "created", "name": name, "gm": bool(gm)}
        if result == "rekeyed":
            self.record_player(name, req["somniumId"], req["nick"], "rekeyed")
            log("register %s -> rekeyed account id %d (unlock used up)%s" % (who, acct_id, " (dry run)" if self.dry_run else ""))
            return 200, {"status": "rekeyed", "name": name}
        log("register %s -> exists (nothing changed)" % who)
        return 409, {"status": "exists", "name": name}

    def _release_claim(self, name, claimed, consume):
        if claimed is None:
            return
        if claimed == "dry-run":
            if consume:
                log("dry run: the unlock for %s would now be used up (kept)" % name)
            return
        try:
            if consume:
                os.unlink(claimed)
            else:
                os.replace(claimed, self.unlock_path(name))
        except OSError as e:
            log("unlock marker for %s: %s failed (%s)" % (name, "removing" if consume else "restoring", type(e).__name__))


# ---------------------------------------------------------------------------------------------
# HTTP (PLAN8 2.4): GET /health, POST /register, everything else 404; Connection: close
# ---------------------------------------------------------------------------------------------
class Handler(BaseHTTPRequestHandler):
    server_version = "wow-accounts/%d" % VERSION
    sys_version = ""
    protocol_version = "HTTP/1.1"
    timeout = READ_DEADLINE                 # per socket read; the whole request is bounded by the timer below

    def setup(self):
        super().setup()
        self._cut_lock = threading.Lock()
        self._reading = True
        self._cut = False
        self._timer = threading.Timer(self.server.read_deadline, self._deadline)
        self._timer.daemon = True
        self._timer.start()

    def _deadline(self):
        with self._cut_lock:
            if not self._reading:
                return
            self._cut = True
        try:
            self.connection.shutdown(socket.SHUT_RDWR)
        except OSError:
            pass

    def _done_reading(self):
        """Stops the request deadline. False if the deadline already cut the connection."""
        self._timer.cancel()
        with self._cut_lock:
            self._reading = False
            return not self._cut

    def finish(self):
        self._timer.cancel()
        try:
            super().finish()
        except OSError:
            pass

    def version_string(self):
        return self.server_version

    def log_request(self, code="-", size="-"):
        pass                                # our own summary lines only (never request bodies)

    def log_error(self, format, *args):
        try:
            text = format % args
        except (TypeError, ValueError):
            text = format
        log("http %s: %s" % (self.client_address[0], safe(text, 160)))

    def send_error(self, code, message=None, explain=None):
        code = int(code)
        if code in (405, 501):
            code = 404                      # unknown methods: "Anything else -> 404"
        self.log_error("%d %s", code, message or "")
        status = "not_found" if code == 404 else ("bad_request" if 400 <= code < 500 else "error")
        self._send(code, {"status": status})

    def _send(self, code, obj):
        body = json.dumps(obj, separators=(",", ":")).encode("utf-8")
        self.close_connection = True
        try:
            self.send_response(code)
            self.send_header("Content-Type", "application/json")
            self.send_header("Content-Length", str(len(body)))
            self.send_header("Cache-Control", "no-store")
            self.send_header("Connection", "close")
            self.end_headers()
            if self.command != "HEAD":
                self.wfile.write(body)
        except OSError:
            pass

    def do_GET(self):
        self._done_reading()
        if urlsplit(self.path).path == "/health":
            self._send(200, {"status": "ok", "service": SERVICE_NAME, "version": VERSION})
        else:
            self._send(404, {"status": "not_found"})

    def do_POST(self):
        if urlsplit(self.path).path != "/register":
            self._done_reading()
            self._send(404, {"status": "not_found"})
            return
        ip = self.client_address[0]
        if self.headers.get("Transfer-Encoding") is not None:
            self._done_reading()
            log("register ip=%s -> bad_request (chunked)" % ip)
            self._send(400, {"status": "bad_request", "message": "Send the request with a Content-Length."})
            return
        length = (self.headers.get("Content-Length") or "").strip()
        if not _CONTENT_LENGTH_RE.fullmatch(length):
            self._done_reading()
            log("register ip=%s -> bad_request (no Content-Length)" % ip)
            self._send(400, {"status": "bad_request", "message": "Send the request with a Content-Length."})
            return
        n = int(length)
        if n > MAX_BODY:
            self._done_reading()
            log("register ip=%s -> bad_request (body of %d bytes)" % (ip, n))
            self._send(400, {"status": "bad_request", "message": "The request is too big."})
            return
        try:
            body = self.rfile.read(n) if n else b""
        except OSError:
            return
        if not self._done_reading() or len(body) != n:
            return                          # the client went away or the 10 s deadline closed it: nothing is done
        code, obj = self.server.accounts.register(body, self.headers.get("Content-Type"), ip)
        self._send(code, obj)


class AccountServer(ThreadingHTTPServer):
    daemon_threads = True
    block_on_close = False
    request_queue_size = 16

    def __init__(self, address, accounts, max_workers=MAX_WORKERS, read_deadline=READ_DEADLINE, max_per_ip=MAX_PER_IP):
        self.accounts = accounts
        self.read_deadline = read_deadline
        self._slots = threading.BoundedSemaphore(max_workers)
        self.max_per_ip = max_per_ip
        self._per_ip = {}                    # source IP -> connections being handled now
        self._per_ip_lock = threading.Lock()
        super().__init__(address, Handler)

    def server_bind(self):
        # HTTPServer.server_bind would look up our own host name (socket.getfqdn) - not needed.
        socketserver.TCPServer.server_bind(self)
        host, port = self.server_address[:2]
        self.server_name = str(host)
        self.server_port = port

    def process_request(self, request, client_address):
        ip = client_address[0] if client_address else "?"
        with self._per_ip_lock:
            taken = self._per_ip.get(ip, 0)
            ok = taken < self.max_per_ip and self._slots.acquire(blocking=False)
            if ok:
                self._per_ip[ip] = taken + 1
        if not ok:
            self.shutdown_request(request)   # too many connections at once (from this address or in total): close it
            return
        try:
            super().process_request(request, client_address)
        except BaseException:
            self._release(ip)
            raise

    def process_request_thread(self, request, client_address):
        try:
            super().process_request_thread(request, client_address)
        finally:
            self._release(client_address[0] if client_address else "?")

    def _release(self, ip):
        with self._per_ip_lock:
            left = self._per_ip.get(ip, 0) - 1
            if left > 0:
                self._per_ip[ip] = left
            else:
                self._per_ip.pop(ip, None)
        self._slots.release()

    def handle_error(self, request, client_address):
        exc = sys.exc_info()[1]
        log("connection from %s ended: %s" % (client_address[0] if client_address else "?", type(exc).__name__))


def serve(args):
    store = MemoryStore(echo=True) if args.dry_run else MysqlStore()
    accounts = Accounts(store, args.etc_dir, args.state_dir, dry_run=args.dry_run)
    allow = read_list(accounts.allow_path)
    gms = read_list(accounts.gm_path)
    try:
        httpd = AccountServer((args.bind, args.port), accounts)
    except OSError as e:
        log("cannot listen on %s:%d (%s)" % (args.bind, args.port, e))
        return 1
    log("%s v%d listening on %s:%d%s; allow list %s: %s; GM list: %s" % (
        SERVICE_NAME, VERSION, args.bind, args.port,
        " - DRY RUN: no database writes, the database is pretended empty, no files are changed" if args.dry_run else "",
        accounts.allow_path, "MISSING - nobody can register" if allow is None else "%d entries" % len(allow),
        "none" if not gms else "%d entries" % len(gms)))
    if not args.dry_run:
        try:
            count = store.count_accounts()
            problem = schema_problem(store.schema())
            if problem:
                log("WARNING: %s does not look like AzerothCore's login database: %s" % (DATABASE, problem))
            else:
                log("database ok: %d accounts in %s" % (count, DATABASE))
        except DbError as e:
            log("WARNING: database not reachable yet (%s) - registrations answer 'error' until it is" % e)
    try:
        httpd.serve_forever()
    except KeyboardInterrupt:
        pass
    finally:
        httpd.server_close()
    return 0


# ---------------------------------------------------------------------------------------------
# Admin commands (used by /usr/local/bin/wow-account)
# ---------------------------------------------------------------------------------------------
class AdminError(Exception):
    pass


def resolve(accounts, who, auto_only=False):
    """WHO (account name, Somnium ID or a known Somnium nickname) -> account name.
    auto_only (unlock): only automatic accounts (S + 16 hex, made by 'Play online'); a nickname then always
    means that player's automatic account, never a normal account that happens to have the same name.
    Otherwise a nickname that is also the name of a normal account is refused (both are listed)."""
    who = (who or "").strip()
    if not who:
        raise AdminError("Nothing was typed.")
    name = name_for(who)
    if name:
        return name
    upper = upper_latin(who)
    if _AUTO_NAME_RE.fullmatch(upper):
        return upper                                     # an automatic account's name (normal accounts cannot look like this)
    key = who.casefold()
    players = [row for row in accounts.read_players() if row[2].strip().casefold() == key]
    names = sorted({row[0] for row in players})
    account = None                                       # a normal account with exactly this name
    if _NAME_RE.fullmatch(upper):
        if accounts.dry_run:
            account = None if names else upper           # dry run: the database is not asked
        elif accounts.store.account_id(upper) > 0:
            account = upper
    if len(names) > 1:
        raise AdminError("More than one account belongs to a player called %s: %s - use the account name." % (who, ", ".join(names)))
    if len(names) == 1:
        if account is not None and not auto_only:
            raise AdminError("%s could mean two accounts: the normal account %s, or %s, the automatic account of the Somnium "
                             "player %s. Nothing was changed. For the automatic account type %s (or the player's Somnium ID). "
                             "For the normal account %s use the worldserver console (README section 3)."
                             % (who, account, names[0], players[0][2].strip(), names[0], account))
        return names[0]
    if account is not None:
        if auto_only:
            raise AdminError("%s is a normal account (made with 'create' or in the worldserver console), not an automatic one. "
                             "Unlock only works for the accounts 'Play online' makes (names like %s): type the player's "
                             "Somnium nickname or Somnium ID. Nothing was changed." % (account, VECTOR_NAME))
        return account
    if _NAME_RE.fullmatch(upper):
        raise AdminError("There is no account called %s (and no Somnium player with that name has registered here)." % upper)
    raise AdminError("No account and no registered Somnium player is called %s." % who)


def read_password_stdin():
    raw = sys.stdin.buffer.readline(256)
    try:
        text = raw.decode("utf-8")
    except UnicodeDecodeError:
        text = ""
    if text.endswith("\n"):
        text = text[:-1]
    if text.endswith("\r"):
        text = text[:-1]
    return text


def cmd_create(accounts, args):
    name = upper_latin(args.name.strip())
    if not _NAME_RE.fullmatch(name):
        print("Account names use only letters A-Z and digits 0-9, up to %d characters." % MAX_ACCOUNT_STR)
        return 2
    if _AUTO_NAME_RE.fullmatch(name):
        print("Names that look like %s (S + 16 characters 0-9/A-F) are kept for automatic accounts - choose another name." % VECTOR_NAME)
        return 2
    password = read_password_stdin()
    if not _PASSWORD_RE.fullmatch(password):
        print("The password must be 1-%d characters: letters, digits or symbols, no spaces. Nothing was created." % MAX_PASS_STR)
        return 2
    # AccountMgr::CreateAccount: Utf8ToUpperOnlyLatin(username/password), SRP6::MakeRegistrationData.
    salt, verifier = make_registration(name, upper_latin(password))
    password = None
    result, acct_id = accounts.store.register(name, salt.hex().upper(), verifier.hex().upper(), False, False)
    if result == "created":
        print("Created the account %s%s. Your friend logs in with this name and the password you typed "
              "(names and passwords are not case-sensitive)." % (name, " (dry run - nothing written)" if accounts.dry_run else ""))
        return 0
    print("An account called %s already exists - nothing was changed." % name)
    return 1


def cmd_testaccounts(accounts, args):
    """Several normal accounts in one go (the user, 2026-09-27: "make simple test accounts ... make 10 for now"):
    PREFIX01, PREFIX02, ... with ONE password (read from stdin, like create), each with its own random salt. Existing names are
    skipped, never changed. GM only with --gm."""
    prefix = upper_latin(args.prefix.strip())
    count = args.count
    if not 1 <= count <= 50:
        print("Make 1-50 test accounts at a time.")
        return 2
    digits = 2 if count < 100 else 3
    if not prefix or not _NAME_RE.fullmatch(prefix) or len(prefix) + digits > MAX_ACCOUNT_STR:
        print("The name start uses only letters A-Z and digits 0-9, up to %d characters." % (MAX_ACCOUNT_STR - digits))
        return 2
    names = ["%s%0*d" % (prefix, digits, i) for i in range(1, count + 1)]
    if any(_AUTO_NAME_RE.fullmatch(n) for n in names):
        print("Those names look like automatic accounts (S + 16 characters 0-9/A-F) - choose another name start.")
        return 2
    password = read_password_stdin()
    if not _PASSWORD_RE.fullmatch(password):
        print("The password must be 1-%d characters: letters, digits or symbols, no spaces. Nothing was created." % MAX_PASS_STR)
        return 2
    upper_pass = upper_latin(password)
    password = None
    made, skipped = [], []
    for name in names:
        # AccountMgr::CreateAccount: Utf8ToUpperOnlyLatin(username/password), SRP6::MakeRegistrationData (a new salt each).
        salt, verifier = make_registration(name, upper_pass)
        result, acct_id = accounts.store.register(name, salt.hex().upper(), verifier.hex().upper(), bool(args.gm), False)
        (made if result == "created" else skipped).append(name)
    upper_pass = None
    dry = " (dry run - nothing written)" if accounts.dry_run else ""
    if made:
        print("Created %d test account%s%s: %s" % (len(made), "" if len(made) == 1 else "s", dry, ", ".join(made)))
        print("They all use the password you typed%s. Names and passwords are not case-sensitive."
              % (" and are Game Masters" if args.gm else ""))
    if skipped:
        print("Already there, not changed: %s" % ", ".join(skipped))
    return 0 if made else 1


def cmd_unlock(accounts, args):
    name = resolve(accounts, args.who, auto_only=True)
    if not _AUTO_NAME_RE.fullmatch(name):                # the service re-keys only automatic accounts (S + 16 hex)
        print("%s is not an automatic account (names like %s) - unlock is only for those. Nothing was changed." % (name, VECTOR_NAME))
        return 1
    acct_id = accounts.store.account_id(name)
    if acct_id == 0 and not accounts.dry_run:
        print("There is no account %s yet - nothing to unlock. That player can simply press 'Play online'." % name)
        return 1
    if accounts.dry_run:
        print("Dry run: would write %s (valid 24 hours)." % accounts.unlock_path(name))
        return 0
    accounts.write_unlock(name)
    print("Unlocked %s for 24 hours. The next time that player presses 'Play online' in the Dreamport, it makes a "
          "new key on their computer. Their characters stay. After that the account is locked again." % name)
    return 0


def normalize_allow_entry(entry):
    entry = (entry or "").strip()
    if name_for(entry):
        return entry.lower()
    if not entry or len(entry) > MAX_NICK or "#" in entry or bad_text(entry):
        return None
    return entry


def cmd_allow(accounts, args):
    entry = normalize_allow_entry(args.who)
    if entry is None:
        print("Type a Somnium nickname (up to %d characters, no #) or a Somnium ID like %s." % (MAX_NICK, VECTOR_SOMNIUM_ID))
        return 2
    path = accounts.allow_path
    try:
        with open(path, "r", encoding="utf-8", errors="replace") as f:
            lines = f.read().splitlines()
    except FileNotFoundError:
        lines = []
    key = entry.casefold()
    present = key in (read_list(path) or [])
    if args.remove:
        if not present:
            print("%s is not on the allow list - nothing changed." % entry)
            return 1
        kept = [ln for ln in lines
                if ln.strip().startswith("#") or re.split(r"\s+#", ln.strip(), maxsplit=1)[0].strip().casefold() != key]
        if accounts.dry_run:
            print("Dry run: would remove %s from %s." % (entry, path))
            return 0
        write_text(path, "\n".join(kept) + "\n")
        print("Removed %s from the allow list. (An account they already have stays; they just cannot get a new one.)" % entry)
        return 0
    if present:
        print("%s is already on the allow list." % entry)
        return 0
    if accounts.dry_run:
        print("Dry run: would add %s to %s." % (entry, path))
        return 0
    write_text(path, "\n".join(lines + [entry]) + "\n")
    print("Added %s. They can now press 'Play online' in the WoW Dreamport to get their account." % entry)
    if not name_for(entry):
        print("(A nickname lets in one Somnium account: the first one that gets an account with it.)")
    return 0


def write_text(path, text):
    """Replaces a list file atomically, keeping its permissions (new files: 0600)."""
    os.makedirs(os.path.dirname(path), mode=0o700, exist_ok=True)
    try:
        mode = stat.S_IMODE(os.stat(path).st_mode)
    except FileNotFoundError:
        mode = 0o600
    tmp = path + ".tmp"
    with open(tmp, "w", encoding="utf-8") as f:
        f.write(text)
    os.chmod(tmp, mode)
    os.replace(tmp, path)


def cmd_gm(accounts, args):
    name = resolve(accounts, args.who)
    on = args.state == "on"
    acct_id = accounts.store.set_gm(name, on)
    if accounts.dry_run:
        print("Dry run: nothing was changed.")
        return 0
    if acct_id == 0:
        print("There is no account %s - nothing changed." % name)
        return 1
    if on:
        print("%s is now a Game Master (level %d, all realms). It takes effect at their next login." % (name, GM_LEVEL))
    else:
        print("%s is now a normal player. It takes effect at their next login." % name)
    return 0


def cmd_list(accounts, args):
    players = {row[0]: row for row in accounts.read_players()}
    db_error = None
    try:
        rows = accounts.store.list_accounts()
    except DbError as e:                 # still show the lists below (they do not need the database)
        db_error = e
        rows = []
        print("Accounts: the database did not answer (%s). Is the realm running? Check with: sudo bash ~/status.sh" % e)
    if db_error is None:
        print("Accounts (%d):" % len(rows))
        print("  %-6s %-18s %-4s %-17s %-7s %s" % ("id", "name", "GM", "last login (UTC)", "online", "Somnium player"))
    for acct_id, name, gm, last, online in rows:
        p = players.get(name)
        who = ("%s (%s)" % (p[2] or "?", p[1])) if p else ""
        print("  %-6s %-18s %-4s %-17s %-7s %s" % (acct_id, name, "yes" if gm not in ("0", "") else "", last,
                                                   "yes" if online not in ("0", "") else "", who))
    for title, path in (("Allow list", accounts.allow_path), ("GM list (Somnium IDs, GM when the account is created)", accounts.gm_path)):
        entries = read_list(path)
        print("%s - %s:" % (title, path))
        if entries is None:
            print("  (file missing)")
        elif not entries:
            print("  (empty)")
        for entry in entries or []:
            print("  " + entry)
    unlocked = accounts.unlocked_names()
    print("Unlocked right now (next 'Play online' makes a new key): %s" % (", ".join(unlocked) if unlocked else "none"))
    return 1 if db_error is not None else 0


def cmd_dbcheck(accounts, args):
    """Read-only: can we reach the database, and are the tables the ones this service writes?"""
    count = accounts.store.count_accounts()
    counts = accounts.store.schema()
    problem = schema_problem(counts)
    if problem:
        print("DATABASE PROBLEM: %s in %s (container %s) - automatic accounts would fail." % (problem, DATABASE, CONTAINER))
        return 1
    print("database ok: %d accounts in %s (container %s); the tables have every column this service uses; %d realm(s)"
          % (count, DATABASE, CONTAINER, counts[-1]))
    return 0


# ---------------------------------------------------------------------------------------------
# --selftest: everything that can be checked without a database (safe to run anywhere)
# ---------------------------------------------------------------------------------------------
def _selftest():
    failures = []

    def check(ok, what):
        print(("  ok    " if ok else "  FAIL  ") + what)
        if not ok:
            failures.append(what)

    print("%s selftest (python %s)" % (SERVICE_NAME, sys.version.split()[0]))
    name = name_for(VECTOR_SOMNIUM_ID)
    verifier = calculate_verifier(VECTOR_NAME, VECTOR_SECRET, VECTOR_SALT).hex().upper()
    print("PLAN8 2.3 test vector:")
    print("  somniumId %s" % VECTOR_SOMNIUM_ID)
    print("  name      %s" % name)
    print("  secret    %s" % VECTOR_SECRET)
    print("  salt      %s" % VECTOR_SALT.hex().upper())
    print("  verifier  %s" % verifier)
    check(name == VECTOR_NAME, "name_for(%s) = %s" % (VECTOR_SOMNIUM_ID, VECTOR_NAME))
    check(verifier == EXPECTED_VECTOR_VERIFIER, "verifier = the independently computed value %s" % EXPECTED_VECTOR_VERIFIER)
    check(srp_login_matches(VECTOR_NAME, VECTOR_SECRET, VECTOR_SALT, bytes.fromhex(verifier)),
          "a full SRP6 login (client math vs AzerothCore server math) succeeds with this verifier")
    check(not srp_login_matches(VECTOR_NAME, "ABCDEFGHJKLMNPQS", VECTOR_SALT, bytes.fromhex(verifier)),
          "the same login with a wrong password fails")
    s2, v2 = make_registration("SOMEONE", "PASSWORD1")
    check(len(s2) == 32 and len(v2) == 32 and s2 != VECTOR_SALT and srp_login_matches("SOMEONE", "PASSWORD1", s2, v2),
          "make_registration: random 32-byte salt, verifier logs in")
    check(upper_latin("abcXYZ09-_ä") == "ABCXYZ09-_ä", "upper_latin changes only a-z")

    print("Account names (PLAN8 2.1):")
    check(name_for("CF5C553C-61F8-43F4-A290-4985CA328EB4") == VECTOR_NAME, "upper-case GUID gives the same name")
    for bad in ["", "cf5c553c61f843f4a2904985ca328eb4", "{cf5c553c-61f8-43f4-a290-4985ca328eb4}",
                " cf5c553c-61f8-43f4-a290-4985ca328eb4", "cf5c553c-61f8-43f4-a290-4985ca328eb4\n",
                "gf5c553c-61f8-43f4-a290-4985ca328eb4", "cf5c553c-61f8-43f4-a290-4985ca328eb", "Shodah",
                "cf5c553c-61f8-43f4-a290-4985ca328eb4x", None, 12345]:
        check(name_for(bad) is None, "not a GUID -> no name: %s" % safe(bad, 50))

    print("Request checks (PLAN8 2.4):")
    good = {"v": 1, "somniumId": VECTOR_SOMNIUM_ID, "nick": "Shodah", "salt": VECTOR_SALT.hex().upper(), "verifier": verifier}

    def parse(obj=None, raw=None, ctype="application/json"):
        body = raw if raw is not None else json.dumps(obj).encode("utf-8")
        try:
            return parse_register(body, ctype)
        except BadRequest as e:
            return "bad: " + str(e)

    r = parse(good)
    check(isinstance(r, dict) and r["name"] == VECTOR_NAME and r["verifier"] == verifier, "a correct request is accepted")
    check(isinstance(parse(good, ctype="application/json; charset=utf-8"), dict), "Content-Type with charset is accepted")
    lower = dict(good, salt=good["salt"].lower())
    check(isinstance(parse(lower), dict) and parse(lower)["salt"] == good["salt"], "lower-case hex is accepted and upper-cased")
    cases = [
        ("wrong Content-Type", dict(obj=good, ctype="text/plain")),
        ("not JSON", dict(raw=b"{nope")),
        ("JSON array", dict(raw=b"[1]")),
        ("not UTF-8", dict(raw=b'{"v":1,"nick":"\xff"}')),
        ("NaN", dict(raw=b'{"v":NaN}')),
        ("duplicate key", dict(raw=b'{"v":1,"v":1}')),
        ("v missing", dict(obj=dict(good, v=None))),
        ("v = 2", dict(obj=dict(good, v=2))),
        ("v = true", dict(obj=dict(good, v=True))),
        ("v = 1.0", dict(obj=dict(good, v=1.0))),
        ("somniumId not a GUID", dict(obj=dict(good, somniumId="Shodah"))),
        ("somniumId a number", dict(obj=dict(good, somniumId=5))),
        ("nick too long", dict(obj=dict(good, nick="x" * 65))),
        ("nick a number", dict(obj=dict(good, nick=7))),
        ("salt 63 hex", dict(obj=dict(good, salt=good["salt"][:63]))),
        ("salt with a quote", dict(obj=dict(good, salt="'" + good["salt"][1:]))),
        ("verifier missing", dict(obj=dict(good, verifier=None))),
        ("verifier = 0", dict(obj=dict(good, verifier="0" * 64))),
        ("verifier >= N", dict(obj=dict(good, verifier="F" * 64))),
        ("body > 2048 bytes", dict(obj=dict(good, pad="x" * 2100))),
        ("deep nesting", dict(raw=b"[" * 1000 + b"]" * 1000)),
    ]
    for what, kw in cases:
        res = parse(**kw)
        check(isinstance(res, str) and res.startswith("bad: "), "refused: %s (%s)" % (what, res if isinstance(res, str) else "ACCEPTED"))
    r = parse(dict(good, nick=" Sho\tdah\u2028x\u200b "))
    check(isinstance(r, dict) and r["nick"] == "Sho dah x", "nick: control/format/line-separator characters become spaces, trimmed")
    r = parse(raw=b'{"v":1,"somniumId":"' + VECTOR_SOMNIUM_ID.encode() + b'","nick":"a\\ud800b","salt":"' + good["salt"].encode()
              + b'","verifier":"' + verifier.encode() + b'"}')
    check(isinstance(r, dict) and r["nick"] == "a\ufffdb", "nick: a lone surrogate becomes U+FFFD (safe to log and store)")
    r = parse(dict(good, nick="\u0160\u00f6\u2713\U0001F600" * 16))
    check(isinstance(r, dict) and len(r["nick"]) == 64, "nick: 64 characters incl. emoji accepted as they are")

    print("SQL (values checked right before they go into the text):")
    for what, fn in [("lower-case name", lambda: q_name("scf5")), ("name with a quote", lambda: q_name("A'B")),
                     ("18-character name", lambda: q_name("A" * 18)), ("hex with a quote", lambda: q_hex("'" + "A" * 63)),
                     ("lower-case hex", lambda: q_hex("a" * 64)), ("name not a string", lambda: q_name(None))]:
        try:
            fn()
            check(False, "refused: " + what)
        except ValueError:
            check(True, "refused: " + what)
    sql = sql_register(VECTOR_NAME, VECTOR_SALT.hex().upper(), verifier, True, False)
    check("INSERT INTO account(username, salt, verifier, expansion, reg_mail, email, joindate) SELECT 'SCF5C553C61F843F4', UNHEX('" in sql
          and "'), 2, '', '', NOW() FROM DUAL WHERE @wa_exists = 0;" in sql, "LOGIN_INS_ACCOUNT values as AccountMgr::CreateAccount")
    check("INSERT INTO realmcharacters (realmid, acctid, numchars) SELECT realmlist.id, account.id, 0 FROM realmlist, account "
          "LEFT JOIN realmcharacters ON acctid=account.id WHERE acctid IS NULL" in sql, "LOGIN_INS_REALM_CHARACTERS_INIT text")
    check("INSERT INTO account_access (id,gmlevel,RealmID) SELECT @wa_id, 3, -1" in sql, "GM row (level 3, realm -1)")
    check("UPDATE account" not in sql and sql.index("START TRANSACTION;") < sql.index("SET @wa_exists") < sql.index("COMMIT;"),
          "one transaction, exists-check inside it, no UPDATE without an unlock")
    sql_rk = sql_register(VECTOR_NAME, VECTOR_SALT.hex().upper(), verifier, False, True)
    check("UPDATE account SET salt = UNHEX('" in sql_rk and "WHERE id = @wa_id AND @wa_created = 0;" in sql_rk
          and "account_access" not in sql_rk, "unlocked: LOGIN_UPD_LOGON only for the existing account; no GM change")
    shown = redact(sql, ("salt", VECTOR_SALT.hex().upper()), ("verifier", verifier))
    check(verifier not in shown and VECTOR_SALT.hex().upper() not in shown, "dry-run output hides salt and verifier")
    print("  SQL for the test vector with GM (public test values; nothing is run):")
    for line in sql.splitlines():
        print("    " + line)

    print("Database answers:")

    def fake(rc, out, err=""):
        return MysqlStore(runner=lambda s: (rc, out, err))

    args5 = (VECTOR_NAME, VECTOR_SALT.hex().upper(), verifier, False, False)
    check(fake(0, "1\t0\t7\n").register(*args5) == ("created", 7), "'1 0 7' -> created")
    check(fake(0, "0\t1\t7\n").register(*args5) == ("rekeyed", 7), "'0 1 7' -> rekeyed")
    check(fake(0, "0\t0\t7\n").register(*args5) == ("exists", 7), "'0 0 7' -> exists")
    check(fake(1, "", "ERROR 1062 (23000) at line 3: Duplicate entry 'X' for key 'account.idx_username'").register(*args5)[0] == "exists",
          "duplicate key on idx_username -> exists")
    for what, st in [("container stopped", fake(1, "", "Error response from daemon: container is not running")),
                     ("garbage output", fake(0, "hello\n")), ("empty output", fake(0, "")), ("NULL", fake(0, "1\t0\tNULL\n"))]:
        try:
            st.register(*args5)
            check(False, "%s -> error" % what)
        except DbError:
            check(True, "%s -> error" % what)
    check(fake(0, "10\t3\t3\t1\n").schema() == [10, 3, 3, 1] and schema_problem([10, 3, 3, 1]) is None,
          "schema check: all columns present, one realm -> ok")
    check(schema_problem([10, 2, 3, 1]) is not None and schema_problem([10, 3, 3, 0]) is not None,
          "schema check: a missing column or no realm -> problem")
    check("table_name = 'account_access' AND column_name IN ('id', 'gmlevel', 'RealmID')" in SQL_SCHEMA
          and SQL_SCHEMA.startswith("SELECT (SELECT COUNT(*) FROM information_schema.columns"), "schema check SQL is read-only")
    check(clean_stderr("mysql: [Warning] Using a password on the command line interface can be insecure.\nERROR 1064 near 'UNHEX('"
                       + verifier + "')'") == "ERROR 1064 near 'UNHEX('<hex>')'", "logged database errors hide hex values")

    print("Rate limits (5 per IP per 10 min, 60 per hour):")
    rl = RateLimiter()
    check(all(rl.allow_ip("1.1.1.1", now=100.0 + i) for i in range(5)) and not rl.allow_ip("1.1.1.1", now=106.0), "6th call from one IP is refused")
    check(rl.allow_ip("2.2.2.2", now=107.0), "another IP is still served")
    check(rl.allow_ip("1.1.1.1", now=700.5), "after 10 minutes the IP is served again")
    rl2 = RateLimiter()
    check(all(rl2.allow_global(now=float(i)) for i in range(60)) and not rl2.allow_global(now=61.0),
          "61st call in an hour is refused")
    check(rl2.allow_global(now=3600.5), "an hour later calls are served again")

    tmp = tempfile.mkdtemp(prefix="wow-accounts-selftest-")
    etc, state = os.path.join(tmp, "etc"), os.path.join(tmp, "state")
    os.makedirs(etc)
    os.makedirs(os.path.join(state, "unlocked"))
    with open(os.path.join(etc, "allow.txt"), "w", encoding="utf-8") as f:
        f.write("# test\nShodah\n  wildy   # comment\n%s\n" % VECTOR_SOMNIUM_ID.upper())
    with open(os.path.join(etc, "gm.txt"), "w", encoding="utf-8") as f:
        f.write("# test\n%s\n" % VECTOR_SOMNIUM_ID)

    print("Allow / GM lists:")
    acc = Accounts(MemoryStore(), etc, state, limiter=RateLimiter((9, 600), (60, 3600)))
    check(read_list(os.path.join(etc, "allow.txt")) == ["shodah", "wildy", VECTOR_SOMNIUM_ID], "comments skipped, case-insensitive")
    check(acc.is_allowed(VECTOR_SOMNIUM_ID, "") and acc.is_allowed("00000000-0000-0000-0000-000000000001", "WILDY")
          and not acc.is_allowed("00000000-0000-0000-0000-000000000001", "Nobody")
          and not acc.is_allowed("00000000-0000-0000-0000-000000000001", ""), "allowed by ID or nickname, nobody else")
    check(acc.is_gm_id(VECTOR_SOMNIUM_ID) and not acc.is_gm_id("00000000-0000-0000-0000-000000000001"), "GM only by Somnium ID in gm.txt")
    check(read_list(os.path.join(etc, "missing.txt")) is None, "a missing list means nobody")

    print("The hourly limit for all players counts only correct requests of allowed players:")
    accg = Accounts(MemoryStore(), etc, state, dry_run=True, limiter=RateLimiter((100, 600), (1, 3600)))
    junk = [accg.register(b"{nope", "application/json", "203.0.113.%d" % i)[0] for i in range(5)]
    strangers = [accg.register(json.dumps(dict(good, somniumId="00000000-0000-0000-0000-00000000000%d" % i, nick="Stranger"))
                               .encode("utf-8"), "application/json", "203.0.113.9")[0] for i in range(5)]
    check(junk == [400] * 5 and strangers == [403] * 5, "5 broken requests -> 400, 5 strangers -> 403")
    check(accg.register(json.dumps(good).encode("utf-8"), "application/json", "198.51.100.7")
          == (201, {"status": "created", "name": VECTOR_NAME, "gm": True}),
          "then an allowed player is still served (the broken and refused requests did not use up the limit, here 1)")
    check(accg.register(json.dumps(dict(good, somniumId="00000000-0000-0000-0000-000000000001", nick="Wildy")).encode("utf-8"),
                        "application/json", "198.51.100.8") == (429, {"status": "slow_down"}),
          "the next allowed player is over the limit (1) -> 429 slow_down")

    print("Admin: which account a typed name means (unlock / gm):")
    accr = Accounts(MemoryStore(), etc, os.path.join(tmp, "state_resolve"))
    accr.store.register("SHODAH", "A" * 64, "B" * 64, False, False)          # the owner's normal account (README section 3)
    accr.store.register("FRIEND", "A" * 64, "B" * 64, False, False)          # a friend's normal account
    accr.store.register(VECTOR_NAME, "A" * 64, "B" * 64, True, False)        # Shodah's automatic account
    accr.record_player(VECTOR_NAME, VECTOR_SOMNIUM_ID, "Shodah", "created")

    def resolved(who, auto_only=False):
        try:
            return resolve(accr, who, auto_only)
        except AdminError as e:
            return "refused: " + str(e)

    check(resolved("Shodah", True) == VECTOR_NAME and resolved("SHODAH", True) == VECTOR_NAME,
          "unlock Shodah -> the automatic account %s, not the normal account SHODAH" % VECTOR_NAME)
    check(resolved(VECTOR_SOMNIUM_ID, True) == VECTOR_NAME and resolved(VECTOR_NAME.lower(), True) == VECTOR_NAME,
          "unlock by Somnium ID or by the automatic name -> %s" % VECTOR_NAME)
    r = resolved("friend", True)
    check(r.startswith("refused: FRIEND is a normal account"), "unlock of a normal account is refused (%s)" % r[:60])
    r = resolved("Shodah")
    check(r.startswith("refused: Shodah could mean two accounts") and "SHODAH" in r and VECTOR_NAME in r,
          "gm Shodah: a nickname that is also a normal account is refused, both are named")
    check(resolved(VECTOR_NAME) == VECTOR_NAME and resolved("Friend") == "FRIEND", "gm by automatic name / normal name")
    check(resolved("Nobody").startswith("refused: There is no account called NOBODY"), "an unknown name is refused")
    with contextlib.redirect_stdout(io.StringIO()) as out:
        rc = cmd_unlock(accr, argparse.Namespace(who="Shodah"))
    check(rc == 0 and accr.unlock_valid(VECTOR_NAME) and not os.path.exists(os.path.join(tmp, "state_resolve", "unlocked", "SHODAH"))
          and out.getvalue().startswith("Unlocked %s for 24 hours" % VECTOR_NAME), "cmd_unlock Shodah writes unlocked/%s" % VECTOR_NAME)

    print("Admin: test accounts (pretend database):")
    acct = Accounts(MemoryStore(), etc, os.path.join(tmp, "state_test"), dry_run=True)
    acct.store.register("TEST03", "A" * 64, "B" * 64, False, False)          # one already there: skipped, not changed
    old_stdin = sys.stdin
    try:
        sys.stdin = io.TextIOWrapper(io.BytesIO(b"Pass-123\n"))
        with contextlib.redirect_stdout(io.StringIO()) as out:
            rc = cmd_testaccounts(acct, argparse.Namespace(prefix="test", count=10, gm=False))
    finally:
        sys.stdin = old_stdin
    made = sorted(n for n in acct.store.accounts if n.startswith("TEST"))
    check(rc == 0 and made == ["TEST%02d" % i for i in range(1, 11)] and "Already there, not changed: TEST03" in out.getvalue()
          and "Created 9 test accounts" in out.getvalue() and "PASS-123" not in out.getvalue().upper(),
          "testaccounts test x10: TEST01-TEST10, the existing TEST03 skipped, the password never printed")
    check(all(not acct.store.accounts[n][1] for n in made), "testaccounts without --gm makes no Game Master")
    with contextlib.redirect_stdout(io.StringIO()):
        bad = [cmd_testaccounts(acct, argparse.Namespace(prefix=p, count=c, gm=False)) for p, c in (("TEST", 0), ("TEST", 51), ("TOOLONGPREFIX1234", 10), ("TE ST", 3))]
    check(bad == [2, 2, 2, 2], "testaccounts refuses 0 / 51 accounts, a too long name start and a space")

    print("HTTP service on 127.0.0.1 (pretend database, temporary folders):")
    httpd = AccountServer(("127.0.0.1", 0), acc, read_deadline=1.0)
    port = httpd.server_address[1]
    th = threading.Thread(target=httpd.serve_forever, daemon=True)
    th.start()

    def call(method, path, obj=None, raw=None, headers=None):
        conn = http.client.HTTPConnection("127.0.0.1", port, timeout=5)
        body = raw if raw is not None else (json.dumps(obj).encode("utf-8") if obj is not None else None)
        hdrs = {"Content-Type": "application/json"} if body is not None else {}
        hdrs.update(headers or {})
        try:
            conn.request(method, path, body=body, headers=hdrs)
            resp = conn.getresponse()
            data = resp.read()
            return resp.status, data, resp.getheader("Connection")
        finally:
            conn.close()

    def call_json(*a, **kw):
        status, data, _ = call(*a, **kw)
        try:
            return status, json.loads(data)
        except ValueError:
            return status, data

    try:
        st, data, conn_hdr = call("GET", "/health")
        check(st == 200 and data == b'{"status":"ok","service":"wow-accounts","version":1}' and conn_hdr == "close",
              "GET /health -> 200, exact body, Connection: close")
        check(call_json("GET", "/register")[0] == 404 and call_json("POST", "/health", obj={})[0] == 404
              and call("DELETE", "/register")[0] == 404, "wrong method or path -> 404")
        other = dict(good, somniumId="00000000-0000-0000-0000-000000000001", nick="Wildy")
        check(call_json("POST", "/register", obj=good) == (201, {"status": "created", "name": VECTOR_NAME, "gm": True}),
              "1st register (ID in gm.txt) -> 201 created, gm true")
        check(call_json("POST", "/register", obj=good) == (409, {"status": "exists", "name": VECTOR_NAME}),
              "2nd register of the same player -> 409 exists")
        acc.write_unlock(VECTOR_NAME)
        check(acc.unlock_valid(VECTOR_NAME), "unlock marker written and valid")
        check(call_json("POST", "/register", obj=good) == (200, {"status": "rekeyed", "name": VECTOR_NAME}),
              "register after an unlock -> 200 rekeyed")
        check(not os.path.exists(acc.unlock_path(VECTOR_NAME)) and not os.path.exists(acc.unlock_path(VECTOR_NAME) + ".claimed"),
              "the unlock is used up")
        check(call_json("POST", "/register", obj=good)[0] == 409, "next register -> 409 exists again")
        check(call_json("POST", "/register", obj=dict(good, somniumId="00000000-0000-0000-0000-000000000002", nick="Stranger"))
              == (403, {"status": "not_allowed"}), "player not on the allow list -> 403 not_allowed")
        check(call_json("POST", "/register", obj=other) == (201, {"status": "created", "name": "S0000000000000000", "gm": False}),
              "allowed by nickname -> 201 created, gm false")
        check(call_json("POST", "/register", obj=dict(other, somniumId="00000000-0000-0001-0000-000000000003", nick=" wildy"))
              == (403, {"status": "not_allowed"}), "the same nickname from another Somnium ID -> 403 (a nickname lets in one account)")
        st, res = call_json("POST", "/register", raw=b"{bad json")
        check(st == 400 and isinstance(res, dict) and res.get("status") == "bad_request" and res.get("message"),
              "bad JSON -> 400 bad_request with a message")
        st, res = call_json("POST", "/register", obj=good, headers={"Content-Type": "text/plain"})
        check(st == 400, "wrong Content-Type -> 400")
        players = acc.read_players()
        check(len(players) == 2 and players[0][0] == "S0000000000000000" and players[1][2] == "Shodah",
              "players.tsv records who owns which account")
        old = acc.unlock_path(VECTOR_NAME)
        acc.write_unlock(VECTOR_NAME)
        os.utime(old, (time.time() - UNLOCK_SECONDS - 60,) * 2)
        check(not acc.unlock_valid(VECTOR_NAME) and not os.path.exists(old), "an unlock older than 24 hours is invalid and removed")

        def raw_exchange(request):
            s = socket.create_connection(("127.0.0.1", port), timeout=5)
            s.sendall(request)
            got = b""
            try:
                while True:
                    part = s.recv(4096)
                    if not part:
                        break
                    got += part
            except OSError:
                pass
            s.close()
            return got

        data = raw_exchange(b"POST /register HTTP/1.1\r\nHost: x\r\nContent-Type: application/json\r\nContent-Length: 3000\r\n\r\n")
        check(data.startswith(b"HTTP/1.1 400") and data.endswith(b'"message":"The request is too big."}'),
              "Content-Length over 2048 -> 400 without reading")
        data = raw_exchange(b"POST /register HTTP/1.1\r\nHost: x\r\nContent-Type: application/json\r\n\r\n")
        check(data.startswith(b"HTTP/1.1 400"), "no Content-Length -> 400")
        data = raw_exchange(b"BREW /register HTTP/1.1\r\nHost: x\r\n\r\n")
        check(data.startswith(b"HTTP/1.1 404") and data.endswith(b'{"status":"not_found"}'), "unknown method -> 404 JSON")
        data = raw_exchange(b"GET /health HTTP/9.9\r\n\r\n")
        check(b'{"status":"error"}' in data, "HTTP/9.9 request -> JSON error answer (HTTP/0.9 style, no headers)")
        s = socket.create_connection(("127.0.0.1", port), timeout=5)
        t0 = time.monotonic()
        s.sendall(b"POST /register HTTP/1.1\r\n")
        try:
            data = s.recv(4096)
        except OSError:
            data = b""
        s.close()
        check(data == b"" and time.monotonic() - t0 < 4.0, "a client that stops sending is cut off by the deadline")

        def health_from(src):
            c = socket.socket(socket.AF_INET, socket.SOCK_STREAM)
            c.settimeout(5)
            got = b""
            try:
                if src:
                    c.bind((src, 0))
                c.connect(("127.0.0.1", port))
                c.sendall(b"GET /health HTTP/1.1\r\nHost: x\r\n\r\n")
                while True:
                    part = c.recv(4096)
                    if not part:
                        break
                    got += part
            except OSError:
                pass
            finally:
                c.close()
            return got

        def settled():
            t_end = time.monotonic() + 3.0
            while httpd._per_ip and time.monotonic() < t_end:
                time.sleep(0.02)
            return not httpd._per_ip

        settled()
        idle = []
        for _ in range(MAX_PER_IP):
            c = socket.create_connection(("127.0.0.1", port), timeout=5)
            c.sendall(b"POST /register HTTP/1.1\r\n")      # and then nothing
            idle.append(c)
        time.sleep(0.2)
        t0 = time.monotonic()
        data = health_from(None)
        check(data == b"" and time.monotonic() - t0 < 0.8,
              "one address holds %d connections that send nothing -> its next connection is closed at once" % MAX_PER_IP)
        probe = socket.socket(socket.AF_INET, socket.SOCK_STREAM)
        try:
            probe.bind(("127.0.0.2", 0))
            can_bind = True
        except OSError:
            can_bind = False
        finally:
            probe.close()
        if can_bind:
            check(health_from("127.0.0.2").startswith(b"HTTP/1.1 200"), "meanwhile another address (127.0.0.2) is served")
        else:
            print("  skip  another address is served (127.0.0.2 cannot be used on this machine)")
        for c in idle:
            c.close()
        t_end = time.monotonic() + 3.0
        data = b""
        while time.monotonic() < t_end:
            data = health_from(None)
            if data.startswith(b"HTTP/1.1 200"):
                break
            time.sleep(0.05)
        check(data.startswith(b"HTTP/1.1 200") and settled(), "when they close, that address is served again; no connection is counted any more")
        # Calls counted so far from 127.0.0.1 (limit 9 in this test): 1 created, 2 exists, 3 rekeyed, 4 exists,
        # 5 not_allowed, 6 created, 7 not_allowed (nickname taken), 8 bad JSON, 9 wrong Content-Type -> the 10th is refused.
        check(call_json("POST", "/register", obj=good) == (429, {"status": "slow_down"}), "over the per-IP limit -> 429 slow_down")
    finally:
        httpd.shutdown()
        httpd.server_close()
        shutil.rmtree(tmp, ignore_errors=True)

    return failures


def selftest():
    global _log_capture
    captured = []
    _log_capture = captured
    try:
        failures = _selftest()
    except Exception as e:                      # a crash in the test itself is a failure too
        failures = ["selftest crashed: %s: %s" % (type(e).__name__, e)]
    finally:
        _log_capture = None
    print("Service log lines written during the HTTP test (expected, for reference):")
    for line in captured:
        print("    " + line)
    if failures:
        print("SELFTEST FAILED (%d): %s" % (len(failures), "; ".join(failures)))
        return 1
    print("SELFTEST OK")
    return 0


# ---------------------------------------------------------------------------------------------
# Command line
# ---------------------------------------------------------------------------------------------
def build_parser():
    p = argparse.ArgumentParser(prog="wow_accounts.py", description="Dreamport automatic WoW accounts (see the file header).")
    p.add_argument("--selftest", action="store_true", help="print the PLAN8 2.3 test vector and run all offline checks")
    p.add_argument("--dry-run", action="store_true", help="no database writes and no file changes; print the SQL instead")
    p.add_argument("--port", type=int, default=DEFAULT_PORT, help="service port (default %d)" % DEFAULT_PORT)
    p.add_argument("--bind", default=DEFAULT_BIND, help="service address (default %s)" % DEFAULT_BIND)
    p.add_argument("--etc-dir", default=ETC_DIR, help="folder with allow.txt and gm.txt (default %s)" % ETC_DIR)
    p.add_argument("--state-dir", default=STATE_DIR, help="folder with unlocked/ and players.tsv (default %s)" % STATE_DIR)
    sub = p.add_subparsers(dest="command")
    c = sub.add_parser("create", help="create a normal account; the password is read from stdin")
    c.add_argument("name")
    c = sub.add_parser("testaccounts", help="create PREFIX01..PREFIXnn normal accounts with one password read from stdin")
    c.add_argument("--prefix", default="TEST")
    c.add_argument("--count", type=int, default=10)
    c.add_argument("--gm", action="store_true")
    c = sub.add_parser("unlock", help="allow one re-key of an automatic account within 24 hours")
    c.add_argument("who")
    c = sub.add_parser("allow", help="add (or --remove) a Somnium nickname or ID in allow.txt")
    c.add_argument("--remove", action="store_true")
    c.add_argument("who")
    c = sub.add_parser("gm", help="make an account GM (level 3, all realms) or a normal player")
    c.add_argument("who")
    c.add_argument("state", choices=["on", "off"])
    sub.add_parser("list", help="accounts, allow list, GM list, unlocks")
    sub.add_parser("dbcheck", help="read-only database test")
    return p


def main(argv=None):
    args = build_parser().parse_args(argv)
    if args.selftest:
        return selftest()
    if not 1 <= args.port <= 65535:
        print("--port must be 1-65535")
        return 2
    if args.command is None:
        return serve(args)
    store = MemoryStore(echo=True) if args.dry_run else MysqlStore()
    accounts = Accounts(store, args.etc_dir, args.state_dir, dry_run=args.dry_run)
    commands = {"create": cmd_create, "testaccounts": cmd_testaccounts, "unlock": cmd_unlock, "allow": cmd_allow, "gm": cmd_gm,
                "list": cmd_list, "dbcheck": cmd_dbcheck}
    try:
        return commands[args.command](accounts, args)
    except AdminError as e:
        print(str(e))
        return 1
    except DbError as e:
        print("The database did not answer: %s" % e)
        print("Is the realm running? Check with: sudo bash ~/status.sh")
        return 1
    except PermissionError:
        print("Permission denied - run this with sudo.")
        return 1


if __name__ == "__main__":
    sys.exit(main())
