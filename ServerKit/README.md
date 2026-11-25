# Dreamport ServerKit - your own WoW 3.3.5a realm (AzerothCore) on a cloud server

The WoW Dreamport ships no NPCs, names or quests: like the real client, it gets every creature, player
and object (at its real position) from a server. This kit sets up that server: **AzerothCore**, the
open-source 3.3.5a (build 12340) server, with **Warden switched off** (the Dreamport client never
answers Warden). Friends can join the same realm with a normal 3.3.5a client.

Two places it runs:
- **Oracle Cloud Always Free** (free): an Ampere A1 ARM server, 2 cores / 12 GB. AzerothCore is compiled
  on it on the first setup (there are no ready-made ARM images). Step 1A.
- **DigitalOcean droplet** (paid, from $24/month recommended): x64, AzerothCore's ready-made images. Step 1B.

Files in this folder:

| File | What it is |
|---|---|
| `setup-server.sh` | One script that turns a fresh Ubuntu 24.04 server (Oracle ARM or x64 droplet) into a running realm (safe to run again) |
| `status.sh` | One-shot health check (run it on the server) |
| `setup-accounts.sh` + folder `account-service` | Optional: automatic WoW accounts for Somnium players ("Play online as ..."), section 8 |
| `Manage WoW accounts.cmd` | Double-click on your PC: the menu for those accounts on your server (section 8) |
| `setup-hold.sh` + folder `dreamport-hold` | Optional: immersive VR casting on your realm - the cast starts at once and waits at its end for the push (section 9) |
| `connect.md` | The SSH commands the lead (Claude) uses to run and check the kit for you |
| `README.md` | This guide |

The script never creates game accounts or passwords - you do that yourself in step 3 (or let the
Dreamport do it automatically for the Somnium players you allow, section 8).

---

## 1. Create the server

**SSH key** (once, both options). If `C:\Users\<you>\.ssh\wow_droplet` does not exist yet, open
PowerShell and run `ssh-keygen -t ed25519 -f "$env:USERPROFILE\.ssh\wow_droplet" -C wow_droplet`
(a passphrase is optional). The private file `wow_droplet` never leaves your PC; the cloud only gets
the public file `wow_droplet.pub`.

### 1A. Oracle Cloud Always Free (free)

Oracle's free tier (docs.oracle.com "Always Free Resources", checked 2026-09-27): Ampere A1 ARM compute
worth **2 OCPUs and 12 GB of memory** (1,500 OCPU hours + 9,000 GB hours a month), **200 GB** of block
storage (boot volumes included), 10 TB of outbound data a month.

1. **Sign up** at oracle.com/cloud/free ("Start for free"). A card is asked for identity verification.
   The **home region** cannot be changed later and Always Free servers live there: pick the one closest
   to you and your players.
2. **Menu -> Compute -> Instances -> Create instance**:
   - **Name**: e.g. `wow-server`.
   - **Image**: *Change image -> Ubuntu -> Canonical Ubuntu 24.04* (not "Minimal").
   - **Shape**: *Change shape -> Ampere -> VM.Standard.A1.Flex*, **2 OCPUs, 12 GB memory**
     ("Always Free-eligible").
   - **Networking**: create a new virtual cloud network and a new public subnet (the defaults), with
     **Assign a public IPv4 address** on.
   - **SSH keys**: *Paste public keys*, paste the content of `wow_droplet.pub`.
   - **Boot volume**: *Specify a custom boot volume size*, **100 GB** (the compile and the Docker images
     need the room; the free 200 GB covers it).
   - **Create**. "Out of host capacity" means the region has no free ARM servers at the moment: choose
     another availability domain, or try again later.
3. **Open the game ports** in Oracle's network (the server's own firewall is done by the script):
   instance page -> the **subnet** link -> *Security Lists* -> **Default Security List** ->
   **Add Ingress Rules**: source CIDR `0.0.0.0/0`, IP protocol **TCP**, destination port range **3724**;
   *+ Another Ingress Rule* with port **8085**; save. (SSH port 22 is open by default.)
4. Copy the instance's **Public IP address** (instance page). Everything below calls it `IP`. On Oracle
   you log in as **`ubuntu`** (not root) and use `sudo`.

Good to know:
- Oracle "may reclaim" Always Free servers that look idle for 7 days (95th-percentile CPU, network and
  memory all under 20%). `status.sh` shows the memory and CPU use of the running realm.
- Upgrading the account to Pay As You Go gives more capacity (and ends "out of host capacity");
  Oracle says it still does not charge for Always Free resources, only for use above those limits.

### 1B. DigitalOcean droplet (paid)

1. **Create -> Droplets**:
   - **Region**: the one closest to you and your friends (lower ping).
   - **Image**: *Ubuntu 24.04 (LTS) x64* - plain Ubuntu (the prebuilt AzerothCore images are x64 only).
   - **Size**: *Basic -> Regular* - **4 GB RAM / 2 vCPUs / 80 GB SSD ($24/month)** is the recommended
     start (MySQL, worldserver and authserver run together, and the first start imports the whole world
     database). The $12 plan (2 GB / 1 vCPU) may work together with the 2 GB swap the script adds, but
     that is not measured; `status.sh` shows the real memory use, and a droplet can be resized later.
   - **Authentication**: *SSH Key -> New SSH Key*, paste the content of `wow_droplet.pub`.
   - **Hostname**: e.g. `wow-realm`. Then **Create Droplet**.
2. Copy the droplet's **IPv4 address** (shown in the droplet list). Everything below calls it `IP`.
   On DigitalOcean you log in as **`root`**.
3. Only if you also add a DigitalOcean *Cloud Firewall* to the droplet: allow inbound TCP **22, 3724, 8085**
   there as well (the script already configures the firewall on the droplet itself).

## 2. Run the setup

Oracle (ARM): the first run compiles AzerothCore - roughly 1-3 hours on 2 cores (not measured yet) -
then 15-40 minutes of data download and database import. DigitalOcean: 15-40 minutes.

**Easiest:** tell the lead (Claude) the IP - it runs the commands in `connect.md` with your key
`~/.ssh/wow_droplet` and reports back when the realm is up.

**Yourself** (PowerShell or Git Bash on your PC, in this ServerKit folder; Oracle user `ubuntu`,
DigitalOcean user `root`):

```bash
scp -i ~/.ssh/wow_droplet setup-server.sh status.sh ubuntu@IP:
ssh -i ~/.ssh/wow_droplet ubuntu@IP
sudo PUBLIC_IP=IP bash setup-server.sh
```

(`PUBLIC_IP` is required on Oracle: the server itself only knows its private address. If your
connection may drop during the wait, start it detached instead:
`sudo PUBLIC_IP=IP nohup bash setup-server.sh >/dev/null 2>&1 </dev/null &` and watch with
`sudo tail -f /var/log/wow-setup.log`.)

What the script does, in order (everything is logged to `/var/log/wow-setup.log`):

1. Adds a 2 GB swap file on servers with less than 8 GB RAM.
2. Installs git and Docker Engine + the compose plugin from **Docker's official apt repository**
   (the guide AzerothCore's wiki links for Linux; it supports Ubuntu 24.04 and has the current
   `docker compose`).
3. Firewall: opens only 22 (SSH), 3724 (login) and 8085 (world).
   - Oracle: in `/etc/iptables/rules.v4` (a backup of the original is kept next to it). Oracle's docs say
     not to use ufw on their Ubuntu images - it can stop the server from booting.
   - Elsewhere: ufw.
4. Clones AzerothCore (`azerothcore-wotlk`, shallow) to `/opt/azerothcore`.
5. Configures it the way AzerothCore documents (nothing in their files is edited):
   - `docker-compose.override.yml` sets `AC_WARDEN_ENABLED: "0"` -> **Warden off**. (AzerothCore reads
     `AC_` + the config key in upper snake case before the config file, so this overrides
     `Warden.Enabled = 1` from `worldserver.conf`.)
   - `.env` binds the database (3306) and SOAP (7878) ports to `127.0.0.1`. Docker's own documentation
     warns that ports published by Docker bypass ufw, so without this the database (default root
     password `password`) would be reachable from the internet.
6. Starts AzerothCore with **its own `docker compose`**:
   - x64: its **prebuilt images** from Docker Hub (nothing is compiled).
   - ARM (Oracle): the same images, **compiled on the server** from the clone with AzerothCore's own
     Dockerfile (only the server programs and `dbimport`; the map-extractor tools are skipped).

   On the first start the compose setup downloads the server's client data (maps, 1.2 GB, extracted
   for the server - your game files are not needed) and imports the databases; AzerothCore's wiki says
   10-15 minutes.
7. Sets the realm's address (`acore_auth.realmlist`) to the server's public IP, waits until the
   worldserver says `ready` and both ports answer, and prints a short status ending with `SETUP OK`.

## 3. Create your account (you, in the worldserver console)

```bash
ssh -i ~/.ssh/wow_droplet ubuntu@IP          # DigitalOcean: root@IP
sudo docker attach ac-worldserver
```

Press **Enter** once: the prompt `AC>` appears. Then type (with your own name and password):

```
account create MYNAME MYPASSWORD
account set gmlevel MYNAME 3 -1
```

- `account create <name> <password>` - name up to 17 characters, password up to 16; neither name nor
  password is case-sensitive (AzerothCore upper-cases both). New accounts get Wrath of the Lich King
  content (expansion 2).
- `account set gmlevel <name> <level> <realm>` is optional: level 3 = administrator (GM commands),
  `-1` = all realms. Leave it out for a normal player account. From the console all three values are
  needed.
- **Detach with Ctrl+P, then Ctrl+Q.** Do **not** press Ctrl+C - that stops the worldserver
  (AzerothCore's wiki warns about exactly this). Then `exit` closes the SSH session.

About the password you type: the console shows it on screen, and Docker keeps the console's screen
output in the container log (`docker logs ac-worldserver`). Console commands are not written to the
server's GM log, but treat the Docker log as containing it. Either
- create the account with a temporary password and change it after your first login with the in-game
  chat command `.account password <old> <new> <new>`, or
- after creating accounts, run `cd /opt/azerothcore && sudo docker compose up -d --no-build --force-recreate ac-worldserver`
  (a fresh container starts with an empty log; this restarts the worldserver, about a minute).

Friends need their own accounts: create them the same way (or let them choose the password and
change it in-game as above).

## 4. Connect

**The Dreamport (Somnium Space):** on the WoW login panel enter the server IP as the server
(port 3724), then your account name and password. The Dreamport uses the password only to log in to
your authserver and never stores it (only the account name is remembered).

**Friends with a normal 3.3.5a client (build 12340):**

1. In the WoW folder open `Data\enUS\realmlist.wtf` (the locale folder may be `enGB`, `deDE`, ...;
   use the one that exists) and make its only line:
   `set realmlist IP`
2. If `WTF\Config.wtf` has a line `SET realmList "..."`, change it to `SET realmList "IP"` as well.
3. Start `Wow.exe` and log in with the account you created for them.

Your own install currently points at Warmane (`set realmlist logon.warmane.com` in
`Data\enUS\realmlist.wtf` and `SET realmList "logon.warmane.com"` in `WTF\Config.wtf`). If you also
want to play your realm with the normal client, change both lines as above; change them back to play
on Warmane. The Dreamport itself does not need either file.

Note: with Warden off, the realm has no client anti-cheat - invite people you trust.

## 5. Everyday: status, restart, stop, update

All on the server (`ssh -i ~/.ssh/wow_droplet ubuntu@IP`, DigitalOcean `root@IP`), inside
`cd /opt/azerothcore` (on DigitalOcean as root, leave out `sudo`):

| Task | Command |
|---|---|
| Health check | `sudo bash ~/status.sh` (last line `STATUS OK` or `STATUS PROBLEM: ...`) |
| Console (accounts, GM commands) | `sudo docker attach ac-worldserver` - leave with Ctrl+P, Ctrl+Q |
| Restart the world server | `sudo docker compose restart -t 60 ac-worldserver` |
| Restart everything | `sudo docker compose restart -t 60` |
| Stop the realm | `sudo docker compose stop -t 60` |
| Start it again | `sudo docker compose up -d --no-build` |
| Logs | `sudo docker compose logs --tail 100 ac-worldserver` (files: `env/dist/logs/`) |

The containers restart by themselves after a crash or a server reboot (`restart: unless-stopped` in
AzerothCore's compose file) unless you stopped them with `docker compose stop`.

**Update AzerothCore** (new fixes; players are disconnected; on ARM this compiles again, faster than
the first time thanks to the compiler cache):

```bash
cd /opt/azerothcore
sudo mkdir -p /root/backups
sudo docker exec ac-database sh -c 'exec mysqldump -uroot -p"$MYSQL_ROOT_PASSWORD" --databases acore_auth acore_characters' | sudo tee /root/backups/acore-$(date +%F).sql >/dev/null
sudo git pull --ff-only
sudo docker compose pull                     # x64 (prebuilt images)
# ARM instead: sudo docker compose build ac-worldserver && sudo docker compose build ac-authserver ac-db-import ac-client-data-init
sudo docker compose up -d --no-build
sudo bash ~/status.sh
```

The backup holds your accounts and characters. The Warden-off, database-port and build settings live
in `.env` and `docker-compose.override.yml`, which AzerothCore's `.gitignore` excludes, so updates keep
them. Running `setup-server.sh` again repairs the setup but never updates AzerothCore.

**Optional hardening:** the database root password is AzerothCore's default `password`; the database
is only reachable from inside the server. To use your own, add `DOCKER_DB_ROOT_PASSWORD=...` to
`/opt/azerothcore/.env` **before the very first start** (MySQL only takes it when its data volume is
created). The authserver's `WrongPass.MaxCount = 0` (no lockout after wrong passwords) can be changed
the same way as Warden, with `AC_WRONG_PASS_MAX_COUNT` under `ac-authserver` in the override file.

## 6. Costs

**Oracle Cloud Always Free:** nothing, as long as only Always Free resources are used (the A1 server up
to 2 OCPUs / 12 GB, up to 200 GB of volumes). Oracle may reclaim idle Always Free servers (step 1A).

**DigitalOcean** (list prices, checked 2026-09-27):

| Droplet (Basic, Regular SSD) | $/month | $/hour | Transfer included |
|---|---|---|---|
| 2 GB RAM / 1 vCPU / 50 GB | $12 | $0.01786 | 2,000 GiB |
| 2 GB RAM / 2 vCPUs / 60 GB | $18 | $0.02679 | 3,000 GiB |
| **4 GB RAM / 2 vCPUs / 80 GB (recommended)** | **$24** | **$0.03571** | 4,000 GiB |
| 8 GB RAM / 4 vCPUs / 160 GB | $48 | $0.07143 | 5,000 GiB |

- Billing is hourly, capped at 672 hours (28 days) per month.
- **A powered-off droplet is still billed.** To stop paying, destroy it. To keep the realm for later,
  take a snapshot first ($0.06 per GB per month) and create the new droplet from it.
- Automatic backups are optional: +20% (weekly) or +30% (daily) of the droplet price.

## 7. If something is wrong

| Symptom | Check |
|---|---|
| `SETUP FAILED` | the lines above it in `/var/log/wow-setup.log`; fix, then run the script again |
| Client stuck at "Connecting" | `status.sh`; Oracle: the Default Security List must allow TCP 3724 and 8085 (step 1A.3); DigitalOcean: a Cloud Firewall must allow them |
| Realm list shows the realm offline / cannot enter | `status.sh` (realm address must be the server's public IP; worldserver must be `ready`) |
| Dreamport says "server has Warden on" | `status.sh` shows the Warden line; `docker-compose.override.yml` must set `AC_WARDEN_ENABLED: "0"`, then `docker compose up -d --no-build` |
| Worldserver keeps restarting | `docker compose logs --tail 100 ac-worldserver`; memory in `status.sh` (resize the droplet if RAM is full) |
| ARM compile failed | the compiler errors above `SETUP FAILED` in `/var/log/wow-setup.log` (AzerothCore publishes no ARM builds, so ARM compiles are not tested by them) |
| Oracle "Out of host capacity" | another availability domain, later, or a Pay As You Go account (step 1A) |

## 8. Automatic accounts ("Play online as <your Somnium name>")

With this, nobody has to create WoW accounts by hand for Somnium players. In the WoW Dreamport's
Online tab a player presses **Play online as <their Somnium nickname>**. The first time, the Dreamport
asks your server for a WoW account, your server makes it (only for players you allowed), and the player
is logged in. After that the same button simply logs in.

How it works, in short:
- The **account name** comes from the player's Somnium ID: `S` + 16 characters. Shodah's account is
  `SCF5C553C61F843F4`.
- The **password** is a random 16-character key that the Dreamport makes and keeps in the player's own
  Somnium storage for this world. Nobody types it or sees it, and it is never sent to your server:
  the Dreamport sends only the one-way values AzerothCore stores for every account (the same values the
  console makes when you create an account by hand). The Somnium ID itself cannot be the password,
  because every Somnium world can read it.
- Only players on your **allow list** get an account. At the start: `Shodah`, `Wildy` and Shodah's
  Somnium ID.
- An existing account is **never changed**, except once after you **unlock** it (a new computer, see
  below). Characters always stay.
- Somnium IDs in the **GM list** become Game Masters when their account is made. At the start: Shodah.
- The old way still works: the Dreamport's "Use a different account" is the normal login form, and
  friends with a normal WoW client get an account from the menu (choice 4).

### Set it up (once, a few minutes)

**Step 1 - install it on the server.** Easiest: tell the lead (Claude) "set up the automatic accounts";
it runs the commands in `connect.md`. Yourself: open PowerShell in this ServerKit folder and run

```bash
scp -i ~/.ssh/wow_droplet -r setup-accounts.sh status.sh account-service ubuntu@145.241.232.229:
ssh -i ~/.ssh/wow_droplet ubuntu@145.241.232.229 "sudo bash setup-accounts.sh"
```

It takes a few seconds and ends with **`ACCOUNTS OK`** (or `ACCOUNTS FAILED` and the reason). Running it
again is safe: it updates the program and keeps your allow list and GM list.

**Step 2 - open port 8086 in Oracle's network** (the server's own firewall was done by step 1). The same
place as the game ports in step 1A.3:
1. Oracle Cloud console -> **Menu -> Compute -> Instances** -> click your server (e.g. `wow-server`).
2. On the instance page click the **subnet** link.
3. **Security Lists** -> **Default Security List**.
4. **Add Ingress Rules**: source CIDR `0.0.0.0/0`, IP protocol **TCP**, destination port range **8086**
   (description e.g. `WoW accounts`). Leave the other fields as they are. Save with **Add Ingress Rules**.

(DigitalOcean: only if you use a Cloud Firewall, allow inbound TCP 8086 there too.)

**Step 3 - check.** On your PC open `http://145.241.232.229:8086/health` in a web browser. It should
show `{"status":"ok","service":"wow-accounts","version":1}`. If the page does not load, step 2 is
missing. Then press **Play online** in the Dreamport.

### Everyday: the menu

Double-click **`Manage WoW accounts.cmd`** in this ServerKit folder. A black window opens with this menu
(it runs on your server; type a number and press Enter):

| Choice | What it does |
|---|---|
| 1 Let a Somnium player in | Adds their Somnium nickname (exactly as Somnium shows it) or Somnium ID to the allow list. Then they press Play online. |
| 2 Show the accounts and who is allowed | All accounts (name, GM, last login, which Somnium player), the allow list, the GM list, open unlocks, and players who were **turned away** recently, with their Somnium ID. |
| 3 Unlock a player's account | For a player on a new computer (see below). |
| 4 Create an account for a friend | For a friend with a normal WoW 3.3.5a client: you choose the name and type the password twice (it is not shown). |
| 5 Game Master on or off | For any account; it takes effect at the player's next login. |
| 6 Take a player off the allow list | They cannot get a new account; an account they already have stays. |
| 7 Check that everything works | Service, port, database, firewall and self-test; ends with `CHECK OK`. |
| 0 Quit | Closes the menu (then press a key to close the window). |

The same things work as commands on the server, e.g. `sudo wow-account allow Wildy`,
`sudo wow-account list`, `sudo wow-account unlock Wildy` (`sudo wow-account help` lists them all).

**Who is allowed.** A Somnium ID on the list always works. A nickname on the list lets in **one**
Somnium account: the first one that gets an account with it (anyone could type any nickname, so a
nickname cannot let in more than one account). If a player is turned away, choice 2 shows their
nickname and Somnium ID; add the ID with choice 1.

**A new computer (unlock).** The key lives in the player's Somnium storage. On another computer, or after
that storage was cleared, the Dreamport has no key and tells the player to ask you to unlock the account.
Choose 3 and type their nickname, Somnium ID or account name. Within 24 hours the player presses Play
online once: the Dreamport makes a new key, the characters stay, the old key stops working, and the
account is locked again. (Unlock is only for automatic accounts, the `S...` names; a nickname always means
that player's automatic account, even if a normal account has the same name.) Unlock only when the player
is ready to press the button - during the unlock, whoever presses Play online first with that Somnium ID
gets the account.

**Game Masters.** New automatic accounts of the Somnium IDs in the GM list get GM level 3 on all realms.
For any other account use choice 5. If a player's nickname is also the name of a normal account (for
example your own account from section 3), choice 5 does not guess: type the automatic account's name
(`S...`) or the Somnium ID, and change the normal account in the worldserver console (section 3).

In the Dreamport, Settings has **"Forget this computer's key"** (for a shared computer). After that the
player needs an unlock to play from that computer again.

### What players may see, and what to do

| The Dreamport says | What to do |
|---|---|
| This server doesn't know your Somnium account yet - ask the server owner to add you. | Choice 1 (their nickname or Somnium ID; choice 2 shows the ID of players who were turned away). |
| Your Somnium account already has a WoW account, but this computer doesn't have its key. Ask the server owner to unlock it (account S...), then press Play online again. | Choice 3 (their nickname, Somnium ID, or the account name the message shows). |
| This computer's key for your WoW account no longer matches the server. Ask the server owner to unlock it (account S...), then press Play online again. | Choice 3, as above. (The key on that computer is old: the account was unlocked and got a new key elsewhere, or the realm's database was reset.) |
| Your WoW account is ready, but this computer could not save its key. You can play now; next time the server owner has to unlock it. | Nothing now. The next time that player presses Play online: choice 3. |
| Too many tries in a short time. Wait ten minutes, then press Play online again. | More than 5 tries in 10 minutes from one internet address, or more than 60 requests of allowed players in an hour in total. Just wait. |
| The account service did not accept the request: ... (the rest says what was wrong) | Usually the world and the service are different versions: copy the ServerKit that came with this world and run step 1 again. |
| The account service has a problem on the server. Try again later, or tell the server owner. | The database did not answer (is the realm running?): `sudo bash ~/status.sh`; the reason is in `sudo journalctl -u wow-accounts -n 30`. |
| The account service on 145.241.232.229 (port 8086) gave an answer this world doesn't understand - it may be a different version. | Another program uses port 8086, or the service is out of date: run step 1 again. |
| Can't reach the account service on 145.241.232.229 (port 8086). | Step 2 (Oracle ingress rule 8086); then choice 7, or `sudo bash ~/status.sh` on the server. |

If the message starts with "The server did not accept this computer's key.", the key on that computer was
refused and the Dreamport asked the account service once more; the rest of the message is the answer.

### Safety, and where things are

- The service never receives, stores or writes down a password. Its log (`sudo journalctl -u wow-accounts`)
  has the account name, Somnium ID, nickname, internet address and the result of each request.
- It only makes new accounts (never changes one, except once after an unlock), only for the allow list,
  and only the IDs in the GM list become GM.
- Limits: 5 requests per 10 minutes per internet address, 60 per hour in total (only correct requests of
  allowed players count towards the 60, so strangers cannot use them up), at most 8 connections at once
  and at most 2 from one internet address, requests larger than 2 KB are refused, and a connection that
  has not sent its request within 10 seconds is closed.
- Port 8086 is plain (unencrypted) HTTP, but it carries no password - only the name, Somnium ID,
  nickname and the one-way values, which cannot be turned back into the random key.
- To switch it off: `sudo systemctl disable --now wow-accounts` (the accounts stay; `setup-accounts.sh`
  switches it on again).

| What | Where on the server |
|---|---|
| The service | `/opt/wow-accounts/wow_accounts.py`, systemd unit `wow-accounts` (restarts by itself) |
| The menu / admin tool | `/usr/local/bin/wow-account` (`sudo wow-account`) |
| Allow list, GM list | `/etc/wow-accounts/allow.txt`, `/etc/wow-accounts/gm.txt` (one entry per line, `#` = note) |
| Unlocks, who owns which account | `/var/lib/wow-accounts/unlocked/`, `/var/lib/wow-accounts/players.tsv` |
| Setup log | `/var/log/wow-accounts-setup.log` |
| Firewall line | `/etc/iptables/rules.v4` (original: `rules.v4.before-wow-accounts`) |

---

## 9. Immersive VR casting on your realm (optional, stage S1)

The Dreamport's **Immersive casting** (VR, Settings > Gameplay, off by default): you pick a spell with a cast time,
**the cast starts at once**, and at its end the spell does not fire - the controller vibrates and the spell waits in
your hand (glowing, pulsing) until you **push the hand forward**. Instant spells fire at once as always.

A stock AzerothCore fires every finished cast at once, so waiting needs a small addition: the module in the folder
`dreamport-hold` (AzerothCore's own module system, none of AzerothCore's files are changed).

**It affects only Dreamport VR players who switched Immersive casting on.** The Dreamport asks the realm for a hold
cast by cast, only for such a player and only for spells with a cast time. Normal WoW clients never ask, desktop (2D)
Dreamport players never ask - their casts are exactly as before. A held cast behaves like a cast in progress: moving
interrupts it, no other spell meanwhile, mana is paid when it fires, it is cancelled after 30 s and never survives a
logout. Details and the AzerothCore source lines it relies on: `dreamport-hold/README.md`.

Without the module the Dreamport still works: it then holds the spell on your side BEFORE sending it (the cast bar
runs after the push, as in stage O2) - the Player.log says which one your realm does (see below).

**The immersive damage bonus (a Dreamport house rule of your realm, stage S1):** a spell a VR player releases with
Immersive casting (a held cast pushed, or an instant / channel cast through it) deals **+10 % damage** - damage only, never
healing, only that cast, only for the player whose client flags it (`DPHOLD HOLD` / `DPHOLD BONUS`; normal WoW clients and
desktop Dreamport players never do). The Dreamport shows it as a buff "Immersive Mode" on the character; normal WoW clients do
not see that buff. The same module applies it (details and the AzerothCore hooks: `dreamport-hold/README.md`).

### Install (once; again after an AzerothCore update)

The lead (Claude) can do it for you with `connect.md`; yourself, on your PC in this ServerKit folder:

```bash
scp -r -i ~/.ssh/wow_droplet setup-hold.sh dreamport-hold ubuntu@IP:
ssh -i ~/.ssh/wow_droplet ubuntu@IP
sudo bash setup-hold.sh
```

It copies the module to `/opt/azerothcore/modules/mod-dreamport-hold`, compiles the worldserver with AzerothCore's own
`docker compose build ac-worldserver` (ARM: faster than the first setup thanks to the compiler cache; x64 servers that
used the prebuilt images compile AzerothCore for the first time - an hour or more), restarts **only the worldserver**
(players online are disconnected for about a minute), and ends with `HOLD OK` or `HOLD FAILED`. If the compile fails,
the running realm is not touched and the module is taken out again. Log: `/var/log/wow-hold.log`. Safe to run again.

**Updates:** `git pull` never touches the module (AzerothCore's `.gitignore` ignores `/modules/*`). On ARM the update
commands of section 5 compile it in again by themselves. On **x64**, `docker compose pull` brings back AzerothCore's
prebuilt worldserver WITHOUT the module - run `sudo bash setup-hold.sh` after every update.

**Remove it:** `sudo rm -rf /opt/azerothcore/modules/mod-dreamport-hold`, then on ARM
`cd /opt/azerothcore && sudo docker compose build ac-worldserver && sudo docker compose up -d --no-build --no-deps --force-recreate ac-worldserver`
(x64: `sudo docker compose pull ac-worldserver && sudo docker compose up -d --no-build --no-deps --force-recreate ac-worldserver`).

### Check it

- Server: `sudo docker compose logs ac-worldserver | grep "Dreamport hold"` shows
  `Dreamport hold: module loaded (protocol 1, hold time-out 30000 ms, arm window 5000 ms) ...` and `Dreamport hold: immersive damage bonus +10% ...`, then, once per login of a
  Dreamport player, `Dreamport hold: <name> (...) said hello (a Dreamport client, protocol 1)` (every Dreamport client says hello; only VR immersive casts are held).
- Player.log (tag `[SomniumWoW]`) after entering the world: `Online: this realm holds immersive casts (Dreamport hold
  module, protocol 1, time-out 30 s) ...` - or, without the module, `Online: this realm has no Dreamport hold ...`.
