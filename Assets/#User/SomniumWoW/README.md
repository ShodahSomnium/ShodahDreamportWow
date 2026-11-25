# World of Warcraft 3.3.5a Dreamport (Somnium Space)

World of Warcraft: Wrath of the Lich King (3.3.5a, build 12340) rebuilt inside a Somnium Space world. The world ships
**no game assets**: every terrain tile, building, tree, character, creature, icon, spell, sound, music track, particle
texture, class and race text, font and loading screen is read at runtime from the player's own WoW 3.3.5a install
(default `D:\WOTLK\World of Warcraft 3.3.5a`, enUS, all data inside the MPQ archives). Our code carries only ids,
coordinates, numbers and our own quest/NPC text (quest text, NPC personal names and greetings are our own writing).

**Iteration 11 ("Complete", 2026-10-03)** adds what is needed to play the whole game in Somnium on your own realm, next to
friends in the normal WoW client: **vendors** (buy, sell, buy back, repair), **bags and the bank** (WoW's bag frames, drag items
between slots, drag an item out of the bags to destroy it), **trainers and professions**, **talents and glyphs**, the
**Character, Reputation, Currency and Achievement** windows, **friends, guilds, mail, chat channels and emotes**, **trade, duels,
inspect, raids, ready checks and raid icons**, the **Auction House, battlegrounds and the Dungeon Finder**, **flight paths, ships
and zeppelins online**, **Charge / knockbacks that move your avatar**, **ground spells with WoW's green circle** (Blizzard,
Consecration ...), **weapon enchant glows**, **instances** (only your group is seen inside), **pets, totems and vehicles**,
**druid forms and polymorph on your Somnium avatar**, **WoW's full-body combat animation** on desktop, **immersive armour**
(helm + shoulders, a setting, off by default), **all four extra action bars**, **VR rows** (push or pull the hand while holding grip
to switch the spell row), **VR spell slotting** from the spellbook, **immersive strike** for melee classes (a setting, off by default),
a more reliable **corpse run**, and the **Elwynn corpse quest** ("A half-eaten body" / "Rolf's corpse") now works. On desktop the
mouse works on every window as in WoW (WoW's panel rules, cursor art, Escape order). The test guide is in **"Iteration 11 - the
complete game (2026-10)"** below; what is not done yet is in **"Known gaps (iteration 11)"**.

**Iteration 9 (stage S1, social)** adds what the online test with a friend asked for: **chat** (WoW's chat frame on desktop, a
**Chat** window from the VR palm menu, **chat bubbles** over whoever speaks - over the Somnium avatar for a Dreamport player),
**groups** (invite, WoW's invite popup, **party frames** with live health / power and the **Somnium profile picture** of a Somnium
player; click a frame or point the laser at it to target a friend for a heal), **Settings > HUD** (move and resize the HUD,
desktop and VR), a **focus** (short press X in VR, H on desktop; three presses clear it), **the Somnium laser for selecting**
(our yellow dot and second line are gone), **Return to the room** moved into Settings (the wrist button is gone), the **Somnium
name tags hidden** in this world, **instance diagnostics** in Player.log, a **game folder browser** (Settings > Game), and
**immersive casting that starts the cast at once and waits for your push**, with **+10 % damage** on spells you release that way
and a buff "Immersive Mode" on your character (a Dreamport house rule of your realm). **Online, the immersive hold and its +10 %
need the small server module the lead installs first** (below); until then immersive casting works as in stage O2 and online
there is no buff. Everything else works without it. The step-by-step test is in **"Social, HUD, focus, immersive hold (stage
S1)"** below. This upload is also the first test of the Bridge name-tag call and of the profile-picture download.

**Iteration 8 (stage O2, online play)** makes the online game playable on your own AzerothCore realm, the way the real client
plays it: **the server decides everything, the Dreamport shows it** with the windows, animations, sounds and spell effects the
offline game already had. Your character now has its **real gear and bags** from the server (equip, food, Hearthstone, quest
items); you can **fight** (auto-attack, XP, level-ups, kills), **cast every class spell**, **loot**, **take and hand in quests** and
talk to NPCs, open chests, herbs, ore and posters. **Every creature and player swings and casts with its animations, sounds and
spell effects** - also normal WoW players next to you. **Dying works the WoW way, online and offline**: Release Spirit, a ghost at
the graveyard in a grey-blue world with WoW's ghost music, an arrow and a marker to your corpse (also on the WoW Map), Resurrect
near the corpse or at the spirit healer. **Try the death flow offline first** (no server needed); the step-by-step test is in
**"Online: combat, items, death, quests (stage O2)"** below. Other Somnium players see your casts and held VR spells on your
avatar's hands (online and offline). Also: on foot your avatar moves at your character's WoW run speed, VR wrist / palm windows
close when you walk away from them, and holding the Somnium tablet no longer fires the wrist spell buttons. Vendors, trainers,
flight masters and mail online come in stage O3b (chat and groups: stage S1, iteration 9, above).

**Iteration 7 (mounts)** adds **mounts, offline and online**. **You can try them right now in Offline mode - no server, no
login, no account needed** (only Online mode logs in to a server): make or pick any character in Offline mode, press
**`J`** (VR: palm menu > **Mounts**), pick the **Brown Horse** (Horde: **Timber Wolf**), press **Mount** while standing still.
(If you tried Online before, click **Offline** on the Play tab first - it remembers your last choice.)
Two testing switches in Settings > Gameplay, **"Unlock all mounts"** and **"Fly anywhere"** (both on by default and remembered
like your other settings; the mounts and riding they give are never saved to your character), give every character all the
mounts of its faction and class and let flying mounts fly anywhere. Summoning is WoW's 1.5 s cast with WoW's own rules and messages; riding works the Euro Truck Sim way (your avatar sits on the mount, which WE drive at
WoW's exact speeds - smooth, the view never tilts or bobs); a chase camera on desktop; other players see you riding. Online,
your realm's spellbook decides, and GM automatic accounts get two **Testing (GM)** buttons in the Mounts window. The step-by-step
test is in **"Mounts - iteration 7"** below ("Test mounts offline first").

**Iteration 6.1 (stage A1, automatic accounts)** adds **"Play online as <your Somnium nickname>"** to the Online tab: no
WoW account to create or type. The first press asks your server's account service for a WoW account bound to your
Somnium account (name `S` + 16 characters of your Somnium ID, a hidden random password kept only in your own Somnium
storage for this world), the next presses just log in. The server owner decides who may (an allow list) and who is a GM.
The old login form stays under "Use a different account". See "Automatic accounts" below and `ServerKit/README.md` section 8.

**Iteration 6 (stage O1, online)** adds an **Online mode**: switch the Play tab from Offline to **Online**, log in to your
own **AzerothCore 3.3.5a realm** (the `ServerKit` folder next to `Assets` sets one up on a DigitalOcean droplet or an Oracle
Cloud server, with Warden off), pick or create a character on the server and **enter the world: every creature, NPC, other
player and game object comes from the server at its real position** - names, levels, quest markers (! / ?), friendly /
hostile, NPC weapons, other players' looks, gear and mounts, doors - NPCs walk the paths the server sends, and your
movement goes to the server, so people with a normal 3.3.5a client see you walk. Stage O1 is login + world + movement only:
fighting, spells, loot, quests, gossip, vendors, flight masters and chat online come in stages O2 / O3 (until then they
answer "Not available online yet"). **Offline mode is unchanged.** See "Online (AzerothCore)" below.

**Iteration 5** adds the **WoW Map**: a live overhead map of the world around you, drawn in real time by a camera high above
your character (roofs and treetops above you are cut away so you see the ground, paths and building floor plans), with WoW's
own markers (your arrow, other players, quest givers ! / ?, flight masters, hostile creatures, your target), the zone name and
WoW coordinates. Open it from the **Somnium tablet** (a "WoW Map" app button, first in the tablet's home grid, next to
Somnium's own Map app, which is untouched), from the **palm menu** in VR (World Map button) or with **`N`** on desktop. A
Classic view shows the game's own minimap tiles instead. See "WoW Map" below.

**Iteration 4 (stage A, systems)** adds, on top of iteration 3: **flight paths and flight masters** where the game has
them (talk to one: WoW's flight map, pick a destination, ride the taxi gryphon / wind rider / hippogryph along the game's
own route), a **floating origin** (the world is quietly re-centred on you every kilometre, so nothing wobbles however far
you travel, and other players are still drawn where they really are), **VR spell casting from half-radial menus** (hold
grip, twist the hand, release; optional Immersive Mode: push the hand forward to fire), a **palm menu** (look at your left
palm for Character / Spellbook / Quest Log / Settings / Bags), a small **floating HUD** at the top left of your vision
(health, mana, target, party), **red view edges at 20 % health**, **helpful spells on friendly NPCs** (heals, buffs),
**far VR targeting** (point at anything you can see, up to 150 m by default) and **world details** (grass, flowers and
bushes from the game's ground-effect data, birds, rabbits, squirrels and other critters). Stage B (zones for levels 1-30)
comes in a later upload.

Iteration 3 is an **offline play test of all ten races**: make a character by choosing a **faction**, a **race** and a
**class** (your Somnium avatar stays the character - the race gives its racial abilities, its racial stats, its starting
zone and its voice), and play that race's real starting zone with creatures, NPCs and quests run locally on your PC.
New in this iteration: the eight starting zones, all nine classes (with the Warlock's imp, Shaman totems, Paladin seals),
particle effects and spell visuals, sound (spells, weapons, UI, NPC voices, footsteps, zone music and ambience), NPC
names above their heads, a Settings window, and on desktop your avatar swings, casts and shoots with your character.
No server yet: **monsters are per player** (see below). Iteration 1's sightseeing is still in the second tab.

## How to run it

1. Open `Assets/World of Warcraft Scene.unity` (Unity 6000.3.5f2, URP 17.3, Somnium SDK 3.1.8).
   The scene holds:
   - `Uploader` - WorldSDKConfig + WorldConfig (scripting on, assembly `SomniumWoW`, spawn **(0, 3000.9, 0)** facing +z in
     the black room, RespawnHeightY -1000). Iteration 4 moved the black room 3 km up (`WowSpace.LoginRoomOrigin` =
     (0, 3000, 0)) so it never overlaps the world near the floating origin; the spawn point and `Spawn Floor (fallback
     collider)` (now at y 2999.5, top at 3000) moved with it - keep them there. Your last upload's name, description and
     preview pictures are kept as you set them.
   - `WoW Game` - `WowGame` with the **7** shader carrier materials (`Resources/WowM2, WowSky, WowTerrain, WowWater, WowWmo`,
     `WowParticle` (iteration 3) and, new in iteration 4, `WowClutter` for the grass) and the install path (`Install Hint`).
     The views (`WowEntityViews`, `WowArmUi`, `WowGear`, `WowAudioDriver`, `WowSpellVfx`, and new in iteration 4: the
     floating origin `WowOrigin`, flights `WowFlight`, VR casting/targeting `WowVrInput`, grass and critters
     `WowWorldDetails`) are added to it at run time - nothing to set up.
   - `Wow Net` (iteration 2) - `Scene Networking` and 50 slots `Wow Net Player 0..49` (Fusion NetworkObject +
     NetworkBridgeData64 each). They carry every player's weapons, race, class, level and avatar combat motion to the
     others. Do not rename or delete them; the code finds them by name.
   - `Directional Light` (the zone lighting drives it), `Spawn Floor (fallback collider)`.
   - Lighting: **Fog ON, Linear**. Keep it on - with the project's automatic fog stripping, turning it off strips the
     linear-fog shader variants from the upload and the world loses all fog. `Tools > SomniumWoW > Check scene` checks the
     fog and the carriers (the editor also re-checks them on scene open and on every save, and adds any carrier in
     `Resources/` that is missing).
   - Unity has the scene open while we edit it: when it asks to reload the changed scene, choose **Reload**.
2. Upload with the SDK panel on `Uploader` as usual (in the scripting section, let the SDK check the `SomniumWoW` assembly).
3. In Somnium you stand in the black room facing the panel. Tabs: **Play** (default), **Sightseeing** (iteration 1) and
   **Settings** (new).

## How to play (Play tab)

- **Characters**: the list shows your saved characters (race and class icons, your nickname in the class colour,
  "Level N Race Class", the zone) with Play and Delete (asks first). Up to 10. Characters from iteration 2 load as Night Elves.
- **New character**: 1. **Faction** - Alliance or Horde (WoW's crests and texts). 2. **Race** - the five races of the
  faction with WoW's race icons and descriptions, the racial abilities with their spell icons, and the starting zone.
  3. **Class** - only the classes that race can be in 3.3.5a (from the game's CharBaseInfo.dbc; Death Knight starts at 55
  elsewhere) -> **Create** -> you go straight in. The race changes your racial abilities and stats, where you start and
  your character's voice; your look is always your own Somnium avatar.
- **Starting zones** (each with its own creatures, about a dozen NPCs, quest givers with "!" / "?", about 10-12 quests per
  character to level 5-6, cap 10):

  | Race | Zone | Map |
  |---|---|---|
  | Human | Northshire Valley | Eastern Kingdoms |
  | Dwarf, Gnome | Coldridge Valley | Eastern Kingdoms |
  | Night Elf | Shadowglen | Kalimdor |
  | Draenei | Ammen Vale | Outland (map 530, Azuremyst Isle) |
  | Orc, Troll | Valley of Trials | Kalimdor |
  | Undead | Deathknell (you wake in the crypt) | Eastern Kingdoms |
  | Tauren | Camp Narache | Kalimdor |
  | Blood Elf | Sunstrider Isle | Outland (map 530, Eversong Woods) |

- **Classes**: Warrior, Paladin (seals, Judgement, auras), Hunter, Rogue, Priest, Shaman (totems you can see, weapon
  imbues, Lightning Shield), Mage (Frost Armor, Arcane Missiles channel, conjured food and water), Warlock (the Imp as a
  pet: it follows you, casts Firebolt, gives Blood Pact, can die and be summoned again; Life Tap), Druid. Spells, names,
  icons and tooltips come from your game's Spell.dbc; new spells arrive by themselves on level-up.
- **Creatures** live in camps at levels 1-5, wander, attack you when you come close (hostile ones), fight back (neutral
  ones), and some cast spells now (Frostbolt, Lightning Bolt, Rend, Poison, Shadow Bolt ...). Each zone ends with a
  named leader. Stun, polymorph and fear work on them; they keep a threat list and can go for your pet or totems.
- **Flight paths** (new): flight masters ("<Flight Master>", level 55, our own names) stand at the game's taxi nodes
  (TaxiNodes.dbc) of your faction - e.g. Stormwind's gryphon platform. Talk to one: you discover the node ("New flight path
  discovered!") and WoW's flight map opens (desktop: the panel at the top left; VR: a panel in front of you, point with the
  ray). Pick a lit node you know: the whole fare is paid at once, you ride the game's own route (TaxiPath/TaxiPathNode) on
  the node's mount at 32 yards/s, the view stays level, streaming runs ahead of you, and you land at the destination.
  Your race's own nodes are known from the start (as AzerothCore gives them); every other node must be visited once.
  Settings > Gameplay > Fast travel = fade to black and land when the destination has loaded.
  Iteration 7: the taxi sits you on the mounts' seat (the same takeover as a ridden mount) and on desktop the ride's chase
  camera follows behind it; while you ride your own mount a flight master answers "You are already mounted! Dismount first.".
- **Friendly targets** (new): with a friendly NPC targeted, heals and buffs (Lesser Heal, Renew, Power Word: Fortitude,
  Mark of the Wild, blessings ...) land on it; harmful spells on it say "Invalid target".
- **Death**: "Release Spirit" brings you back at your zone's graveyard with half your health.
- **Saves**: level-ups, quest changes, loot, death, every 30 s (not during a fight), going back to the room and quitting.
  Characters are stored **per Somnium account** in this world's LocalStorage **on this PC** (two copies). The settings
  are saved the same way under `somniumwow_settings_v1_<account>` (+ `_2`), 2 s after the last change and when you leave.
- **Settings** (room tab **Settings**, or in the game: key `O` / the Settings button after the quest log on the desktop
  bar and on the VR arm panel; changes apply at once):
  - Display: nameplates, NPC names, floating combat text, Somnium player plates, quest markers, interface scale
    (desktop), arm panel scale (VR); new: Floating HUD (VR, on), Forearm action bar (VR, off), Low health red edges (on).
  - Effects: spell effects, world particles, effects quality (low / medium / high); new: Ground clutter, Clutter density
    (0.25-1.5), Birds and critters.
  - Audio: master, music, effects, ambience, footsteps and voice volumes; music and footsteps on/off; voice gender.
  - Gameplay: difficulty (easy / normal / hard: creature health x0.85 / 1 / 1.2, creature damage x0.7 / 1 / 1.3), auto
    loot, VR swing speed, haptics, desktop avatar combat animations; new: Immersive casting (VR, off), Fast travel
    (flights, off), Target reach (VR, 20-300 m, default 150).

### WoW Map (iteration 5)

How to open it:
- **Somnium tablet** (desktop and VR): open the tablet (Tab on desktop), the home grid has a **"WoW Map"** button (WoW's globe
  icon, first cell) beside Somnium's own Map app. The map fills the tablet's screen; everything it adds to the tablet is
  removed again when you leave the world.
- **VR palm menu**: turn your left palm to your face, press the **World Map** button (the micro button before the backpack); the map window opens above the palm like the other palm windows.
- **Desktop**: **`N`** or the **World Map** micro button next to the WoW micro buttons (the mouse cursor is free while it is open).

What it shows: the **Live** view is an orthographic camera straight above you, rendered at 1024 px, 12 times a second, only
while a map is open (nothing is rendered when all maps are closed). Fog is off for the map picture; anything more than 12 m
above your feet (30 / 60 m when zoomed out) is cut away, so roofs and treetops do not hide the ground. The **Classic** view
shows the game's own minimap tiles. Markers: you (WoW's minimap arrow, turned with your heading), other players (party blips
in their class colour), quest givers (yellow ! / ?), flight masters, hostile creatures (red dots), your pet, your target
(ringed). Top: zone name and WoW map coordinates (as WoW's world map shows them).

Buttons: zoom - / + (60, 100, 160, 250, 400, 600 m across), Rotate (off: north up; on: the map turns with you, your arrow points
up), View (Live / Classic), Recenter. Click the map to look at that spot; Recenter goes back to you.

### Controls

Desktop (all keys checked against the Somnium client's own bindings; none of them is used by the client):

| Key / mouse | Does |
|---|---|
| Left click on a creature or NPC | target it; click your target again = talk (NPC), loot (corpse) or, since iteration 8, attack (an enemy) |
| Talk to a flight master, click a node on the flight map (new) | fly there (hover a node: name + cost; the X closes the map) |
| `1`-`0`, `-`, `=` | the 12 action buttons (click them too); hover = tooltip |
| `V` | start/stop auto-attack (targets the nearest enemy first when you have no target) |
| `T` | target the nearest enemy (WoW's Tab; Tab opens the Somnium tablet) |
| `Z` | draw / sheathe your weapon |
| `P` / `L` / `C` / `B` | spellbook / quest log / character / backpack (the mouse cursor is free while a window is open) |
| `O` (new) | Settings window (also the Settings micro button, the computer icon after the quest log) |
| `N` (iteration 5) | WoW Map window (also the World Map micro button, and the "WoW Map" app on the Somnium tablet) |
| `J` (iteration 7, a Dreamport addition) | Mounts window (select, Mount / Dismount, right-click then an action button = put a mount on the bar); riding keys: see "Mounts - iteration 7" > Riding controls |
| right click on a spellbook icon, then an action button | put the spell on that button ("Clear a button" empties one) |
| `K` | back to the black room (also Settings > **Return to the room**, press it twice - stage S1) |
| `Enter` or `/` (online, stage S1) | WoW's chat edit box under the chat frame (bottom left): type, Enter sends; `/s /y /e /p /g /w Name /r /invite Name /help`, Up / Down = history, mouse wheel scrolls; your avatar does not walk while you type. Enter is also the Somnium client's own quick chat: online on desktop the WoW chat takes it, offline it is Somnium's again |
| `H` (stage S1, a Dreamport addition) | focus: no focus = your target becomes the focus; with a focus = target the focus; three quick presses = clear it |
| Click a party frame (stage S1) | target that party member (e.g. to heal); right-click = Set Focus; the focus frame: click = target the focus, right-click = Clear Focus |
| Right-click the target frame (stage S1) | WoW's unit menu: Invite (another player), Leave party (a party member / yourself), Cancel |
| `O` > **HUD** (stage S1) | move and resize the player / target / party / focus frames (Reset HUD puts them back) |
| WASD, mouse | Somnium's own movement and camera |

On desktop your Somnium avatar now **swings, stabs, punches, draws a bow, shoots and casts** (raise, channel, release) when
your character does, blended over the client's own animation, and other players see it (Settings > Gameplay > Avatar
combat animations turns it off). In VR your avatar is moved by your own hands, as before.

VR:

| Gesture | Does |
|---|---|
| **Hold grip** (hand away from a sheath) (new) | a half-radial menu in WoW action-bar art opens at that hand: right hand = action buttons 1-6, left hand = 7-12 |
| **Twist** the hand while holding (new) | moves the highlight across the slots (about +-75 degrees; a haptic tick per slot); the highlighted slot shows name, cost, cast time, cooldown or why it cannot be used |
| **Release** grip (new) | casts the highlighted spell (or does that action); releasing in the middle, on an empty slot or after pulling the hand 12 cm back cancels |
| Immersive casting (Settings > Gameplay, off by default) (new; stage S1: start first, then push) | a spell with a cast time **starts at once**; at its end the controller vibrates and the spell waits in your hand (a breathing glow in the spell's school colour): **push that hand forward** (faster than 1.5 m/s) to fire it (online: at the target it was started on; offline: what you aim at, else your target); it drops after 30 s. Instants cast at once. **+10 % damage** on every spell released this way, buff "Immersive Mode" (a Dreamport house rule; online needs the realm's Dreamport hold module) |
| Point with the right hand + trigger, far away (new; stage S1: along the Somnium laser) | the target follows the Somnium laser (Settings > Gameplay > Target reach, 150 m by default), stopped by terrain; the unit you point at glows (nameplate + a soft glow, a light tick beyond 5 m); trigger = target it. Nothing of ours is drawn on the laser |
| Short press **X** on the left controller (stage S1, a Dreamport addition) | focus: no focus = your target becomes the focus; with a focus = target the focus; three quick presses = clear it. Holding X is still Somnium's inspector |
| Palm menu **Chat** / **Party** buttons (stage S1) | Chat = the chat window above the palm (Say / Party / Yell / Emote / Reply, the Type row opens the headset keyboard); Party (a Dreamport addition) = Invite <target> / Leave party |
| Laser + trigger on a party frame of the floating HUD (stage S1) | target that party member (the frame lights up gold); the same on the focus frame targets the focus |
| Turn your **left palm** towards your face (new) | Character / Spellbook / Quest Log / Settings / Bags appear above the palm (poke them or ray + trigger); a window opens in the world above it |
| Palm menu **World Map** button (iteration 5) | the WoW Map window above the palm; the "WoW Map" app on the Somnium tablet shows the same map |
| Palm menu **Mounts** button (iteration 7, a Dreamport addition) | the Mounts window above the palm; riding: left stick walk / run / steer, your own right-stick turn, right A jump / climb (see "Mounts - iteration 7") |
| Floating HUD (new) | small, top left of your vision: player (health, mana, buffs), target with its cast bar, pet and totems, your cast bar, up to 4 party members, error lines (Settings > Display > Floating HUD) |
| Low health (new) | at 20 % health and below the edges of your view turn red and pulse |
| Look at your left forearm (off by default now) | Settings > Display > Forearm action bar brings back the iteration-3 arm panel (frames, 12 action buttons, windows standing up from it like a lid) |
| Point with the right hand + trigger, or touch a button with your right hand | press a button, a checkbox or a slider on the arm panel or a window |
| In the spellbook: tap a spell's NAME, then an action button | put the spell on the bar (tapping the icon casts it) |
| Point at a creature/NPC + trigger | target it; trigger on your target again = talk / loot |
| Squeeze the grip with either hand behind your back (two-hander, staff, bow, gun, shield) or near a weapon on your hips (one-hander, off-hand, dagger, wand) | draw it - it stays in your hand; squeeze again the same way to put it back |
| Bow in one hand: the other hand squeezes behind your back, then brings the arrow to the string and pulls | immersive bow - your shots (buttons 1-6 of that hand's half of the bar) show beside the bow; let go on the one you want |
| Swing a drawn weapon into a creature (tip faster than the Settings' swing speed, default 1.6 m/s) | start auto-attack on it (a haptic pulse; the real damage runs on WoW's swing timer) |
| Settings (palm menu) > **Return to the room**, press twice (stage S1; the wrist button is gone) | back to the black room |

### Monsters are per player in Offline mode

In **Offline** mode monsters, NPCs, quests and loot run **locally on each player's PC** (an offline simulation of the WotLK rules). Two
players in the world see their **own** copies of the creatures: you cannot fight the same monster together, and a
creature you kill is still alive for the other player. What IS shared: every player's avatar shows their weapons and
combat motions and a nameplate "Name - Level N Race Class" in the class colour, over the world's network slots. Other
players are drawn where they really are however far you travel (each player has their own floating origin; the network
record carries it), hidden on other maps, in the room or beyond 1.5 km; flying players ride their mount. **Online** mode
(iteration 6, next section) puts everyone on one AzerothCore realm: the same creatures and NPCs for all.

Not available offline (the spell shows as unusable with the reason): Hunter pets (Tame / Call / Feed / Revive Pet),
tracking (Find Treasure, Track Beasts), Pick Lock, Pick Pocket, resurrection, teleports. Reagents (Soul Shard) and totem
tools are not checked. Pet commands (Attack / Follow / Stay) are greyed out: the imp acts on its own (it attacks what you
attack and follows you otherwise). No vendors, no rested XP; speed buffs do not move your avatar faster.

## Iteration 11 - the complete game (2026-10)

What you asked for on 2026-09-27/28 and 2026-10-03, in one upload. Online means your own AzerothCore realm; offline means our own
zones on your PC. Every test below says what to do, what you should see, and the Player.log line that proves it. Values that are
our own choice (not WoW's) are named "ours" in Player.log once.

**Before you test (server):** no server change is needed for this upload. Two things need your OK first (nothing was changed):
- Guild creation: AzerothCore needs 9 other players to sign a guild charter (MinPetitionSigns, default 9). To allow a small group,
  add `AC_MIN_PETITION_SIGNS: "1"` under `services.ac-worldserver.environment` in the ServerKit's `docker-compose.override.yml`,
  then restart the worldserver. Only if you want it.
- Battlegrounds and the Dungeon Finder need full groups. With a GM account you can type `.debug bg` (battlegrounds start with fewer
  players) and `.debug lfg` (Dungeon Finder tests with fewer players) in chat. These are toggles in your GM session, not config edits.
  The Auction House only has listings once players post them.

**Start of the session (desktop, online or offline).** Player.log shows:
`UI: 8999 strings read from the install's Interface\FrameXML\GlobalStrings.lua`,
`UI keys: 78 desktop chords from N owners; no collision with the client's keys`,
`UI: N registered windows built for the desktop layout; 0 failed` and `UI: game UI for <name> ... windows registered (...)`.

### Fixes after the first iteration-11 test (2026-10-03, version 11.1)
- **Forms and polymorph hide your avatar for everyone.** The forms hid the client's mirror copy of the avatar (drawn only by mirrors
  and the tablet camera); now every visual copy is hidden and the main copy is the one we follow (also for the combat animation and
  immersive armour). Log: `Gear: body of the local player: humanoid Animator '...' (MainRoot ...)` / `NonXRMainRoot`, and
  `Forms: hiding 2 visual copies of the avatar ...` while in a form.
- **Elite / rare mobs:** the target frame shows WoW's gold elite dragon, the silver rare one and the rare-elite one (creature rank from
  the server); a world boss or an enemy 10+ levels above you shows the skull instead of a level.
- **Cursors:** WoW's own cursor per NPC and object (the server's IconName: Buy for vendors, Directions for guards, Trainer, Taxi ...),
  quest cursors over quest givers, Mine / GatherHerbs / PickLock over nodes (greyed "Unable" while your skill is too low), LootAll
  over a lootable corpse, Skin over a skinnable one, Mail over a mailbox, and the sell cursor over bag items while a vendor is open.
- **Spell ranks:** a new rank from the trainer replaces the old one on every action button (and the server is told).
- **Sounds:** precast, channel, aura, ground-area and missile sounds now last exactly as long as the spell (loop files loop, others
  play once and stop with the spell); repeated casts no longer stack copies.
- **Target / focus portraits:** the selected unit's own face, rendered live (Somnium players: their Somnium profile picture; your own
  player frame: your picture, else the class icon). Log: `UI portrait: first 3D portrait rendered ...`.
- **Ships, zeppelins, elevators, the tram:** carried the way the Somnium client carries players in its own elevators, as the Euro Truck
  Dreamport does it (the ship's movement written onto the avatar before the client moves it; position only, desktop and VR). Log every
  30 s while riding: `Transports: riding ... carry lost L of C` (L should stay 0).
- **Desktop walks by default** (VR unchanged): you walk at WoW's walk speed with the walk animation (WoW clients see you walk); hold
  Shift to run at WoW's run speed. Log: `Speed: desktop walks by default ...`, `Online: walk mode` / `run mode`.

### Additions and fixes after 11.1 (2026-10-03, version 11.2)
Both Somnium players need this upload for the shared origin, the armour placement and the full armour to show on each other.
- **Friends' Somnium avatars and voice nearby.** Nearby Dreamport players now share one floating origin (the Somnium player with the
  lowest id within 400 m leads; ours), so the client's own avatar culling and positional voice see the true distances. Log:
  `Origin: sharing the origin of Somnium player N (cell ...)`.
- **Armour Adjustment** (Settings > Armour): pick Helm / Left shoulder / Right shoulder; "Show this piece"; Left / right, Down / up,
  Back / forward (+-30 cm) and Size (x0.5-2); "Mirror the shoulders" (on: one shoulder moves the other the same way, mirrored; show /
  hide stays per piece); Reset this piece; Save. The placement belongs to the slot, so a new helm sits where you put the old one.
  Others see your placement. Log on Save: `UI: Armour Adjustment saved - helm (x, y, z) m xS, ...`.
- **Helm in photos and mirrors** (Settings > Armour, on): the helm hidden from your own eyes (VR, desktop first person) still shows in
  the Somnium camera's pictures and in mirrors (the client's mirror layer 8). Turn it off for pictures without it. Log: `Armour: <you>
  helm hidden from your own eyes, kept for the Somnium camera and mirrors (layer 8).`
- **Immersive armour: Full (experimental)** (Settings > Avatar): chest, legs, gloves, boots, belt, bracers, tabard, cloak and robe
  on a WoW body shell posed from your avatar, plus the helm and shoulders. Log: `Armour: full shell for ... measured the avatar: N
  meshes ... torso remap: ...` (when it says "not readable", a fallback thickness is used). Look down in VR and raise your arms.
- **Minimap (desktop):** WoW's minimap at the top right (FrameXML MinimapCluster): zone name in its PvP colour, the install's minimap
  tiles, north up with the turning arrow, + / - and the mouse wheel over the map (6 steps, remembered), "!" / "?" quest givers, flight
  masters, unit dots, your group and your corpse. Settings > HUD > "Show minimap". Windows now stop at its left edge. Log: `UI minimap:
  MinimapCluster 192 x 192 TOPRIGHT ...`. While you are a ghost the corpse arrow is now at the top centre.
- **VR target laser:** a 50 % white laser while far targeting, warmer while on a unit.
- **Immersive strike:** Settings rows for the ring's icon size, spread, distance (0.25-1.20 m) and height (VR).
  After a strike, nothing on the wheel fires for 0.3 s (the swing cooldown), so a full swing no longer also hits the icon opposite.
  The ring now floats with you: it stays at the same distance and height in front of you while you walk, no longer turns toward the
  target, turns with you at once on a thumbstick turn, follows a head or body turn once you face more than 25 degrees away (a glance
  aside does not move it), and always appears straight in front of you.
  Log: `VR strike: right hand passed <spell> 0.12 s after a strike - ignored (swing cooldown 0.30 s).`
- **Other WoW players jump** with WoW's jump start / in the air / landing animations.
- **Sheathed weapons** on WoW-model players and on your avatar: staffs and two-handers diagonal over the left shoulder, ranged up-right.
- **Floors:** WoW players no longer sink through building floors (Stormwind); the ground under them is found on the building first.
- **Quest and gossip windows:** the see-through button socket at the bottom left is filled.
- **Death aura:** a player you saw as a ghost no longer keeps the ghost sound/aura after being resurrected.

### Swimming animations (2026-10-26, version 13.5)
- **Desktop:** your avatar plays WoW's own swim animations from your race's model (42 Swim forward, 45 backwards, 43 / 44 sideways,
  41 SwimIdle treading water): the body lies in the water by WoW's lean with the shoulders at the waterline, legs kicking, arms
  stroking. Plays whatever the "Avatar combat style" (needs "Avatar combat animations" on).
- **VR:** your tracked head and hands stay yours; your legs kick with WoW's swim legs under your upright body.
- Others see it (the animation id travels in your record as before): desktop swimmers lie and stroke, VR swimmers kick.
  GearPoser.ApplySwim, GearWowAnim.SwimLoopNow.

### Swimming, water, meeting stones, strike ring (2026-10-26, version 13.4)
- **Swimming (Somnium avatar, on foot):** water deeper than 0.75 x your height (AzerothCore's rule) floats you up with your head and
  shoulders out; you swim at WoW's swim speed and stay at the depth you swim to (no buoyancy, as in WoW); you walk out where the
  floor comes up. Up / down: desktop Space / Left Ctrl, or hold the right mouse and press W to swim where the camera looks; VR: hold
  A to rise, push the left stick forward and look up / down to swim that way. Lakes, rivers, the sea and city canals / water in
  buildings. Online, WoW players see you swim (swim flags + pitch). Game/Play/WowSwim.cs, World/Unity/WowLiquidSurface.cs.
- **Breath / Fatigue bars** (WoW's mirror timers, top centre on desktop, on the arm and VR HUD): the server's breath under water
  and fatigue far out at sea - drowning is the server's, so watch the blue bar. Offline has none.
- **Water** reflects the zone's sky at glancing angles, glints in the sun and is more opaque from a distance. Settings > Effects
  "Water visibility" (0.5x - 2.5x, default 1.25x).
- **Meeting stones:** target a party member, use the stone: the summoning portal appears, ONE more party member clicks it, then
  the target gets the summon popup. The portal (gameobject type 18) could not be clicked before. Without a party member target,
  out of party or under the stone's level, the client says why (WoW's own texts).
- **Immersive strike ring:** no longer hidden for hostile NPCs that also have gossip, a vendor or quests (they were treated as
  friendly); clicking them again attacks. Their level now shows in its difficulty colour.

### Photon Fusion 2.1.3 (2026-10-21, version 13.3)
- Somnium now runs Photon Fusion 2.1.3 (was 2.0.12); this world was moved with Somnium's migration tool V1.0 (unattended, task Full:
  exit 0, 0 compile errors, 15 NetworkTransforms checked, 0 missing scripts; backups in MigrationBackups/), as the other Dreamports.
- Our code: SceneNetworking - NetworkObjectFlags.V2, RequestStateAuthority() checked, OnReliableDataReceived(ReadOnlySpan<byte>)
  (migration guide 6, as Skyrim's).
- Community Modules: only its Plugins folder is kept (Somnium's networking plugin, now 4.0) - the rest the tool added (examples,
  the CommunityModules assembly) is unused here and was moved out to D:\Dreamport\WoW\Fusion21_Removed_2026-10-21, as in Skyrim.
- Before uploading: accept the Somnium SDK update if Unity offers one; test with two clients (other players' gear, casts, armour).

### Outland flights, phantom units (2026-10-14, version 13.2)
- **Outland flights:** our flight network kept a path only when it ran both ways or was not used by a quest spell; the Dark Portal's
  paths (to Honor Hold 564 - also used by a quest flight - and to Temple of Telhamat 582, one-way, both cost 0 in TaxiPath.dbc) were
  dropped, so the map showed nothing reachable there (harness OutlandTaxi). Now a two-way path always counts and a one-way path counts
  unless a quest spell sends it; the realm still decides each flight.
- **Phantom units:** WoW's invisible triggers and stalkers ("Watcher" ..., Creature\InvisibleStalker - a model with nothing to draw, or
  UNIT_FLAG_NOT_SELECTABLE) had a name and a click box that covered flight masters. They now have neither; clicks go through.

### VR ground spells and dynamite, macros (2026-10-12, version 13.1)
- **VR ground spells / items (dynamite):** the trigger never placed the green circle in VR - the "pointer on our UI" flag the circle
  checked is also on for the whole aim (it includes "a cursor mode owns the world clicks", the circle itself) and for any open panel
  the laser crosses (the bags). It now refuses only a press on one of our buttons.
- **Throwing:** an item's spell aimed at a place (dynamite: WoW's ReadyThrown 108 / AttackThrown 107, harness ThrowAnim) counts as
  yours, so your desktop avatar throws it and others see the release; its missile flies to the spot.
- **Macros (Settings > Macros):** 18 macro buttons (WoW's MacroFrame): New, a name, the text (255 letters; VR: tap a box for the
  headset keyboard, "New line" for a line break), Delete, "Put on bar" (then click an action button, or in VR release a spell menu on
  one). A macro's button runs its lines: /cast Name (Rank n) ([@player] = on yourself), /use Item, /target Name, /cleartarget,
  /startattack, /stopattack, /stopcasting, any chat command (/say, emotes ...); # lines are skipped; other lines are said. Its icon is
  its first /cast spell's or /use item's. These commands also work typed in the chat. Saved with your settings (Defaults keeps them);
  the realm stores only the button. No [conditions] are tested (the first choice is used).

### City quest map, hide weapons, others' level-ups (2026-10-12, version 13.0)
- **Quest map in cities:** a quest now shows on a zone's map when the realm tagged its area with that map OR its area lies inside
  the map's bounds (POI points are world coordinates) - Stormwind's map showed nothing when the realm had tagged its quests with
  Elwynn. Ours; WoW draws the tagged ones only.
- **Hide weapons (Settings > Avatar):** "Show main hand", "Show off hand", "Show ranged weapon". A hidden weapon is not drawn on your
  avatar, cannot be drawn into a hand, and others do not see it either (your record sends it empty).
- **Other players' level-ups:** when another Somnium player's level goes up (their record carries it), WoW's level-up effect plays
  on their avatar. Log: `Gear: <player> reached level N - the level-up effect on their avatar`.

### Login area back to the black room (2026-10-11, version 12.9)
- The WotLK login scene (12.7) and its sky (12.8) are removed: the sky hid the menu. The login area is the black room again, as
  before 12.7. A better lobby comes later.

### Login area: WotLK's sky only (2026-10-11, version 12.8)
- The whole login scene looked bad from inside: it is a small diorama of flat painted cards 2-9 yd from WoW's camera. Only its sky
  is kept now - the sky bowl and the cloud layers (Login_SkyBowlA, Login_CloudsA05, Login_Clouds_Unholy01, Icecrown_CloudsA06 / A07),
  ten times larger about your eyes so it looks the same from where you stand but far away; the black void stays behind it for any
  gap. The invisible floor and walls are your platform. (Models can now be built with only the batches of chosen textures.)

### The WotLK login scene (2026-10-11, version 12.7)
- **Login area:** the black room is replaced by WoW's own WotLK login screen, read from your install
  (Interface\Glues\Models\UI_MainMenu_Northrend\UI_MainMenu_Northrend.m2): the Northrend mountains, Icecrown and the frost wyrm
  flying in on its 67 s loop, with WotLK_main_title playing as before. You stand where WoW's login camera is (your eyes at its
  point, looking where it looks), on the room's invisible floor; the panel floats in front as before. If the model is missing,
  the black room stays. Log: `Room: WoW's login scene ... built round the spawn`.

### VR bag slots, bag portraits (2026-10-11, version 12.6)
- **VR bag slots:** WoW's bag bar (the backpack, the four bag slots and the keyring) sits under your open bags in VR, as on the
  desktop's main menu bar: drag a bag out of its slot into your bags to unequip it, drop a bag on a slot to equip it, tap a slot to
  open / close that bag. The bags stack above it.
- **Bag portraits:** the round bag icon at the top left of each open bag was drawn invisible (the same clear-picture bug as the
  spellbook tabs); the backpack now shows WoW's backpack icon there too.

### Trainer animation, profession items (2026-10-11, version 12.5)
- **Trainers:** buying a spell made the trainer loop its casting animation. The server's spell visual (kit 179 = animation 53
  SpellCastDirected) now plays once; only real idle emotes loop.
- **Professions:** the recipe icon shows the item it makes (WoW's GetTradeSkillIcon) and its tooltip is that item's whole tooltip
  (GameTooltip:SetTradeSkillItem), also on each recipe line - before, both were the spell's. The window rebuilds when an item's
  details arrive from the server, so names, icons and tooltips fill in.

### Held spells keep their effects and sound (2026-10-11, version 12.4)
- **Immersive casting:** while a finished spell waits in your hand for the push, its precast effects and sound keep looping
  (before, they stopped about a second after the cast time). A sound file that only plays once starts again and loops. They end
  when you release it, cancel it or put it away. Other players see and hear the same on your avatar: your held state already
  travels in your cast record, and their side now keeps the precast going from it (also when the server showed them the cast).
  Log: `SpellVfx: spell N held - its precast effects and sound loop until it is released`, `Gear: <player> holds <spell> ...`.

### Easier weapon grabs and the immersive bow (2026-10-10, version 12.3)
- **Easier grabs (VR):** either hand takes any weapon (no more crossing your hands for a dagger on the other hip); a weapon on the
  back (two-hander, staff, bow, gun, shield) comes from anywhere behind your head's plane down to just under the chest; a weapon on
  the hips within 0.28 m of its handle (was 0.2). Wands now hang on the right hip and can be taken. A shield stays the left hand's.
  Putting a weapon back works the same way. Log: `Gear: VR ... hand drew the ... (..., behind the back)`.
- **Immersive bow (Settings > Controls, VR, on):** with a bow in one hand, the other hand squeezes behind your back to take an
  arrow (WoW's ArrowFlight model) and holds it; near the string it snaps onto the string's middle. Up or down once (7 cm) before
  pulling switches to the bar above / below. Pulling back shows the six buttons of the string hand's half of the bar as a column
  beside the bow: slot 1 after 6 cm, then one slot per 8 cm, a pulse for each, a fifth of a slot of "buffer" at each edge; the column
  slides down so the chosen button stays in the green frame. Letting go on a button casts it (items and macros too; an Auto Shot that
  is already running stays on) and the arrow flies; letting go before slot 1 puts the arrow away. The bow hand never opens a spell
  menu while holding the bow; put the bow back behind your back as you took it.
- **Bowstrings:** WoW draws a bow's string itself between two bones at the limb tips (62 of the 69 bow models in the files have
  them; probe BowString), so our bows had none. Your own bow now has its string, which follows the arrow, and its limbs bend with
  WoW's own BowPull animation as you draw and spring back with BowRelease. Other players do not see your string or arrow yet.

### Quest tracker beside the right bars, quest text size, sharp VR interface (2026-10-10, version 12.2)
- **Desktop quest tracker:** it now moves left of the Right Bar / Right Bar 2 and ends above the bottom bars, as WoW places its
  WatchFrame (UIParent.lua:1937 / :1941: -CONTAINER_OFFSET_X, CONTAINER_OFFSET_Y) - it no longer covers the right action buttons.
- **Quest text size (VR):** Settings > Display, 0.75x .. 2x - the quest window, the quest log and the objectives list grow with it.
- **Sharp VR interface (Settings > Effects, VR, on):** the client's URP renderers draw one layer, "Overlay", after post-processing -
  outside DLSS (its OverlayLayerRenderFeature). Our VR panels, menus, nameplates, chat bubbles and combat texts move onto it, with
  their sorting under the client's own laser and HUD. Checked every second: only in VR, only when every renderer draws that layer
  (itself or through the client's pass) and the head camera renders it; otherwise everything stays as before. The desktop interface
  is a screen overlay, drawn after DLSS already. The client keeps its own tablet off that layer in XR because things drawn after
  post-processing can shake against the world - turn the option off if our panels do. Log: `UI: the VR interface is drawn after DLSS`.

### Gear Select bar and bags beside the Character window (2026-10-10, version 12.1)
- **Gear Select has its own menu:** six boxed buttons in a row (WoW's tooltip box with a gold border, the action's icon on top, its
  key under a gold line) instead of the half ring; the chosen box is larger and brighter, the row's name is above, the spell's
  details box is below, the row ladder stands at the outer end and the bars above / below show as faint rows. Switching the style
  in Settings rebuilds the menu the next time you grip. Log: `VR input: ... spell menu rebuilt as the Gear Select bar`.
- **Bags beside the Character window in VR:** in 12.0 the bags still stacked in the arm window's bottom-right corner (over the
  Character window); they now stack in a column to the right of the window, 12 units out, and the laser reaches them there.
- **Spellbook tabs show their icons:** the skill-line tabs down the right (General, the class trees) loaded their icons but drew them
  fully transparent (the picture was made without a texture, so its colour was clear); the same for the shaman totem bar's call /
  recall buttons.

### Fixes after the 11.9 test (2026-10-10, version 12.0)
- **Ships and zeppelins:** if you drop through a deck (more than 1.5 m under your last spot on it, within 12 m) while still counted on
  board, you are put back on that spot (at most 3 times in 10 s). The deck carry itself is the elevator method it already used. Log:
  `Transports: fell through the deck of ... - put back on it`.
- **Auction House money boxes:** each box showed one digit (the text field kept 10 units of padding on each side whatever its width);
  the padding now follows the width, the boxes are right-aligned and have WoW's input border (Common-Input-Border).
- **VR buttons inside buttons (the mailbox):** the VR laser took the first button under it, so a big one built earlier (a row, a
  window blocker) beat the small one inside it; the smallest button under the laser now wins - the letter's items, money and Take
  buttons answer.
- **Ground spells in VR (Distract, Rain of Fire, Blizzard ...):** pointing at the floor counted as "the client's laser is on a Somnium
  interactable", so the trigger was left to the client; now only holding something or a Somnium panel stops the placement.
- **Bags with the Character window in VR:** opening one closed the other; the bags now stay open beside the window (right of it) so
  gear drags between them.
- **Your avatar in the Character window:** drawn in its armour in the middle (WoW's CharacterModelFrame), live.
- **Gear Select (Settings > Controls "Spell menu style", VR):** grip and MOVE the hand left / right to choose a spell, up / down for
  another bar, release to cast; sticky edges and a dead zone keep a near miss on the spell. "Twist wrist" stays the default.
- **Outland skies:** the skybox models the light names (LightParams -> LightSkybox: Blade's Edge, Bone Wastes ...) are drawn round
  the camera over the sky gradient. Log: `Lighting: skybox ...`.
- **Ground spell effects that stayed (Rain of Fire):** a ground patch is let go once its dynamic object outlives its spell's duration
  by 2 s.

### Fix after the 11.7 test (2026-10-04, version 11.8)
- **Falling through the map after a hearthstone:** a teleport inside the map to a place that had not streamed in yet put you at the
  server's height over nothing (Player.log: "ground the given z (no collider, no terrain height)"); the 2 s hold ran out and the fall
  guard's safe spot was that same empty place. Now the avatar is held at the target (gravity and collision off, as the ship arrival)
  until a floor collider is under it, or the area has loaded with its terrain - also when only the terrain is in and a city's floors
  are still streaming - then set down on it (at most 30 s). Log: `Play: the teleport target has not loaded yet - the avatar is held
  there ...`, `Play: teleport hold ended - the ground loaded after N s: ...`.
- **Other players saw your Full armour "huge and blobby" after you walked away and back:** past 15 m (or past the nearest 8 avatars)
  the Somnium client swaps an avatar for its culling stand-in - a generic model with its own skeleton and unreadable meshes
  (AvatarsCullingSystem / AvatarHoldersHandler.SetMainHolderCulling). The armour followed the stand-in and measured it (Player.log:
  "humanoid Animator 'Root' ...", "measured the avatar: 0 meshes, 0 vertices, 2 not readable, 0 of 14 segments"), so it fell back to
  WoW's own bulky body. Now the stand-in is recognised (its holder) and never measured: it wears the avatar's last good fit (or no
  shell until the avatar itself was measured), no helm face card on it; a measurement that reads nothing never replaces a good one;
  good measurements are kept per avatar, so when the avatar comes back into range its fit is back at once. The followed avatar copy
  switches back to the real one as soon as the client shows it again. Renderers switching on / off no longer trigger a new measurement.
  Log: `Armour: full shell for ... is the client's stand-in ... - not measured; drawn with the measurement of ...`,
  `Armour: full shell for ...: avatar '...' measured before - that measurement used again (no wait)`,
  `Gear: body of ...: humanoid Animator '...' (..., the client's culling / loading stand-in - looking for the avatar itself ...)`.
- **Other players floating above their griffon on a flight:** a flight sent nothing about the griffon (only ridden mounts did), so
  other players placed it by a guess - its saddle under the rider's hips, measured in the griffon's standing pose. Gryphon.m2's saddle is
  1.79 - 2.28 m up when it stands but -0.06 - 0.42 m in its Fly animation (harness probe TaxiSeat), so the rider hung about 1.8 m above
  the flying griffon. Now a flight sends what a ridden mount sends (ride kind "taxi", the Fly animation, and where the griffon is drawn
  relative to the rider), so everyone draws it exactly where the rider's own client does; and the guess (players on an older build)
  measures the saddle again in each animation, eased. Log: `Gear: our ride record - kind 3 ...` (the rider), `Gear: <name> mounted
  display ... (taxi), gait 135 ..., anchor relative (...)` (the others).
- **Other players saw no face (and a wrong-sized helm) with "Face in helm":** the face card and the helm took their size from the
  head's height above the avatar's Animator object - for another player that is the Somnium client's own Animator on its avatar root,
  not at the feet, so the size swung (Player.log, the same face: "s 3.00", later "s 0.54"); the face snapshot's camera then sat inside
  the head (an empty card) and the helm came out about half size. Both now take the head height from the avatar's own skeleton
  (its T-pose), and the snapshot camera stands clear of a face bigger than expected. Checked in the render lab with a remote-style
  avatar (no mirror copy, the drawing offset, Hide avatar part): the face is drawn like your own. Log: `Armour: face card for ...:
  ... s 1.02 from the T-pose head height 1.64 m ...`.
- **Armour popping off and on near other players:** the armour shell's culling box hung on that same Animator object, which for
  another player can stand far from the avatar (Player.log: "avatar model (0, 0, 0)") - Unity culled the shell whenever that spot was
  off screen. It now hangs on the avatar's hips. And when the client swaps a player between its stand-in and the avatar (around 15 m),
  the armour is no longer dropped for up to 2 s: the new copy is looked for every 0.2 s and the armour stays in place meanwhile.
- **Eating / drinking cancelled at once in VR:** in VR the client's sitting pose moved the avatar under the head, which went out as a
  move, and the realm stands a seated player up on any move (AzerothCore MovementHandler.cpp:503-504) - the food's aura went with it.
  In VR the sitting / eating / drinking pose is gone (your own body sits), and while seated a VR sway of less than 0.5 m sends no move
  (walking off still stands you up). Log: `Online: seated (stand state 1) - the avatar sways ... no move sent`.
- **Immersive eating (Settings > ActionBars, VR, off by default):** sit down in real life to eat and drink. Standing, food and drink
  are refused before anything is used ("Sit down (in real life) to eat or drink."); standing up while eating stops it, as standing
  up does in WoW. Sitting = your headset below 82% of your standing height (your avatar's height, or the tallest you stood). Log:
  `Gear: immersive eating on (VR) - standing height ... sitting below ...`, `Gear: immersive eating - the wearer stood up in real life ...`.
- **Accepting a quest in VR without scrolling:** the quest window's reward and quest-title buttons inside the scrolled text lay
  invisible (clipped) over the Accept button until the text was scrolled to its end, and the VR ray hit them first. The VR ray and poke
  now skip anything clipped away by its scroll area (desktop clicks already did), so Accept works at once - in every scrolled window.
- **Music and ambience inside buildings (inns, taverns, halls):** buildings carry their own sounds in WMOAreaTable (3053 rows with
  their own music), which only played as ambience where the zone had none, never as music. Now, as the WoW client does, the building
  under your feet decides first (its own music / ambience / intro, else its linked area's, else the zone's), held 1 s so a doorway
  does not flip it: Goldshire's Lion's Pride Inn plays Zone-TavernAlliance (RA_HumanTavern*.mp3) with Tavern.wav instead of Elwynn's
  forest music, Brill's Gallows' End the Horde tavern music, Booty Bay's Salty Sailor the pirate tavern music (harness InnMusic 11/11).
  Log: `Audio: zone '...' (area ...): music 156 'Zone-TavernAlliance' ... - the building's sound row`.
- **Finer armour fitting - top and bottom edges (Settings > Armour, chest to feet):** "Top forward / Top up" and "Bottom forward /
  Bottom up" (+-15 cm) move a piece's top and bottom edge on their own; the piece leans or stretches smoothly in between - straighten a
  bent piece, pull a robe's hem in under you, set where a piece starts and ends. Everyone sees it (1 cm steps; the network record is now
  full, 64 of 64). The page shows only the rows that apply to the selected piece (Mirror shoulders / Helm in photos for the helm and
  shoulders, the tapers from the chest on, the edges for chest to feet).
- **Robes no longer stick out behind (defaults):** the fit that shapes the shell's hips to your avatar also pushed a robe's whole skirt
  back and deeper, down to the hem (Player.log: "centre moved -10..-6 cm", "depth x0.92..1.50"). Below the pelvis it now fades out over
  15 cm, so the skirt hangs from the legs in WoW's own shape.
- **Boots no longer huge (defaults):** your avatar's shoe against WoW's bare foot always measured at the limit ("L foot 3.00/3.00"), so
  the boots came out at 1.65x the leg's thickness and 1.6x WoW's length. Boots now follow the shin (0.9x its thickness, at most 1.4x
  WoW's foot) at WoW's own length - about the size you set with Size near its minimum.
- **Valve Index grip needs a squeeze:** on Index controllers the grip (spell menus, weapons, the palm guard) read the capacitive touch,
  so the slightest touch counted. Now an Index uses its squeeze FORCE when the controller reports one (held at 25%), else a near-full
  grip (90%) without the touch button. Log once per hand: `Gear: '...' is a Valve Index - grip from ...; features: ...`.
- **VR spell menus and the Somnium tablet:** while the tablet is open or held (+0.5 s), gripping opens no spell menu on either hand, so
  the tablet can be spawned, grabbed and moved; the palm menu also stays closed while the left grip is held or a hand is at the tablet.
  New Settings > Controls "Spell menu twist sensitivity" (VR): higher = less wrist twist to move around a spell menu (50% = as before).
- **Desktop: sitting in the saddle on mounts and griffons:** the rider's seat was fixed at one point of the mount while its gait bobbed
  underneath (Gryphon.m2's saddle moves -0.06 - 0.42 m in Fly; the flight's fixed seat was one sample near the top), so you hung over
  it. On desktop you now sit on the live saddle every frame and move with the mount; the chase camera stays on the steady seat (no
  bob). VR keeps the steady seat (comfort). If you still sit high or low, Settings > Avatar "Mount seat height" adjusts it.
- **Huge legs in full body tracking:** the armour measured the avatar while the trackers pulled the leg's bones away from its mesh
  (Player.log: "L shin 3.00/3.00" - the limit; 0.76/1.10 without trackers), and kept it. Now an arm or leg segment at the limit is a bad
  reading: it takes its parent's (thigh / upper arm) or WoW's own size, is never kept as good, and the avatar is measured again every
  5 s (up to 12 times) until a clean reading. Log: `Armour: full shell for ...: that measurement is doubtful (... at the limit, replaced
  ...) - drawn repaired, measured again in 5 s`.
- **Experience bar on the VR palm menu:** WoW's purple XP bar ("XP n / m", "Level 80" at the cap) under the palm menu's buttons.
- **The cloak in VR (and desktop first person):** it was taken off for your own eyes because its collar wraps the front of the neck.
  Now it stays on and only the collar is folded flat behind the neck, so you see your cloak looking down or behind you. Log:
  `Armour: full shell for the local player: first person - the cape stays on, its collar (N vertices above the shoulder line) folded behind the neck`.

### Additions after the 11.6 test (2026-10-04, version 11.7)
- **Buffs and debuffs at the top right (desktop)**, as WoW's BuffFrame (3.3.5a BuffFrame.xml: TOPRIGHT -205, -13; 30 px buttons, rows of 8
  right to left, durations under them - gold, white under a minute; up to 32 buffs and 16 debuffs, the debuffs below with WoW's
  red border). Right-click a buff to cancel it. The VR HUD keeps them under its player frame. Log: `UI: BuffFrame at the top right ...`.
- **Accepting a duel on desktop:** in desktop first person a popup had no usable cursor (Player.log: "client camera state not readable
  (no field _currentCameraState ...)" - the cursor only came with Alt). The client's camera state is now found by its type, and a popup
  (or the stack split) turns the cursor on by itself. Timed popups (summon, duel out of bounds) update their text in place instead of
  rebuilding the buttons under the click. Log: `UI popup: DUEL_REQUESTED shown ... cursor ..., catcher on ...`.
- **Missing sounds:** every popup's open / close (StaticPopup.lua "igMainMenuOpen" / "igMainMenuClose"), the duel flag's drop
  ("DuelingFlagSpawn" at the flag's Spawn animation), raid warnings and boss emotes; the Dungeon Finder role-check sound moved to the
  right moment. Log: `UI sound: '<name>' played for the first time (...)`.
- **Deeprun Tram:** its station ambience ("SubwayStation", from the station building's WMOAreaTable rows) now plays - buildings'
  ambience is used where the zone has none. The tram cars' own sound entries (6756-6758) are not in this install, so the cars stay
  silent. Online, the transport log now prints the path rotation the realm sends (180 deg runs the cars along the tunnel).
- **Item tooltips complete:** "Tumultuous Ring" / "Stalwart Treads" are random-suffix items - their template has no stats (AzerothCore
  item_template RandomSuffix 381 / 385); the stats sit on each item (ITEM_FIELD_RANDOM_PROPERTIES_ID, PROPERTY_SEED, enchantment slots
  7-11, Item.cpp:669-705), which we never read. Now read, with the suffix's SpellItemEnchantment stats ("of the Soldier": +12 Strength
  ...). One tooltip builder (W/Play/WowItemTooltip.cs) online and offline in 3.3.5a's order and colours: every rating, Equip / Use /
  Chance on hit with Spell.dbc text, sockets and socket bonus, sets (n/N) with bonuses, requirements in red, heirloom scaling, unique,
  durations, "<Made by>", descriptions - in bags, character, bank, loot, rolls, mail, trade, auction, merchant and quest rewards.
- **Spell effects on the body (the Holy Light "streak from the hip"):** our M2 animator turned every billboard bone fully toward the
  camera; WoW's cylindrical billboards (bone flags 0x40 lock Z, 0x10 / 0x20 lock X / Y) only turn about their locked axis
  (WebWowViewerCpp calcBoneMatrix). HolyLight_Low_Head.m2's column hangs from a pivot 2.6 m below the head, so it tipped over and lay out
  of the hip. Fixed for every model (226 spell, 291 world, 23 creature, 8 item models use them). Body effect points now face the avatar's
  root (WoW keeps ground / top-of-head on the model root) instead of the twisting shoulders, the head point sits on top of the head, the
  chest point in front of the upper chest (WoW's attachment 34 ratio). Log: `SpellVfx: attachment N (...) on the avatar -> ...`.
- **Avatars in the zone's light:** the sun, ambient and fog that avatars' lit shaders read were already WoW's zone light (WowLighting),
  but the environment REFLECTION stayed Unity's default blue daylight sky (no skybox / reflection probe in our scene) - so every glossy
  avatar surface shone blue-white, even at night in Eastern Plaguelands. Now a 16 px reflection cubemap of the zone's own sky (Light.dbc
  bands, as SkyDome.shader) replaces it, updated with the light; the client's settings come back in the room and on leaving. Log:
  `Lighting: avatars lit by ... reflection = the zone sky ...`, `Lighting: shaders under the local player root: ...` (if an avatar still
  looks bright, its own shader - a toon minimum light or emission - is the reason; this line names it).
- **Shift + loot = loot all** (WoW's AUTOLOOTTOGGLE, Bindings.xml:1327, default Shift): Shift held when you open a corpse inverts Auto
  Loot - everything taken at once (or the window, if Auto Loot is on).
- **Taper top / Taper bottom** (Settings > Armour, the full armour's pieces): x0.5 .. 1.5 at the piece's top and bottom (chest and waist:
  upper / lower edge; arms and legs: shoulder or hip / wrist or ankle; the cloak: shoulders / hem). Saved, seen by others (record ints
  57-59). Size and Tilt now share a row, as do the two tapers.
- **The waist and chest grow evenly:** their Size scaled about the hip / chest joints, which sit in front of the back - so the rear stuck
  out a lot. Now they scale about the avatar's measured middle (the torso remap's centre).
- **Hide avatar part** (Settings > Armour, per piece): your avatar's own body under the selected piece - skin and clothes - is hidden so
  only the WoW armour shows (no clipping): chest, waist, arms, hands, legs, feet. The helm's is "Face in helm" (head hidden, face card).
  Done on copies of the avatar's meshes with that part's vertices moved onto their own bone (positions only, so the meshes' LOD levels
  are untouched, unlike 11.3's hair cut); the originals come back when it is off, and while the armour measures you. Everyone sees it.
  Log: `Armour: avatar of <who> hidden under the armour (chest, ...) - N vertices on M of K skinned meshes ...`.

### Fixes after the 11.5 test (2026-10-04, version 11.6)
- **Huge boots after a relog:** the fitting WAS saved and loaded (Player.log: "144 values loaded ... gen 280"), but the session started
  DEAD - the avatar lay in the death pose when the full armour measured it ("L shin 3.00/3.00 ... L foot -> <=4.50"), so the boots
  followed the 3x shins. The armour now measures only an upright avatar (hips at least 0.75 x the leg above the feet, spine within
  35 degrees of vertical; ours) and waits otherwise (hidden); your own avatar is measured anyway after 20 s only when you are not dead,
  sitting, mounted or on a taxi (a seated VR player). A hand / foot is also never more than 2x WoW's, whatever its limb measures. Log:
  `Armour: full shell for <who>: waiting for the avatar to stand upright before measuring it (...)`.
- **Your fitting is saved:** every change is written 2 s later and on pause, closing and leaving the world (two copies); the Armour
  page's **Save** now writes at once. Log: `Settings: saved N bytes, gen G ... (the Armour page's Save button)`.

### Fixes after the 11.4 test (2026-10-04, version 11.5)
- **Lying down when dead, and WoW's other states on the avatar** (the client's own poses through the Bridge, which the client syncs to
  everyone, VR too): dead (not released) = ProneForward (face-down, as WoW's HumanMale Death falls); /sleep = LayDownBack; /sit, eating
  and drinking = Meditation (cross-legged, as WoW's SitGround) with WoW's EmoteEat (anim 61) hand-to-mouth every 8 s (ours) on desktop
  avatars; /kneel = Kneeling; chairs = SittingStraight. Release and resurrection stand you up; walking stands you up (online kneel /
  sleep send CMSG_STANDSTATECHANGE 0); mounts, taxis and forced moves step in front and hand back. Offline /sit /kneel /sleep work
  and a dead player is rooted until release. Log: `Gear: local player lies down (death) - the client's pose ProneForward ...`.
- **Resurrecting others ("I cannot attack that"):** resurrection spells (Redemption, Resurrection, Ancestral Spirit, Revive, Rebirth)
  carry their target type in Spell.dbc's Targets field (0x8000 CORPSE_ALLY), which we did not read - the cast went out with no target
  and the realm answered "Invalid target" (the voice "I cannot attack that"). Now the dead player (unreleased) or their corpse is the
  target, as AzerothCore expects (Spell.cpp:2091). The "X wants to resurrect you" popup (WoW's StaticPopup RESURRECT, with the reclaim
  countdown) accepts or declines.
- **Corpses after release:** a released player's corpse (the realm's CORPSE object) now stays at the death spot as their WoW character
  lying dead, "Corpse of <name>", selectable; clicking a Somnium ghost's avatar selects their corpse; resurrecting it brings the ghost
  back there. Bones are not drawn. Offline has no other players, so nobody else to resurrect there.
- **Stormwind cathedral slabs:** Stormwind.wmo group 92 'cathy_facade01' (exterior, 2810 tris, spire to z 249) is the whole-cathedral
  stand-in seen from the canals; the detailed cathedral is group 95 'holy01'. 3.3.5a draws a building's outdoor groups from inside it
  only through portals that lead outdoors (wowdev WMO flag 0x8; WebWowViewerCpp wmoObject.cpp portal walk) - none of Cathedral
  Square's do. Now the same rule for every building with portals (672 of 1985): the camera's group from the floor under it, a portal
  walk with view cones (8 portals deep), outdoor groups shown only in a cone that reaches outdoors; renderers only, collision unchanged;
  flying 10+ yd above every floor = outside (everything drawn). From the Trade District a few far tops (the outer wall over the inner
  gate) are now sky, as in the client. Log: `World: WMO ... portal culling: N exterior groups ...`, `... portal culling applies:
  camera in interior group ...`.
- **Quests that would not hand in:** (1) a quest with 2+ reward choices: the tapped reward was looked up by object, and the dialog is
  rebuilt in the background (a chair or door coming into sight, late item names), so the choice was lost and "Choose a reward first"
  stayed - now each reward button keeps its index (online and offline). (2) online, "ready to hand in" now also follows the server
  (REQUEST_ITEMS completable, OFFER_REWARD), as 3.3.5a does, and "Continue" sends CMSG_QUESTGIVER_REQUEST_REWARD. Log: `... hand in quest
  ... - hand-in path: ...`. All 8 offline zones' delivery quests hand in (probe OfflineDelivery 65/0).
- **Trainer spam:** buying sent a request per tap (each answered "failed", with an error, a sound and another list request); a late list
  re-opened the window; each purchase re-asked twice. Now one buy per spell until its answer (2 s at most), at most one buy per 0.25 s, one
  list request at a time, a list nobody asked for never opens the window, the same error at most once a second. Log: `Online trainer:
  guard dropped ...`.
- **Need / Greed / Pass icon:** the item's icon was drawn fully transparent (a picture made without a texture starts transparent black,
  UiKit.cs:64, and the roll window never reset its colour) - now shown.
- **Defaults keeps your armour:** it wiped the Armour Adjustment fit, Armour fit and seat height (saved at once) and turned the armour
  off. Those are now kept (the Armour page's Reset resets a piece) and the armour stays on.
- **Defaults:** Immersive armour Full and combat animation WoW (full body); settings saved before 11.5 move to these once (log:
  `Settings: moved to the 11.5 avatar defaults once ...`).
- **No vibrating on griffons (desktop):** the flight wrote your position only late in the frame while the client places a desktop avatar
  earlier; now as the ground mounts: the ride steps and writes the seat in Update (order -500), and the pose and seat are held in
  LateUpdate before the chase camera.

### Fixes after the 11.3 test (2026-10-04, version 11.4)
- **The fitting view was black** (and 3D target portraits empty): both drew on layer 31, which the client's URP renderers never draw
  (URP_Renderer.asset m_OpaqueLayerMask 1475593719 leaves out layers 3, 9, 11-13, 18, 19, 27, 29, 31). Now layer 30, which the client
  draws and nothing uses. Log: `UI fitting view: first render - ... on layer 30 ...`.
- **Tilt** (Settings > Armour, every piece): -45 .. +45 degrees, + leans the top forward (the chest and waist about their middle,
  arms and legs about the joint, the helm and shoulders about themselves; the cloak: + swings its hem back). Saved, seen by others
  (record ints 55-56).
- **Other players saw your armour wrong** (right again after a rejoin): their game, and yours, calibrated and measured your avatar once
  - often while it was still loading, the client's culling stand-in, or off screen (a culled Animator leaves the bones behind the
  meshes). The full armour now measures only once the avatar is drawn, and calibrates and measures again when its Animator, humanoid
  avatar, bones or meshes change; the helm re-reads the head's turn on a new humanoid avatar. Log: `Armour: full shell for <who>: the
  avatar changed (...) - calibrated and measured again`.
- **Combat animations for other players, and after an avatar change:** every Somnium avatar also has a hidden "WearablesRoot"
  copy (BaseAvatarRoot.cs:140, mirrored from the main copy each frame, never drawn for others); our body finder did not know it and
  could bind to it - the WoW animation (and the armour following it) then moved an invisible copy: "my clothing moves but my avatar
  does not", and others saw nothing. Now the main copy is always preferred (looked for again every 2 s until found), and a new
  Animator / humanoid avatar / destroyed bone re-binds at once. Others also used THEIR OWN animation settings to decide what they saw
  of you; now your choice travels with you (record int 32 bit 28). Log: `Gear: 'Name' (player N) WoW combat animation on - posing
  their avatar copy ... (MainRoot)`, `Gear: body of X: avatar changed (...) - re-bound`.
- **Quest tracker names:** item objectives showed "Item 13471" until something else changed - the names arrived (item query) but the
  tracker only redrew when a count changed. Now it redraws on new text too, in WatchFrame's own reversed form: "- 0/1 The Deed to
  Brill" (WatchFrame.lua:839-841, 1270-1277); the quest log keeps "The Deed to Brill: 0/1". The quest item button shows sooner and fits.
- **Quest map (WotLK 3.3 quest POIs, online):** the server's quest areas (CMSG_QUEST_POI_QUERY, AzerothCore QueryHandler.cpp:411-478).
  The Quest Log's **Show Map** button and the tracker's numbered POI buttons open the WoW Map in a new **World Map** view (the View
  button: Live -> Classic -> World Map) - the zone's own map art, numbered quest icons, the selected quest's blue area, your arrow,
  party and corpse, the zone's quest list and "Show Quest Objectives". Live / Classic show the icons and area too. 3.3.5a's minimap
  has no quest areas (none in Minimap.xml / .lua), so neither does ours. Offline has no POI data (map art only). Log: `Online quests:
  POI - ...`, `UI quest map: ...`.
- **Face in helm** (replaces 11.3's hair cut, which broke the avatar mesh's own LOD levels - "I look like a ghost"): with the helm on,
  the head is hidden the client's own way (the head bone scaled to 1e-10, MainRoot.cs:18 / 99-112) on every copy, and a snapshot of
  the face (taken before, on layer 30, transparent background) is drawn on a face-shaped card inside the helm, following the head.
  Mirrors and the Somnium camera too; every player's game does it for everyone (the wearer's choice is synced). Settings > Armour
  "Face in helm". Log: `Armour: face card for <who>: snapshot of the ... copy, face W x H m ..., head hidden on ...`.

### Additions and fixes after 11.2 (2026-10-04, version 11.3)
Both Somnium players need this upload to see each other's armour fit, hair cut and cape.
- **Armour too big (Full):** the avatar's posed fingers and the shoe above the ankle counted as hand / foot thickness (Player.log
  `L hand 3.00/3.00 ... L foot 3.00/3.00`, the 3x limit) - boots like a bell. Hands and feet are now at most 1.5x the forearm / shin
  they sit on (ours). Log when it applies: `Armour: full shell for ...: N hand / foot thickness capped ...`. Armour fit now goes to 0.50.
- **Armour Adjustment for every piece** (Settings > Armour): Helm, Left / Right shoulder, Chest, Waist, Arms, Hands, Legs, Feet, Cloak
  (the last seven with Immersive armour "Full"). Each: Show, Left / right, Down / up, Back / forward, Size. Arms, hands, legs and feet
  move both sides together (+ = outward); Size = how thick it sits (feet: the boot's length too; cloak: the whole cloak). Per slot,
  saved, seen by others (record ints 48-54). Log on Save: `UI: Armour Adjustment saved - helm (...), ..., cloak (...)`.
- **Fitting view:** with the Armour page open, a "Fitting" panel left of Settings shows your avatar and armour from the front, live
  (12 renders/s, only while shown); picking a piece zooms to it (the cloak from the back); Turn left / right, Zoom to piece / Whole body.
  VR: beside the lid, with your head (the client's mirror copy); the laser reaches its buttons. Log: `UI fitting view: built (...)`,
  `UI fitting view: first render - ...`.
- **Hair hidden under the helm** ("Hide hair under helm", on): head triangles outside the helm's box are not drawn - beside, above,
  behind, or below it at the back; the face and chin are kept. A copy of each affected avatar mesh is drawn instead and the original
  comes back when the helm goes. Mirrors and the Somnium camera too; others see it. Log: `Armour: hair cut under the helm of ... - N head
  triangles outside the helm hidden on M of K avatar meshes ...`.
- **Cape physics** (Full armour with a cloak; Magica Cloth cannot be used - it is not in the reference set the Somnium upload server
  compiles worlds against): 6 simulated rows inside the cape hang, swing after turns, trail when walking / running and settle when
  you stop; they stay outside your back and legs (also seated on a mount). Every client simulates every cape itself (nothing extra is
  sent). Hidden in first person; the Cloak slot's Show / offset / size apply. The cape now moves with your back instead of stretching
  (it flared twice as far). Log: `Armour: cape physics for <who>: 6 rows (...)`.
- **Horse seat:** you sat with your hips ON the saddle point (attachment 0); WoW's own human riders hold the pelvis 0.249 m (male) /
  0.228 m (female) above it in their Mount animation (measured, probe RiderPelvis) - now 0.24 m, plus Settings > Avatar > "Mount seat
  height" (-30 .. +50 cm, saved).
- **Stormwind's floating cathedral:** Stormwind.wmo group 93 'bazaarfacade03' (MOGP 0x28C1: unreachable interior, no portal of its
  own) is a flat-card copy of the cathedral that WoW shows only through portal 38, the Trade District's window; we drew it everywhere,
  over the real one. Such portal-only backdrop groups are now drawn only while the camera is in the group that looks through that
  portal, on its side, never with collision; antiportal (occluder) groups are not built. Only this one is placed in the open world
  (all 1985 WMOs checked). Log: `World: WMO ...STORMWIND.WMO group 93 'bazaarfacade03' (...): drawn only while ...`.
- **NPC helmets and shoulders:** humanoid NPCs never got their helm / shoulder MODELS (only the baked body). Now from
  CreatureDisplayInfoExtra's NPCItemDisplay: the helm on attachment 11 (race / gender model, its texture), the shoulders on 6 / 5, and
  what the helm hides (HelmetGeosetVisData: hair, facial hair, ears). Stormwind City / Royal Guards, Ironforge Guards, Mountaineers,
  Northshire guards, Dughan, Bolvar, Magni. Static pieces share one mesh; nothing runs per frame. Log: `Entities: <name> display <id>:
  helm ..., shoulders ..., cape ..., geosets ...`.
- **Stance bar** (desktop): Wow.exe's own rule - aura 36 or SPELL_ATTR2_USE_SHAPESHIFT_BAR, never SPELL_ATTR2_NO_SHAPESHIFT_UI, in
  StanceBarOrder: paladin auras, DK presences, warrior stances, druid forms, Stealth / Shadow Dance, Shadowform, Metamorphosis (Ghost Wolf
  and hunter aspects are not on 3.3.5a's bar). An aura is checked while you have it from your own cast; clicking casts it (WoW's
  CastShapeshiftForm has no toggle). Log: `UI stance bar: N forms for <class> (...)`.
- **Spellbook tabs:** General and your class's trees (Holy / Protection / Retribution ...) down the right edge, WoW's art and tooltips,
  a page per tab, spells by name then rank (Wow.exe's rules, SkillRaceClassInfo flag 0x80 = General). Log: `UI spellbook: tabs ...`.
- **Need / Greed / Pass:** the item's slot, icon, count and coloured name; hovering the icon shows its full tooltip.
- **Auto dismount to attack:** right-click, the attack key or a melee ability on a ground mount dismounts first (WoW's Auto Dismount,
  default on in Wow.exe), then attacks; in the air it is still refused. Log: `Mount: auto dismount to attack <name>`.
- **Weapon away** (desktop): a cast or ranged spell (Exorcism, Holy Light, Hammer of Justice, Frostbolt, Hearthstone) puts the weapon
  on its sheath place; a swing, a melee ability or Z draws it again; mounting puts it away until you fight or press Z (your choice -
  WoW does neither). Others see it. Log: `Gear: weapon sheathed for a ranged cast (<spell>)`, `Gear: weapons drawn again (...)`.
- **Hand effects** follow your hands during the WoW combat animation (they were placed before the pose). Log: `SpellVfx: the local
  player's hand / weapon effects follow the posed avatar ...`.

### 1. Mouse and windows like WoW (desktop first, then VR)
- **Panels.** Press C, then P: the Spellbook opens at the left and the Character window moves to the centre. Talk to a vendor or a
  quest NPC: the Spellbook closes and the NPC window takes the left (WoW's rule). Log: `UI windows: Spellbook (SpellBookFrame) opened
  at left x 0` / `... closed (pushed out by Merchant)`.
- **Clicks stay on the window.** Click on a window's paper: your target stays. The micro buttons bottom right are WoW's 10 plus the
  World Map; their tooltips show the keys. Talents, PvP and Dungeon Finder appear once you reach their level.
- **Keys:** C Character, P Spellbook, Shift+N Talents, N WoW Map, O Social, K Skills, U Reputation, Y Achievements, B / F12 backpack,
  F8-F11 single bags, Shift+B all bags, J Mounts / Companions, H focus.
- **Escape** closes, in this order: a popup, the Game Menu, a centre window, the unit menu, Raid Info, a cast, the item on the cursor,
  all windows, then the target. With nothing left it opens the Game Menu (Interface / Key Bindings / Logout / Exit Game / Return to
  Game). Log: `UI: Escape -> closed <what>.`
- **Destroy an item.** Drag a bag item onto the ground (or click it, then click the ground): "Do you want to destroy X?". Yes destroys
  it; a rare or better item needs DELETE typed first. Log: `UI: destroy <name> xN (bag b slot s) - asked (DELETE_ITEM)` then
  `- confirmed` or `- kept`.
- **First person:** zoom fully in and hold Alt (or open any window): the cursor appears. Log: `UI cursor: first-person catcher on (Alt held)`.
- **VR palm:** look at the left palm. Row 1 = WoW's micro buttons (Character ... Game Menu); row 2 = Settings, World Map, Mounts,
  Chat, Party, Backpack. Log: `UI: palm menu 319 x 140 units ... row 1 ...`.
- **VR windows:** a window opened from the palm appears on the lid (one at a time, sized to the window). A vendor, trainer or bank
  appears next to the NPC (log `UI: <Kind> window placed in the world (towards <NPC>)`); the bags open on the lid beside it.
- **VR drag:** point at a bag item, hold the trigger and move: the icon follows the laser (a haptic tick on pick-up). Release over a
  slot = move; release in the air = the destroy popup in front of you (Yes / No with the laser; type DELETE on the headset keyboard).

### 2. Vendors, bags, bank, innkeepers, books (online and offline)
- **Vendor (desktop, online).** Right-click a vendor (Northshire or Goldshire innkeeper / trade vendors), or pick "I want to browse your
  goods". WoW's merchant window opens at the left and the backpack opens. Right-click a row = buy one; Shift-click = quantity box;
  left-click picks it up, then click a bag slot = bought into that slot. Log: `Online npc: <name> (..., npc flags 0x80) ->
  CMSG_LIST_INVENTORY - merchant`, then `Online vendor: SMSG_LIST_INVENTORY from <name>: N items (slots a..b), repair yes/no`.
- **Selling.** Right-click a bag item while the merchant is open, or drop it on the merchant window: the money goes up and it shows in
  the buy-back button. The Buyback tab lists the last 12; click one to buy it back. Worn items cannot be sold by a drop.
- **Repair (armorer).** Repair All shows the cost in its tooltip. "Repair an Item" turns on the repair cursor; click a damaged item in
  the bags or on the character. Right-click, a click on the world or Escape ends it. Log: `UI bags: repair cursor on: ...`.
- **Walk away** more than 10 yd: the window closes (`Online vendor: the merchant ... closes - ...`).
- **A gossip NPC with only a vendor option** opens the merchant at once (`Online npc: ... has only the Vendor option ... chosen
  directly (GossipFrame.lua:10-17)`).
- **Bags.** The bag bar is bottom right; the keyring button appears once you own keys. Drag items between slots (the item goes INTO
  the slot you drop it on), Shift-drag (or Shift-click) a stack to split it, drop an item on a bag button to put it in that bag,
  drag a weapon onto the paper doll and back. Log once: `UI bags (npcui): WoW's container frames - backpack, 4 bags, keyring, 7 bank
  bags (ContainerFrame.lua); ours: ...`.
- **Bank (online).** Talk to a banker: the bank opens with the banker's name and the bags open too (`Online bank: SMSG_SHOW_BANK of
  ...`). Right-click moves items between bank and bags. Purchase asks "Do you want to purchase a bank slot for:" with the cost (red if
  you cannot afford it). Drop a bag on a bought slot, then click the slot to open that bank bag.
- **Innkeeper (online).** Choose "Make this inn your home": "Do you want to make <area> your new home?". Accept prints "<area> is now
  your home." and the Hearthstone tooltip names it (`Online bank: home = <area> ...`). Walking away closes the question.
- **Books, plaques and readable items (online).** Right-click a readable item or click a book / plaque: the item-text window shows the
  pages with Prev / Next and scroll arrows.
- **Gossip options with a code or a cost (online):** the dialog asks "Please enter code:" or shows the confirm text with the money.
- **VR:** the merchant and bank hang next to the NPC, books in front of you, the bags in the arm window. Tap an item = buy one; hold
  0.5 s = quantity; drag onto a bag slot = buy there. Tap a bag item = sell (merchant) or move (bank). Drag a bag item onto the
  merchant = sell. Hold a stack = split. Tap a buyback item = buy back.
- **Offline:** in Northshire talk to Aldric Venn: "Let me see what you have for sale." opens the merchant with the zone's food, drink
  and potions (8 offline vendors, ours). Buy, sell and buy back work; walking away closes it. Log once: `OfflineSession: ours - offline
  vendor of Northshire Valley: 4 items (...)`. Offline there is no bank, innkeeper, repair or readable item.

### 3. Trainers, skills and professions
- **Profession trainer (desktop, online).** Click a tailoring trainer: the Trainer window opens on the left (green = can learn,
  red = a requirement is missing, grey = known). Train Apprentice Tailoring, accept the "uses a profession slot" popup; the list
  refreshes. Log: `Online trainer: <name> (Tradeskill) - N services: ...`, `Online trainer: learned service 3911 from <name>`.
- **Class trainer:** the gossip "train" option opens the same window. Its "unlearn talents" option is in section 4.
- **Skills (K):** Tailoring 1/75 is under Professions; Unlearn asks first. Log: `Online profession: N skills shown (...)`.
- **Profession window:** click the Tailoring spell. With linen in the bags pick Bolt of Linen Cloth, set 2 with - / +, press Create:
  two casts in a row. Log: `Online profession: Tailoring 1/75 - N recipes (...)`, `Profession: Create 2 x Bolt of Linen Cloth (2963) - 1/2 sent`.
- **Skinning and fishing:** skin a looted beast; when fishing the bobber bobs on a bite (`Online profession: a fish bites (...)`)
  and clicking the bobber loots it.
- **VR:** the same windows on the left; laser or poke on rows and buttons. A trained spell pulses the right hand; a fishing bite
  pulses both hands. Skills is on the palm.
- **Offline:** talk to your class trainer in the start zone: class spells up to level 10, free; Train teaches a missed one. K / palm
  opens Skills with the class lines. Profession buttons say "Not available offline". Log: `Offline trainer: <npc> (<class>) - N
  services: ...`, `Skills offline: N class skill lines for <class> ...`.

### 4. Talents and glyphs
- **Desktop online, level 10+:** Shift+N or the Talents micro button opens the 3 trees, a pet tab (hunter with a pet) and a Glyphs
  tab (level 15+). Log: `Online talents: N points free, spec 1/1, K talents (a/b/c), glyphs ...`.
- **Preview** (on by default, Settings > Gameplay): clicks add preview points, Learn sends them (`Online talents:
  CMSG_LEARN_PREVIEW_TALENTS ...`), Reset clears them, right-click removes one. With preview off, a click learns at once
  (`CMSG_LEARN_TALENT`). Locked tiers and missing prerequisites are red in the tooltip and send nothing.
- **Glyphs:** right-click a glyph in the bags: Talents opens on the Glyphs tab (`glyph X waits for a socket`). Click a matching socket
  (`inscribe glyph ... CMSG_USE_ITEM glyphIndex N`); a wrong socket says "That Glyph cannot be inscribed there.". Shift + right-click a
  glyph, then confirm, removes it (`CMSG_REMOVE_GLYPH`).
- **Unlearn talents** (class trainer gossip): Talents opens with the cost question; Accept logs `unlearn all talents accepted
  (MSG_TALENT_WIPE_CONFIRM ...)`. **Dual spec** (bought at a trainer): the spec tabs and "Activate These Talents" (`Online talents:
  activate spec N`).
- **VR:** the same window on the wrist UI, preview always on. Tap a talent to add a point; tap a socket to inscribe.
- **Offline:** Talents opens at level 10; points go into the character record and their passives apply from the start of the
  session; "Unlearn Talents" resets for free. Log: `Offline talents: learned <name> r/max`, `Offline talents: K learned talents from
  the record`. Glyphs and dual spec offline say "Online only - play on your realm.".

### 5. Character, reputation, currency, achievements
- **C (desktop, online):** WoW's character pane. With a known title, a "Select a Title" box under the level line ("None" plus your
  titles; `Online titles: chose 'Private <name>' (CMSG_SET_TITLE)`). Two stat blocks with dropdowns (Base Stats / Melee / Ranged /
  Spell / Defenses) and WoW's tooltips; five resistance icons. Drag items onto / off the equipment slots; broken items show red. Log
  once: `Online stats: armor ..., resistances .../..., ranged AP ..., spell crit ...%, defense ... (+...), mana regen .../... per 5 s`.
- **Tabs at the bottom:** Character / Pets / Reputation / Skills / Currency. The Pets tab is the Mounts window (J), which now has
  WoW's **Companions** and **Mounts** tabs at the top when you know a companion: Summon / Dismiss for companions, Mount / Dismount for
  mounts (`UI: Mounts window - Summon companion '<name>' (spell N).`).
- **Reputation (U):** Classic > Alliance > Darnassus ... with At War, Move to Inactive and Show as Experience Bar. Gaining reputation
  prints "Reputation with X increased by N." (`Online reputation: Stormwind +250 -> Friendly 4250`).
- **Currency:** Honor, Arena Points and emblems by category (`Online currencies: N known (honor H, arena A)`).
- **Achievements (Y):** Summary, categories, filter. Earning one shows the "Achievement Earned" toast with its sound (`Online
  achievements: earned 6 'Level 10' (10 points, total ...)`, `UI: achievement toast shown (AchievementAlertFrame, AchievementSound)`).
- **VR:** Character from the palm micro row, tabs by tapping; the toast on the floating HUD with a short pulse on both hands.
- **Offline:** the same character layout (no titles; ranged attack power, parry and armor are filled); Reputation and Achievements say
  "Online only - play on your realm.".

### 6. Friends, guild, mail, chat
- **Log in (online):** `Online social: ours (not WoW's) - names wait 3 s ...`, `Online guild: ours - roster / info re-asked at most every
  2 s ...`, `Online mail: ours - MSG_QUERY_NEXT_MAIL_TIME at login ...`. In a guild: `Online guild: roster - N members (M online), R
  ranks` and "Guild Message of the Day: ..." in the chat.
- **Channels:** entering a zone joins General and LocalDefense (capitals add Trade and LookingForGroup): "Joined Channel: [1. General -
  <zone>]". `/1 hi` talks in General.
- **Chat kinds:** `/g`, `/o` (green "[Officer]"), `/ra`, `/rw`, `/bg`, `/c Name` - each with WoW's header. Outside a raid `/ra` says
  "You are not in a raid group".
- **Names:** left-click a [Name] in the chat = whisper ("Tell Name: "); right-click = Whisper / Invite / Target / Ignore / Cancel.
- **Commands:** `/friend <name>` ("<name> added to friends."; unknown = "Player not found." after 3 s), `/who 10-20`, `/afk`, `/dnd`,
  `/join test`, `/chatlist`, `/leave test`, `/ginfo`, `/groster`, `/chat`, `/wave` at a target ("You wave at X." and the avatar waves;
  log `Online social: text emote WAVE, anim 67`), and `/duel /follow /inspect /trade /readycheck /pvp`.
- **Social window (O):** Friends / Who / Guild / Chat / Raid. A guild invite shows the 60 s Accept / Decline popup.
- **Mail:** right-click a mailbox (creature or object): the inbox and the backpack open (`Online mail: mailbox opened`, `Online mail:
  inbox - N mail(s) ...`). Take All takes the money first; soulbound items cannot be mailed; "Mail sent."; walking 10 yd away closes it.
- **Guild charter:** a guild master sells the charter (`Online guild: registrar - charter 5863, cost 1000 copper, 9 signatures on this
  realm`). Offer it to players; turning it in without enough signers says "You need more signatures." (see "Before you test").
- **VR:** palm Chat = a 0.45 x 0.35 m window: row A Say Party Guild Officer Yell, row B Emote Reply Raid Channel, row C 8 quick
  emotes sent at your laser target. Press a [Name] = whisper with the headset keyboard; hold 0.5 s = the name menu.
- **Offline:** friends, Who, channels, guild and mail each answer one line ("Friends are on the online realm" ...). `/wave` prints
  "You wave." and plays the animation; `/afk` and `/dnd` work. Plain text says "Chat is on the online realm".

### 7. Trade, duels, inspect, follow, raids, PvP flag, summons
Test with two characters on your realm, one in Somnium and one in the WoW client, standing together.
- **Unit menu:** target the other player and right-click the target frame: Set Focus, Whisper, Inspect, Invite, Trade, Follow, Duel,
  Raid Target Icon, Cancel (out-of-range rows greyed). VR: the palm Party button.
- **Trade:** choose Trade (or drop a bag item on the other player). Put items in (right-click or drag), type money, both click Trade:
  "Trade complete.". A trade asked from the WoW client shows our TRADE popup (No by itself after 60 s). Log: `Online interact:
  CMSG_INITIATE_TRADE`, `UI popup: TRADE shown`.
- **Duel:** "Duel starting: 3 / 2 / 1", the opponent turns red. More than 50 yd from the flag: "Exiting duel area, you will forfeit in
  10 Seconds.". `/yield` or SELF menu > Forfeit Duel gives up.
- **Inspect** (28 yd): 19 slots with tooltips and enchants, plus a Talents tab summary.
- **Follow:** "Following X." - your avatar walks 2.5 yd behind. A movement key, more than 40 yd or a taxi: "You stop following X.".
  VR: position only; any stick input stops it. Log: `Follow: following X`.
- **Raid:** Social > Raid > Convert To Raid; 8 groups; right-click a member for Promote / Main Tank / Remove; Ready Check (VR: a
  vibration); Raid Target Icon > Skull puts a skull over the mob. Log: `Online raid: MSG_RAID_READY_CHECK ...`, `Raid marks: world icons drawn`.
- **PvP flag:** SELF menu > Player vs. Player > Enable (the PvP icon on the player frame); Disable starts the 5:00 timer; `/pvp` toggles.
- **Summon:** a warlock's ritual shows "X wants to summon you to <zone>. The spell will be cancelled in 2 Minutes." (Accept greyed in combat).
- **Offline:** SELF and creature menus show Set Focus, Raid Target Icon, Cancel; trade / duel / inspect answer one line saying they
  are on the online realm.

### 8. Auction House, battlegrounds, Dungeon Finder (online)
- **Auction House:** right-click an auctioneer (`Online auction: <house> opened (deposit N %, cut N %)`). Browse: type a name and
  Search; click a row, Bid: "Bid accepted.". Auctions tab: drag (or right-click) a bag item onto the slot, set the price and 12/24/48 h,
  Create Auction: "Auction created."; Cancel Auction: "Auction cancelled.".
- **Battlemaster:** First Available plus the running instances; Join Battle. The queue icon appears top right; when ready the entry
  popup shows (`UI: CONFIRM_BATTLEFIELD_ENTRY shown (<battle>, slot 0)`); Enter Battle. The scoreboard opens when the battle ends.
- **PvP micro button (level 10+):** honor and kills; the Battlegrounds tab joins (Join as Group when you lead a group).
- **Dungeon Finder (level 15+):** roles, Random or Specific Dungeons, Find Group; the animated eye; the role check (`UI:
  LFDRoleCheckPopup shown`) and dungeon ready popups (`UI: LFDDungeonReadyPopup for <dungeon>`); right-click the eye for Teleport and
  Leave Queue. Once per session: `UI: queues hub built (...)`.
- **VR:** laser or poke; name / money boxes open the headset keyboard; the queue icon and eye float right of the view; invites pulse
  both controllers. **Offline:** the PvP and Dungeon Finder windows say "... on the online realm".

### 9. Flight paths, Charge and knockbacks, ships
- **Flight master (online):** a new one shows "New flight path discovered!" and the map opens. Click a lit node: you ride the gryphon /
  wind rider on the server's route and land. With too little money: "You don't have enough money!". Log: `Online taxi: map of node N
  '<name>' from <master> - K known, R reachable`, `Online taxi: flight started - spline S, N points, L yd, T s ...`, `Flight: landed at ...`.
- **Two-map flight** (Light's Hope Chapel -> Tranquillien): a short black fade, the loading screen, then the flight goes on in
  Ghostlands (`Online taxi: map edge reached - CMSG_MOVE_SPLINE_DONE sent ...`, `Online taxi: flight continues ...`).
- **Charge / Intercept / Feral Charge / Death Grip / Disengage / a boss knockback (online):** the avatar dashes or flies the arc and
  lands (`Forced move: Path N ... takes the avatar` ... `Forced move: Path N done at ...`; `Online: knockback - counter N ... - acked`).
- **VR:** the view dims during the move; Settings "Forced movement comfort (VR)" = Dim (default), Blink or Smooth. Fear / polymorph:
  the stick does nothing, the view is 50 % dark, and both hands tick when control is lost and when it comes back.
- **Fear / polymorph / root / stun:** WASD, the stick and jump do nothing until it ends (`Online: control of the character LOST ...`).
- **Strafing** around a mob while attacking or casting: no "You are facing the wrong way!" (`Online: facing flushed before a cast /
  swing at ... - turned N deg`, first 3 times).
- **Boat or zeppelin (online)** (Stormwind-Auberdine, Orgrimmar-Undercity): walk on deck and the ship carries you (position only on desktop
  and VR: your view never turns); at the other continent you are still on board (`Transports: standing on ...`, `Online transport: on board
  <name> ...`, `Transports: on-board hold released - the deck is under the feet`). The Thunder Bluff / Undercity / Ironforge
  elevators and the Deeprun Tram should carry you too now (their floors come from the game's own M2 collision mesh) - please check.
- **Offline:** Warrior Charge at a mob 20 yd away dashes there (`OfflineSession: Charge - X yd to <mob> in N ms (28 yd/s)`); Mage
  Blink jumps 20 yd; Hunter Disengage leaps back. Offline flight paths work as before.

### 10. Ground spells, weapon glows, item spells, runes
- **Ground spells (desktop, third person).** Mage Blizzard: press its key -> a green rune circle 8 yd wide lies under the mouse and
  follows the ground. Beyond 30 yd -> the red no-sign. Left click in range = cast; a short right click or Esc = nothing is cast. Log:
  `Ground target: Blizzard aimed (radius 8 yd, the mouse)` then `Ground target: Blizzard placed at (x, y, z) (d yd)`.
- **First person:** the cursor appears while aiming. With Settings "Ground spells at the screen centre" on, the circle sits under the
  screen centre. Start line: `Spell effects: weapon enchant glows on, ground spells at the mouse`.
- **VR:** release a wrist menu on Blizzard -> the circle appears where the laser meets the ground; the right trigger casts with a
  pulse. Grip a wrist menu and release with nothing highlighted = cancel. Pointing at Somnium's own UI never confirms; 30 s = cancel.
- **Areas (online):** Consecration, Blizzard, Death and Decay, Rain of Fire, Hurricane and Flamestrike's burn stay on the ground for
  their duration (also another player's or a boss's). Arcane Explosion and Thunder Clap show an impact on every mob hit; Multi-Shot
  sends arrows to every target. Log: `SpellVfx: ground area of spell N - kit K, area model ... (first per spell)`.
- **Offline (our zones):** Blizzard at 40 yd says "Out of range" and keeps aiming; at 20 yd it is placed and ticks on the mobs
  inside (`OfflineSession: Blizzard placed at ...`, `Blizzard 42208 ticks at ... (n hit)`).
- **Weapon glows:** poisons, Crusader, Flametongue, Windfury, Rockbiter glow on NPC weapons, other WoW players' weapons, your own
  Somnium avatar's held weapon and other Somnium players' (synced). Settings "Weapon enchant glows" off = all gone. Log: `Enchant
  visual N: <models>@<attachment> (first per visual)`.
- **Item spells (online):** use Instant Poison from the bags -> the icon follows the mouse with "Choose an item for Instant Poison.";
  the bags or the Character window open by themselves if neither is open; click the main hand -> applied (drip glow at the tip).
  Disenchant: click a green item in the bags.
- **Death knight (online):** the rune frame under the player frame (desktop) and on the VR HUD, with sweeps (`UI: Rune frame
  attached to the desktop player frame ...`). **Rogue / cat druid:** combo points on the target frame.

### 11. Action bars, VR rows, VR spell slotting, immersive strike
- **Desktop keys:** 1..= the main bar (in a stance or form it shows the form's bar: warrior Battle Stance = slots 72-83), Shift+1..6 the
  bar page, Alt+1..= the Bottom Left Bar, Ctrl+1..0 the pet bar, Ctrl+F1..F10 the stance bar. Log: `UI: action keys - 1..= main bar,
  Shift+1..6 bar page, Alt+1..= Bottom Left Bar (ours), Ctrl+1..0 pet bar, Ctrl+F1..F10 stance bar`.
- **Four extra bars (desktop):** Bottom Left, Bottom Right, Right and Right 2 are on by default (Settings > ActionBars, Always Show).
  Drag a spell from the spellbook (P) onto Bottom Left button 3 (or Shift-click the spell, then the button). The cast bar, bags and
  pet bar move up for the bottom bars (`UI: managed frame offsets ...`). Online log: `Online: action button 62 (Bottom Left Bar 3) =
  <id> '<name>' (CMSG_SET_ACTION_BUTTON)`; log out and in, it is still there.
- **Offline warrior:** the Battle Stance bar is filled from the main bar once (`Offline: form bar 1 (Battle Stance) filled from the
  main bar (n slots, slots 72-83)`).
- **VR rows:** hold grip on either hand: the half-ring opens on "Action Bar" with a pip ladder and faint ghost rings. Push the hand
  about 5 cm away: a firm click and "Bottom Left Bar"; about 12 cm = "Bottom Right Bar". Pull toward you = "Right Bar", then "Right
  Bar 2". The previous row is hidden. Twist to highlight, release to cast; release in the middle to cancel. Log: `VR input: right hand
  row -> Bottom Left Bar (depth 0.06 m)`.
- **VR slotting:** palm > Spellbook, tap a spell's icon or name. Both wrist menus appear at the hands ("Place <spell>"). Grip a hand,
  twist, push or pull to a row, release: the spell is on that button. Log: `VR input: slotting Fireball - wrist menus up`, then `VR
  input: Fireball placed on Bottom Left Bar button 3 (slot 62)`. Release in the middle (or 30 s without a grip) = "Cancelled.".
- **VR immersive strike (melee):** Settings > ActionBars > Immersive strike ON (choose the ring's bar and distance). Target an
  enemy: a circle of the bar's attack spells stands in front of your chest and follows your facing. Hit an icon fast with your weapon (any part of the blade)
  or your hand: the spell is used on the target, with a strong pulse and a white flash. The wrist menus still work. Log: `VR strike:
  ring on (n icons from Action Bar, distance 0.55 m, radius 0.24 m) - target <name>` and `VR strike: right fist|blade 2.4 m/s hit
  <spell> (Action Bar button k) on <name>`.

### 12. Forms on your avatar, WoW combat animation, immersive armour
- **Forms (offline, desktop):** Night Elf druid with Bear Form: your Somnium avatar disappears and the WoW bear stands there, walking,
  running and swiping with you; cancel the form (right-click the buff) and your avatar is back at once. Log: `Forms: offline character
  drawn as display 2281 ...`, `Forms: local look Creature display 2281 (form 5, spell 5487) - avatar hidden (N renderers) ...`.
- **Desktop first person in a form:** you see the bear's body under you, head hidden; tall forms are shrunk in your view only.
- **Online:** druid forms, Ghost Wolf, polymorph / hex on you, Noggenfogger, Orb of Deception: your avatar shows the creature as long as
  the realm says. Release Spirit = a pale-blue ghost of your avatar (a Night Elf = the wisp); Stealth / Prowl = a faint shell; Shadowform
  = dark violet.
- **Second Somnium player:** sees your bear / sheep / ghost at full size, your nameplate over it and your spells from the creature's
  hands (`Forms: '<you>' (player N) look Creature display 2281 ...`).
- **VR:** in a form you see the creature's back under you (head hidden); a small class-coloured glow marks each controller so the
  menus and laser still work. Nothing follows the creature's pitch or roll.
- **WoW combat animation (desktop):** fight with a weapon drawn while standing: the whole avatar (feet, knees, hips, spine, arms) takes
  your race's combat stance and plays WoW's swings, ability animations, casts, parry / dodge / block. While walking, the legs keep
  walking and the upper body attacks. Others see the same. Log: `Gear: WoW full-body animation (ours) - blend in 0.2 s / out 0.3 s, ...`.
- **Settings > Avatar:** "Show forms on my avatar" (on), "Combat animation style (desktop)" Simple (arms) / WoW (full body, default),
  "Immersive armour" Off / Helm and shoulders / Full (experimental, 11.2; the default from 11.5), "Armour fit" 0.5-1.4. The Armour page (11.2)
  moves, sizes and hides the helm and each shoulder.
- **Immersive armour:** set Helm and shoulders: your equipped helm and shoulders appear on your avatar, sized to it; swapping gear
  changes them; "Hide Helm" online hides it. You never see your own helm in first person or VR; others do (and, with "Helm in photos
  and mirrors", the Somnium camera and mirrors). Log: `Armour: the local
  player helm <id> -> Item\ObjectComponents\Head\..._HuM.m2, shoulders <id> -> ...`.
- **Emotes:** `/dance` or `/wave` on desktop plays WoW's emote animation on the avatar (dance loops until you move), synced to others.

### 13. Dungeons, raids and area triggers (online)
- **Exploration quest:** in Elwynn take "The Fargodeep Mine" (quest 62) and walk deep into the mine: "Discovered Fargodeep Mine: N
  experience gained". Log: `Online instances: area trigger 88 (map 0, sphere r 10) entered at (...) - CMSG_AREATRIGGER sent`.
- **Inn rest:** walk into the Lion's Pride Inn: `Online instances: resting (an inn or a city) at (...)`; leaving: `no longer resting`.
- **Dungeon (desktop and VR):** walk into the Stormwind Stockade portal: the Stockade's loading screen, then you stand inside the
  prison. Log: `Online instances: map 0 -> 34 'The Stockade' instance type 1 (WMO-only)`. Walking out brings you to Stormwind.
- **Only your group inside:** Somnium players of your group inside the dungeon are drawn; one in another copy is hidden (`Instances:
  remote character N hidden - another copy of map 34 ...`).
- **Menus** (desktop: right-click your player frame; VR: palm Party with nothing targeted): Dungeon / Raid Difficulty (level 65+),
  "Reset all instances" (asks first) and "Raid Info" (saved instances, Extend Raid Lock; Escape closes it).
- **Lock warning** entering a raid in progress: "You have entered an instance already in progress! ..." with Accept / Leave Instance.
- **Errors:** a portal you may not enter shows WoW's red line ("You must be level 80 and in a raid group to enter.").
- **Offline:** nothing changes.

### 14. Quests and the corpse run
- **Elwynn corpse quest (online):** take "Find the Lost Guards" from Guard Thomas, then right-click "A half-eaten body" (VR: target ray +
  trigger): the quest frame opens at once; Complete Quest opens "Discover Rolf's Fate"; "Rolf's corpse" in the murloc camp works the same.
  Log: `Online quests: CMSG_GAMEOBJ_USE on A half-eaten body (type 2, entry 55, lock 43 - a quest giver opens without the Opening spell)`.
- **Corpse-looking quest NPCs** (e.g. Teronis' Corpse): the "!" shows and right-click talks (`Online quests: CMSG_QUESTGIVER_HELLO to
  ... (..., looks dead - alive for the server)`).
- **Gossip options** (innkeeper, guard): buttons with icons below the quests (`UI: gossip option N '...' chosen`, `Online npc:
  CMSG_GOSSIP_SELECT_OPTION to ...`).
- **Corpse run:** die, release, run back. In VR the Resurrect popup appears about 39 yd from the body and follows you. Log: `Online:
  position synced before the corpse reclaim (...)`, then `Online death: Resurrect at the corpse sent (CMSG_RECLAIM_CORPSE ...,
  position synced first)`; a refused press retries once.
- **Soulstone / Reincarnation:** the death popup has a second button (`Online death: self-resurrection offered - spell 3026 ...`).
- **Party:** in the quest log select a quest and press Share (`Online quests: share quest N ... (CMSG_PUSHQUESTTOPARTY)`); the
  Somnium receiver gets the quest's Accept / Decline page.
- **Quest tracker** (top right on desktop, right of the HUD in VR): objectives, timers, a quest-item button; click a title = the quest
  log. The quest log reaches all 25 quests, with Track / Untrack and an Abandon question.
- **Offline:** the tracker shows offline quests; Share answers "Sharing quests needs a party on your realm (online).".

### 15. Pets, totems, stables
- **Hunter (online):** Call Pet -> the pet frame with the name and happiness icon, and the 10-button pet bar above the action bar
  (Attack / Follow / Stay | abilities | Aggressive / Defensive / Passive). Left click = use, right click an ability = autocast,
  Ctrl+1..0 = the buttons. Log: `Online: Pet bar for 0x... 'Fang' family 1 ...`.
- **Pet frame right-click:** Rename / Abandon / Cancel; a warlock / DK / mage pet shows Dismiss.
- **Spellbook > Pet / Demon tab** (or VR palm Pet): the pet book; drag an icon onto pet bar buttons 4-7.
- **Stable master:** select a pet, Stable / Take, Purchase a slot.
- **Shaman:** totems top left with real timers (right-click = destroy); with Call of the Elements the totem row above the main bar.
- **VR:** the pet bar on the arm strip (tap = use, hold = autocast); push to the "Pet Bar" row on the wrist menus.
- **Offline (warlock):** Summon Imp -> AzerothCore's default bar; Stay, Follow, Passive and autocast work (`Offline pets: ours -
  autocast starts ON ...`).

### 16. Vehicles and possession (online)
- **Eye of Acherus** (Death Knight start zone): the avatar disappears and you fly the Eye with WASD / Space / Ctrl; its spells are on
  the bar; Cancel brings you back (`Online: possessing Eye of Acherus (0x...)`).
- **Scarlet Cannon:** right-click it; A/D turn, W/S aim (about +-25 degrees), a ring shows where the shot lands, key 1 fires, Leave
  Vehicle gets you off. The desktop main bar is hidden while the vehicle bar shows (`UI: main action bar hidden under the vehicle bar`).
- **Havenshire Stallion** (Grand Theft Palomino): ride the horse at its own speed with its spells; leaving restores your speed.
- **Passenger seats** (Wintergarde Gryphon, mine carts): you sit on the vehicle and follow it.
- **VR:** the seat follows the vehicle's turn only, never its pitch or roll, with a short blink on and off. On a turret the left stick
  turns and aims.
- **Offline:** vehicles and possession do not exist.

### Probes (harness, for the record)
Every iteration-11 probe and the earlier regression probes run with `bash P/integ11/probes/run_all.sh` (results in
P/integ11/probes/summary.txt). Online code is tested only against the harness fake server; nothing here has run on your realm or in a
headset yet.

## Social, HUD, focus, immersive hold (stage S1) - iteration 9 (2026-09-27)

What your test with Wildy asked for, in one upload:
- **Chat** (online): WoW's chat frame bottom left on desktop (Enter or `/` to type), a **Chat** window from the VR palm menu,
  every chat type from the realm (say, yell, emote, party, guild, whispers, channels, system, NPC speech) and **chat bubbles**
  over whoever speaks - over the Somnium avatar for a Dreamport player.
- **Groups** (online): invite (target frame right-click > Invite, or `/invite Name`), WoW's invite popup with Accept / Decline,
  **party frames with live health / power**, the leader icon, and the **Somnium profile picture** of a Somnium player.
- **HUD**: Settings > **HUD** moves and resizes the player / target / party / focus frames (desktop and VR separately).
- **Select party members to heal**: click a party frame (desktop) or point the laser at it + trigger (VR).
- **Focus**: short press **X** on the left controller (VR) or **H** (desktop): set focus / target the focus; three quick presses
  clear it. Holding X is still Somnium's inspector.
- **The same laser for selecting**: far targeting and our menus follow the Somnium laser; our yellow dot and our second line are
  gone (a white dot stays only on our own game panels, where the client draws no line).
- **Return to the room** moved to the Settings window (every page, press twice); the wrist button and "Press K" text are gone.
- **Somnium name tags** of the other Somnium players are hidden in this world (a Bridge feature, first used in this upload).
- **Two instances?** Player.log now shows which Fusion session you are in, and a one-time hint appears when you are alone.
- **Immersive casting: the cast starts at once, then waits for your push** (online this needs the server step below), with a
  stronger "ready" vibration and a breathing glow; instant spells cast at once. **+10 % damage** on every spell you release
  with Immersive casting, shown as a buff **"Immersive Mode"** on your character (a Dreamport house rule of your realm).
- **Game folder**: when the WoW folder is not found the room opens a folder browser; Settings > **Game** > Change... picks another
  folder; it is saved per Somnium account on this PC.

Dreamport additions (not in WoW), named as the rules ask: the VR Chat window with its Say / Party / Yell / Emote / Reply buttons
and Type row (the headset keyboard); the online Enter holder (Enter opens WoW's chat, never Somnium's quick chat, online on
desktop); the `/help` text; bubbles for emotes; the 3 s wait for an unknown speaker's name; the palm menu's **Chat** and
**Party** buttons (Party = Invite <target> / Leave party); "Leave party" on the party member's menu; Somnium profile pictures in
the party frames; the Settings > HUD page; the focus key H and the short-press X; the far-hover tick in the right controller and
the soft white hover glow; the "alone in this instance" hint; the "Immersive Mode" buff and its +10 % damage; no walking while
the desktop chat edit box is open (as in WoW, the Somnium movement keys pause while you type).

### Before you test online: the server step (the lead does it, once)

The immersive hold and its +10 % damage need the small AzerothCore module `ServerKit/dreamport-hold` on the realm
(`ServerKit/README.md` section 9). It affects **only Dreamport VR players with Immersive casting on**; normal WoW clients and
desktop players are never held and never get the bonus. Everything else in this upload works without it. From Git Bash on the
user's PC (the worldserver restarts: players online are disconnected for about a minute):

```bash
IP=145.241.232.229; U=ubuntu; H=/home/ubuntu; KEY=~/.ssh/wow_droplet; KIT="/d/Dreamport/WoW/World Of Warcraft/ServerKit"
SSH="ssh -i $KEY -o StrictHostKeyChecking=accept-new -o ServerAliveInterval=30 $U@$IP"
scp -i $KEY -o StrictHostKeyChecking=accept-new -r "$KIT/setup-hold.sh" "$KIT/dreamport-hold" $U@$IP:$H/
$SSH "sudo nohup bash $H/setup-hold.sh >/dev/null 2>&1 </dev/null &"
$SSH 'sudo tail -n 40 -f /var/log/wow-hold.log'      # wait for the last line: HOLD OK or HOLD FAILED (compiler errors are in the log)
$SSH 'cd /opt/azerothcore && sudo docker compose logs ac-worldserver | grep "Dreamport hold"'
```

Expect `Dreamport hold: module loaded (protocol 1, hold time-out 30000 ms, arm window 5000 ms) - only casts a Dreamport VR client
asks for are held`, `Dreamport hold: immersive damage bonus +10% (ImmersiveDamageBonus 0.10) - ...`, and one `Dreamport hold: <name>
(...) said hello (a Dreamport client, protocol 1)` per Dreamport login. If the **compile** fails, the running realm is untouched and
the module is taken out again (HOLD FAILED). If the compile works but the new worldserver does not come up or does not log the module's
line, it also ends with HOLD FAILED and the log says why - the realm then runs the new build: remove the module as in `ServerKit/README.md`
section 9 ("Remove it") or send the log. **Until HOLD OK, online immersive casting works as in stage O2** (held on your side before the cast; the cast bar
runs after the push) and the "Immersive Mode" buff is not shown online (the realm would not add the damage). The C++ was written
without an AzerothCore compiler here (checked against stub headers of the exact AzerothCore declarations): this is its first compile.

### Test guide (stage S1)

**Chat** (desktop, online, with Wildy or a normal 3.3.5a client nearby):
1. Press **Enter**: WoW's edit box opens under the chat frame (bottom left) with "Say:". Type `hello` + Enter. Both of you see
   `[<your name>] says: hello` (white) and a bubble over the speaker (over the Somnium avatar for a Dreamport player). Log:
   `Online: chat sent Say (5 chars, language 7)`, `Online: chat Say from <name> (5 chars)`, `Chat bubbles: first bubble (...)`,
   for a Dreamport friend `Chat bubbles: a Somnium player's line shows over their avatar.`
2. `/y HEY` = a red line and a bubble; `/e waves` = `<name> waves` in orange; in a group `/p hi` = `[Party] [<name>]: hi`
   (blue-violet); not in a group: `You aren't in a party.` (log `Online: chat party chat without a group: ...`).
3. `/w Wildy psst` = `To [Wildy]: psst` (pink); Wildy gets `[<you>] whispers: psst` with WoW's whisper sound and answers with
   `/r ...`. `/w Nobody hi` = `No player named 'Nobody' is currently playing.` (log `Online: chat player not found: ...`).
4. Type a `|` (e.g. `a|b`): it arrives as `a|b` and nobody is kicked (the client sends `a||b`, AzerothCore's rule).
5. A GM account: `.gm on` in the edit box = the server's yellow system answer (log `Online: GM command '.gm on'`).
6. NPC speech (city guards, a fleeing creature's `... attempts to run away in fear!`) = yellow / red / orange lines with a bubble
   over the NPC.
7. Enter never opens the Somnium quick-chat box while online (log once `UI: chat - holding the EventSystem selection ...`); while
   the edit box is open your avatar does not walk (log `Speed: the WoW chat edit box is open - the avatar does not walk while
   typing ...`). Offline there is no WoW chat frame and Enter is the Somnium quick chat again (`UI: chat ready (offline: chat is on
   the online realm)`).
8. VR: palm menu > **Chat** (the yellow speech-bubble button): the chat window above the palm (`UI: chat window (VR) opened (palm
   menu).`). Pick Say / Party / Yell / Emote / Reply, press the Type row = the headset keyboard (`Keyboard: opened for Say.`), type,
   Enter: the line appears and the others see a bubble over your avatar. While the keyboard is open the trigger types and never
   changes your target (`VR input: the chat keyboard is open - far targeting paused while typing (logged once).`). A gold number
   on the Chat button counts lines that came while the window was closed.

**Groups, party frames, profile pictures** (online, same faction, two Dreamport players or one plus a normal WoW client):
1. Desktop: target the other player, **right-click the target frame**: a menu (their name, Invite, Cancel) > Invite. You see
   `You have invited <name> to join your group.` Log: `Online group: CMSG_GROUP_INVITE '<name>'` and `Online group:
   SMSG_PARTY_COMMAND_RESULT op INVITE '<name>' result 0 -> You have invited ...`. VR: palm menu > **Party** > Invite <target>, or
   type `/invite <name>` in the chat window.
2. The invited player gets WoW's popup `<you> invites you to a group.` with Accept / Decline and the invite sound; in VR it is a
   panel about 1 m in front (laser or poke). Nothing pressed for 60 s = declined. Log: `Online group: SMSG_GROUP_INVITE from <you> -
   PendingInviteFrom set`, `UI: group invite popup (StaticPopup PARTY_INVITE) for <you> ...`.
3. Accept: both party frames show the other player (their Somnium profile picture, round, else the class circle) with live health
   and power and the leader icon; the inviter sees `<name> joins the party.` Log: `Online group: SMSG_GROUP_LIST #n ... (changed)`,
   `Party: the party frames show the server group (1 other member(s)).`, `Online group: SMSG_PARTY_MEMBER_STATS_FULL #1 for <name>
   ...`, `Portraits: loading <nick>'s Somnium profile picture (https://somniumspace.org/userimage/...)` then `Portraits: <nick>'s
   profile picture loaded (...; kept in memory only).` (or `failed (HTTP 404 ...)` when they have no picture) and `UI: party portrait
   WxH cut round (1).` **This upload is the first test of the profile-picture download** - please send the `Portraits:` lines.
4. Walk far apart: the frames keep updating from the server's party stats.
5. **Heal your friend**: click their party frame (desktop) or point the laser at it and pull the trigger (VR; the frame lights up
   gold): they become your target. Log: `UI: party frame <name> (0x...) clicked - target set (1).` Online without a group, a
   Somnium friend's frame is clickable too.
6. Leave: desktop - click the member's party frame, right-click the target frame > **Leave party**; VR - palm menu > **Party** >
   Leave party. You see `You leave the group.` and `Your group has been disbanded.`; the frames go back to the Somnium players. Log:
   `Online group: CMSG_GROUP_DISBAND`, `Online group: SMSG_GROUP_DESTROYED`, `Party: the party frames show the other Somnium players (1).`
7. Wrong name: `/invite Nobody` = `Cannot find player 'Nobody'.`; inviting the other faction = `Target is unfriendly.`
8. Offline: clicking a party frame says `Other players can be targeted on the online realm.` (each player runs their own world).

**HUD size and place, focus** (desktop and VR):
1. Desktop in game: **O** (Settings) > **HUD**. Move "HUD size (desktop)", "HUD across (desktop)" and "HUD down (desktop)": the
   player, target, party and focus frames (with pet and totems) grow and move at once. Log: `UI: desktop HUD group size 1.20x at 10%
   across, 5% down (...; Settings > HUD).` **Reset HUD** puts them back: `UI: Settings 'Reset HUD' - the HUD group back to its default
   size and place (desktop and VR).`
2. VR: palm menu > Settings > HUD: "HUD size (VR)", "HUD left / right (VR)", "HUD down / up (VR)". The floating HUD grows and moves
   and still follows your view as before (no roll). Log: `UI: VR HUD size 1.20x, moved +0.10 m right and -0.04 m up (Settings > HUD).`
3. Focus: target someone, then short-press **X** on the left controller (VR) or **H** (desktop). A yellow line `Focus: <name>.` and
   a small frame labelled "Focus" right of the party frames. Log: `Focus: set to <name> (0x...) by the X button (VR) (1 so far).`
   Target something else and press again: your focus is your target again (`Focus: <name> targeted by the X button (VR) ...`). Three
   quick presses: `Focus cleared.` (`Focus: cleared by the X button (VR) pressed three times (...)`). Holding X still opens Somnium's
   inspector (log, first 3 times: `Focus: long X press (0.62 s) left to the client (inspector mode).`). Start log: `Focus: button
   ready - VR: a short press of the left X (under 0.35 s; ...), desktop: H. ...`
4. The focus frame: click it (desktop) or laser + trigger (VR) = target the focus; desktop right-click = Clear Focus; a desktop
   right-click on a party frame = Set Focus. Log: `UI: focus frame shows <name>.` / `UI: focus frame hidden - no focus.`
5. **Return to the room**: Settings (any page), the middle button at the bottom. The first press changes it to "Press again to
   leave" for 3 s; the second takes you back to the room (the character is saved; online it logs out). Logs: `UI: Settings 'Return
   to the room' pressed once - waiting 3 s for the second press.` then `Room: back to the room (the Settings window's Return to the
   room button) from ...`. K still works on desktop.

**The Somnium laser** (VR; desktop is unchanged):
1. Enter the world. Log: `VR input: target ray aims along the client's laser, Near-Far interactor 'Near-Far Interactor Right' (far
   caster, stabilized origin); near the right hand: ...` (it lists 'Grab Right' as "not the entity layer"), `VR input: far targeting
   along the client's laser, nothing of ours drawn on the ray; ...` and `UI: VR pointer = the client's laser, Near-Far interactor
   'Near-Far Interactor Right' (...).`
2. Point at a creature 20-60 m away: no line and no dot of the game's; its nameplate glows, a soft white glow lies on it, a light
   tick in the right hand. Trigger = your target (`VR input: target ray trigger on <name> at N m`, `Entities: VR ray on <name> ->
   target`); trigger again = attack / talk as before.
3. An NPC within 5 m: the Somnium laser draws its own line to it with the same highlight, no second mark; trigger = target, again =
   talk (`... at N m (the client's laser is on it)`).
4. Ground or sky + trigger: `Entities: VR ray on the ground -> target cleared` (unchanged).
5. Hold X (the inspector): no highlight, the trigger is the inspector's (`VR input: the client's inspector mode is on ... - far
   targeting paused.`); release: `VR input: the client's laser is free again - far targeting on.`
6. Laser on a Somnium panel (the VR keyboard, the tablet) + trigger: your target does not change (`VR input: the client's laser is on
   a Somnium UI panel - that trigger press is the client's.`).
7. Our own game panels (palm menu, windows, party frames): the pointer now sits on the laser's line and its dot is white.

**Name tags, wrist, instances** (two whitelisted players, e.g. Shodah + Wildy, desktop and / or VR):
1. No Somnium name plate over the other's avatar, in the black room and in the WoW world (the WoW nameplate from Settings stays).
   Log: `Tags: on - the Somnium name tags of the other Somnium players in this world are hidden while the Dreamport runs ...`,
   `Tags: player N 'Wildy' (has a WoW network record): Somnium name tag hidden with the Bridge (SetPlayerTagDisableState(true))`,
   then `Tags: player N: the Bridge hid the name tag (its canvas 'Canvas' is off)`. **This upload is the first use of that Bridge
   call** - please send the `Tags:` lines. If the log says `Tags: the Bridge player-tag member did not work here (...) - this visit
   uses the proven fallback (the tag's CanvasGroup alpha)`, the tags are still hidden. Leaving the world: `Tags: off (leaving the
   world) - N player tag(s) given back`. A visitor sees nothing, as before.
2. VR: the left wrist shows only the place and zone (no "Back to room" button). Desktop: the label over your avatar shows the place
   and zone without "Press K to return to the room"; K still returns (`Room: back to the room (the K key)`).
3. Both players compare Player.log `Net: room joined (player id X, master M) - Fusion session #xxxxxxxx (a fingerprint of its
   N-character name: the same on two players' logs = the same instance), region .., P of M player(s) by SessionInfo; K player(s) in it
   (ids 1 (me), 2); the Bridge lists K other Somnium player(s); the whitelist names 1 other player(s)`. **Different fingerprints = two
   different Somnium instances** (the "I don't see Wildy until he relogged" case). Joins and leaves: `Net: player 2 joined the Fusion
   session #xxxxxxxx - ...` / `Net: player 2 left ...`.
4. Alone in the instance for 60 s (friend elsewhere), then enter the WoW world: a white hint for 30 s under the wrist panel (VR) or
   above the info label (desktop), plus two yellow lines in a game: "No other Somnium player in this instance" / "A friend may be
   in another instance". Log: `Net: alone in the Fusion session #xxxxxxxx for 60 s (...)` and `Wrist: the one-time hint 'alone in
   this Somnium instance' is shown for 30 s (...)`. Once per visit.

**Immersive casting: start, wait, push; +10 % damage** (VR, Settings > Gameplay > Immersive casting ON):
1. Any Dreamport client, desktop or VR, once after entering the world: `Online: asking the realm once whether it holds immersive
   casts (DPHOLD HELLO, an addon message to ourselves - nobody sees it, nothing changes)`, then (after HOLD OK) `Online: this realm
   holds immersive casts (Dreamport hold module, protocol 1, time-out 30 s) - ...; spells released with Immersive casting deal +10%
   damage (the module's house rule)` - or `Online: this realm has no Dreamport hold (it echoed DPHOLD HELLO back; ...)`. No chat line.
2. The buff: in VR with Immersive casting on, a buff **"Immersive Mode"** (two glowing hands) sits in your buff row; its tooltip
   says "Spells you release with Immersive casting deal 10% more damage." Log: `Online: buff 'Immersive Mode' shown (...)` /
   `OfflineSession: buff 'Immersive Mode' shown (...)`. Switch the setting off: it goes. Right-click does nothing (it is ours; the
   realm never hears of it). Normal WoW clients do not see it. Online on a realm without the module it is not shown.
3. Desktop (2D), online: cast Fireball. Nothing changes: the cast bar runs, the spell fires at its end, no `DPHOLD` line in Player.log.
4. VR, online: select an enemy and pick Fireball from the radial menu. The cast bar and hand glow start at once. Log: `Online:
   Fireball (133) cast at once, the realm is asked to hold it at the end of its cast (DPHOLD HOLD, castCount N) #1`, then `Online:
   Fireball armed on the realm (DPHOLD ARMED, ... ms cast) - it will wait at its end`.
5. At the end of the cast the controller vibrates, the spell glows in the hand and pulses about once a second, nothing fires. Log:
   `Online: Fireball held by the realm at the end of its cast (DPHOLD READY after X s) - push to release it` and `VR input: Fireball is
   ready in the right hand after X s (vibration 0.85 for 0.18 s) - it waits, glowing, until you push it forward`.
6. Push the hand forward: the missile flies. Log: `VR input: pushed the right hand at X m/s -> Fireball released at <target> (selected
   target)`, `Online: Fireball pushed after X s held - released on the realm (DPHOLD GO, castCount N)`, `Online: Fireball released by
   the realm at the push (held X s, SPELL_GO Y ms after DPHOLD GO)`. Its damage is +10 % (the realm's combat log numbers; the realm's
   log: `Dreamport hold: <name> released 133 (...) with Immersive casting - +10% damage for that cast (...; logged once per login)`).
7. VR: pick an instant (Fire Blast, Frost Nova, a buff): it fires at once, no glow, nothing to push. Log: `VR input: Fire Blast is
   instant - cast at once (...)`, `Online: Fire Blast is instant - cast at once (...)` and `Online: Fire Blast (2136) released with
   Immersive casting - the realm adds +10% damage (DPHOLD BONUS, castCount N) #1`.
8. VR: hold a Fireball and walk with the stick: `Interrupted` (log `Online: cast 133 interrupted (SMSG_SPELL_FAILURE)`, then `VR input:
   held Fireball ended without a push (...)`). Hold one and pick another spell: `Another action is in progress` (`VR input: Fireball is
   still held in the right hand - push it first (...)`). Hold one 30 s without pushing: cancelled (`Online: Fireball held 30 s without
   a push - the realm cancelled it (DPHOLD EXPIRED; its SPELL_FAILURE follows)`).
9. A friend on a normal WoW client next to you sees your cast bar run, stay full until your push, then the missile. Their own casts
   are unchanged.
10. Offline (Play offline), VR immersive: Fireball starts at once, is held at its end (vibration, loop glow) and fires at the push;
    instants fire at once (`OfflineSession: Fire Blast is instant - fired at once (immersive casting holds only spells with a cast
    time; logged 5 times)`); walking while holding = `Interrupted`. The +10 %: `OfflineSession: Immersive casting - spells released
    with it deal +10% damage (ImmersiveDamageBonus 0.10, ours - a Dreamport house rule; logged once)` and `OfflineSession: <spell>
    released with Immersive casting - +10% damage for this cast (n)`; the floating combat numbers are 10 % higher than a normal cast.

**Game folder** (room):
1. Normal start: the room is Ready as before. Settings > **Game** shows In use `D:\WOTLK\World of Warcraft 3.3.5a`, enUS, how it was
   found, and the saved choice (none).
2. **Change...** opens the folder browser (Drives, Up, a desktop path field, Use this folder). Choosing the folder in use just saves
   it (`Install: saved ... as the game folder`).
3. Not found (rename `D:\WOTLK` to e.g. `D:\ZZ_Test`, not a name starting with WoW / WOTLK / Wrath / World of Warcraft): the room
   says "Choose your World of Warcraft 3.3.5a folder - the one with Wow.exe" and the browser is open; go to the install and **Use that
   folder**. Next start: `Install: the saved game folder is used: ...`. Rename it back: the room says in red that the saved folder is
   not usable now and which one it uses instead.

### What was tested (2026-09-27) and what not

Compiled three ways (editor, the Somnium upload server's reference set, editor tools) with 0 errors and 0 warnings; the server-API
guard lists only the Bridge name-tag member (first used here, the upload is its test). Tested against a fake AzerothCore server (the
packets written as AzerothCore's source writes them; never the user's realm): chat 43 checks, groups 36, the hold's client half 25,
the offline hold 6, the immersive bonus 16 online (the marker and the buff) + 10 offline (x1.10 compared with an identical session
that casts normally), focus 15, HUD settings 7, the instance diagnostics 46, the folder browser 37, plus every stage O2 regression
probe. **Not tried in the client or a headset by us**: the chat window and bubbles, the party frames, popups and portraits, the HUD
layout, the laser and highlight, the name-tag hiding, the palm Chat / Party buttons, the folder browser. **Not compiled against
AzerothCore yet**: the hold module (the lead's `setup-hold.sh` run is its first compile).

## Online: combat, items, death, quests (stage O2) - iteration 8 (2026-09-27)

After the first login on your realm the NPCs stood in the right places, but your character had no gear and you could not fight,
take quests or die properly. This stage makes the online game playable the way the real 3.3.5a client plays it: **the server
decides everything** (damage, XP, loot, items, quests, death) and the Dreamport shows it with the same windows, animations,
sounds and spell effects the offline game already had. Offline, dying now follows WoW's real death flow too.

What is new, in short:
- **Your gear and bags** come from the server: the Character window (`C`) and the Backpack (`B`) show your real items with WoW's
  tooltips; tapping an item equips or uses it on the server (food, drink, the Hearthstone, quest items, openable items).
- **Fighting**: auto-attack (`V`, or swing your weapon in VR) with WoW's own checks and messages, damage numbers, XP, level-ups,
  kills and your death. **Every creature and player the server shows now swings and gets hit with its animations (creatures with their attack and wound sounds).**
  A second click (VR: trigger) on the enemy you have targeted starts the attack, like WoW's right-click (offline too).
- **Spells**: every class spell can be cast (not only mounts), with cast bars, channels, cooldowns, auras and your action bar
  from the server. **Everyone's spells show their effects and play their sounds** - NPCs, normal WoW players and other Somnium
  players' characters.
- **Loot** windows with the server's coins and items; auto loot.
- **Death, online and offline, the WoW way**: "You have died", Release Spirit, a ghost at the graveyard in a grey-blue world with
  WoW's ghost music, an arrow and a marker to your corpse (also on the WoW Map), Resurrect near the corpse or at the spirit healer.
- **Quests and NPC talk**: WoW's quest dialog with the server's texts, accept, hand in with a reward choice, abandon, the quest log
  and all progress messages; chests, herbs, ore and wanted posters open.
- **Somnium players (the hybrid)**: the server shows everyone's spells; on top of that the Gear network record carries what only a
  Somnium player has - which hand a spell comes from, the cast in progress and the VR held spell (Immersive Mode) - so other Somnium
  players see it on your avatar's hands, online and offline.
- **Also in this upload (lead fixes)**: on foot your avatar moves at your character's WoW run speed (online the server's value, so a
  ghost or a slowed character moves at the right speed); in VR a wrist / palm window closes by itself once you walk away from it;
  holding or using the Somnium tablet no longer fires the wrist spell buttons.

Still answering **"Not available online yet"**: vendors, trainers, flight masters and bankers without a quest, the mailbox, items
you read (letters, books), choosing gossip options (they are shown as text), difficulty (offline only). Those, chat and groups
come in stage O3b.

What to look for in `Player.log` is in `code font` (every line starts with `[SomniumWoW]`). The online parts were tested
only against a fake AzerothCore server in the test harness, never on your realm; the offline death flow ran on the real game
files (see "What was tested" at the end of this section).

### Test the death flow offline first (no server needed)

Desktop, e.g. a Night Elf in Shadowglen:
1. **Let a creature kill you.** You see "You have died." in red, hear the death cry, and a popup "6 Minutes until release" with
   **Release Spirit**. Log: `OfflineSession: the player died`, `UI: death popup 'DEATH' shown.`
2. **Press Release Spirit.** You stand at the graveyard, the world fades to grey-blue, the ghost music and ambience start and you have
   a "Ghost" buff; you run faster (the ghost's +50%, a Night Elf's wisp +75%). A **Spirit Healer** (the winged angel) stands 5 yd
   ahead. Top right: a turning corpse arrow "Corpse NN yd" and "30 Seconds until resurrection". A gravestone "Corpse" icon floats
   over where you died, and the WoW Map (`N`) shows the corpse icon. Log: `OfflineSession: spirit released (Release Spirit) - a ghost
   at the graveyard ...; reclaim delay 30 s ...`, `UI: ghost HUD on - corpse marker ...`, `UI: ghost-world look ready ...`,
   `Audio: ghost - music 36 'GhostMusic (DONOTRENAME)' (day 3820, night 3821), ambience 4160.`
3. **Run back** following the arrow. Within 39 yd, after the 30 s, a popup "Resurrect now?" **[Accept]**: you are alive at half
   health and mana, and the colours and the zone's music come back. Log: `OfflineSession: the corpse is in reach (.. yd, delay over)
   - Resurrect offered.`, `UI: death popup 'RECOVER_CORPSE' shown.`, `OfflineSession: resurrected at the corpse - N/M health (50%) ...`,
   `UI: ghost-world look out (weight 0).`, `Audio: alive again - the zone's music and ambience.`
4. **The spirit healer.** Die again, release, then click the Spirit Healer while standing next to him: his offer **[Accept]
   [Cancel]**; Accept brings you back at half health. From level 11 WoW asks twice and you get the Resurrection Sickness debuff.
   Log: `OfflineSession: the spirit healer offers his resurrection (targeted, 3.0 yd).`, `UI: death popup 'XP_LOSS' shown.`,
   `OfflineSession: resurrected by the spirit healer - ...`. After **Cancel**, click him again and his offer comes back (log `UI: the
   spirit healer's offer shown again (Spirit Healer clicked).`).
5. **WoW's timers.** Die twice more within 5 minutes: the delay becomes 60 s, then 120 s (`... reclaim delay 60 s ...`). Die and
   wait 6 minutes without pressing anything: you are released automatically (`OfflineSession: spirit released (automatically after
   6 minutes, AC m_deathTimer) ...`).
6. **VR**: the same flow. The popup appears 1.1 m in front of you (right ray + trigger), the corpse compass sits just below the
   centre of your view, and the world greys in the headset.

### Online, on your realm

Log in online (Play tab > **Online** > **Play online as ...**), with a fresh character for the first steps.

**Gear and bags**
1. Open the Character window (`C`) and the Backpack (`B`). The starter gear is in its slots (the weapon on your avatar straight
   away), the backpack holds the Hearthstone and food / drink with stack counts, and "N free" is at the bottom. The names fill in
   within about a second ("Retrieving item information" until then); tooltips look like WoW's (slot and type, damage / speed / dps,
   armor, stats, "Use: ..."). Log: `Online items: inventory ready: 5 equipped, 3 in bags, 13 free, item info 8/8 - Chest 6123
   'Novice's Robe' display 12683, ...` (before the names arrive: `Online items: inventory from the server: ...`).
2. Tap a weapon or armour piece in the backpack (also a trinket or other gear with a "Use:" effect): it moves to its slot, the old
   one goes to the backpack, and a yellow "Equipped <name>" appears. Log: `Online items: equip <entry> '<name>' from bag 255 slot
   <n> (CMSG_AUTOEQUIP_ITEM)`, then `Online items: equipment now: ...`. An item your level is too low for shows WoW's red text, e.g. "You must reach level 10 to use that item."
   (log `Online items: SMSG_INVENTORY_CHANGE_FAILURE 1 CANT_EQUIP_LEVEL_I for '<name>' "You must reach level 10 to use that item."`).
3. Tap food or drink out of combat: the server's food spell and its buff run and the stack count drops by one. Log: `Online items:
   use 4540 'Tough Hunk of Bread' -> spell 433 'Food' (CMSG_USE_ITEM bag 255 slot <n>, castCount <k>, no explicit target)`. Tap the
   Hearthstone: a 10 s cast bar reading "Hearthstone", then the teleport home. Tap Linen Cloth: red "You can't use that item."

**Fighting**
1. At login the log says `Online combat: melee auto-attack, every unit's swings (SMSG_ATTACKERSTATEUPDATE), XP, levels, kills and
   death from the server; ours: ...`.
2. Attack a hostile creature: desktop **`T`** targets the nearest enemy, **`V`** starts / stops the attack (or click it twice: the first click targets,
   the second attacks, like WoW's right-click); VR: swing your drawn weapon at it, or trigger on it twice. Your character draws its weapon and swings every weapon-speed seconds, white
   numbers (yellow crits) float over the creature, and you hear the swing and impact sounds. Log: `Online combat: CMSG_ATTACKSWING
   <name> (<guid>), Hostile, level N`, `Online combat: SMSG_ATTACKSTART - the server swings at <name>`, `Online combat:
   SMSG_ATTACKERSTATEUPDATE me -> <name>: Hit 12 (HitInfo 0x2, state 1)`.
3. The creature fights back with its attack animation and attack sound on every swing, its aggro sound when it pulls, red numbers on
   you and "Miss" / "Dodge" / "Parry" words. Log: `Online combat: SMSG_ATTACKERSTATEUPDATE <name> -> me: Hit 5 (...)`.
4. Step out of reach while attacking: red "You are too far away!" and your race's voice line; turn away: "You are facing the wrong
   way!" (the server checks that you face the target - turn toward it). Log: `Online combat: SMSG_ATTACKSWING_NOTINRANGE -> 'You are
   too far away!'`.
5. Kill it: it plays its death, your attack stops and "+N experience" appears. Log: `Online combat: <name> died - killing blow me
   (SMSG_PARTYKILLLOG)` and `Online combat: SMSG_LOG_XPGAIN +N XP for <name> (M without the rested bonus)`.
6. On a level-up: the "Level N" banner with "Congratulations, you have reached level N!", the level-up effect and sound. Log:
   `Online combat: SMSG_LEVELUP_INFO level N (health +x, mana +y, stats ...)`.
7. Watch another player or an NPC fight nearby: both play their swing and wound animations, the creatures their attack and wound
   sounds, with no numbers over their fight (like WoW).
8. Attack a friendly NPC: "You cannot attack that target.". While mounted: "Can't attack while mounted." and nothing is sent.
9. After a fight: `Online combat: left combat - this session so far: swings mine N (h hit), at me M, others K; kills k, XP +x; packets
   p, odd 0`. If "odd" is above 0 with a warning `Online combat: <opcode> (...) ... - handled as far as read`, a packet surprised the
   client: please send the Player.log.

**Spells**
1. Open the spellbook (`P`): every class spell shows its cost, range and cast time; greyed spells say why ("You have no target",
   "Out of range", "Not enough mana"). Log: `Online: spellbook from the server - N spells: M in the book (cast on the server), K mounts ...`.
2. Target a monster and cast a damage spell (Fireball, Wrath, Shadow Bolt ...): the cast bar, the glow and sound on your hands, then
   the missile, the impact and the damage number; the global cooldown sweeps the action bar. Log: `Online: cast 133 'Fireball' sent
   (castCount 1, targets 0x2 unit 0x..., ... ms cast) #1`, then `Online: cast 133 -> SPELL_START ... ms -> SPELL_GO (1 hit)`.
3. Cast with no target: "You have no target." and nothing is sent (log `Online: cast ... refused here: "You have no target."
   (targets)`). Refusals from the server (mana, range, line of sight) show the server's text (log `Online: CAST_FAILED <spell> result
   <n> <NAME> "<text>"`).
4. Cast a buff with no target or a monster selected: it lands on you; with a friendly player selected it lands on them; the buff shows
   on the buff bar with its time. A channel (Arcane Missiles): `Online: channel 5143 'Arcane Missiles' 3000 ms (MSG_CHANNEL_START)`.
5. Put a spell on the action bar: `Online: action button N = <id> '<name>' (CMSG_SET_ACTION_BUTTON)`. After a relog the bar comes back
   from the server: `Online: action bar from the server (SMSG_ACTION_BUTTONS state 1) - ...`.
6. VR with Immersive Mode: the spell glows in your hand until the push, then it is sent. Log: `Online: <name> held in the hand
   (immersive) - sent to the server at the push`.
7. A caster monster: its cast bar, the glow, missile and impact, and its attack animation when the spell is released.

**Loot**
1. Kill a creature, walk up and click it (twice if it is not your target): WoW's loot window opens with the coins and the item names and icons from the server. Log:
   `Online loot: opened <guid> (loot type 1): <n> copper, <k> items`.
2. Take one item: it leaves the window and goes into your bags, with a green "You receive loot: <name>" and the item's pickup sound
   (log `Online items: looted item <entry> '<name>' x<n> (<total> in the bags)`; quest rewards log "received"). **Take All**: the
   coins give "You loot ..." and the window closes by itself once empty. Walking more than 10 yd away closes it too.
3. A lootable creature too far away: "You are too far away to loot that corpse." (nothing is sent). A dead creature with nothing
   on it has no sparkle, and clicking it does nothing, as in WoW.

**Death and resurrection online** (the same popups and markers as offline, driven by the server)
1. Die: "You have died." and the Release Spirit popup. Log: `Online death: the player died (health 0) at ...` (and `Online combat:
   the player died to <name> (health 0 from the server)`).
2. Release Spirit: you appear as a ghost at the graveyard (its name from the game's WorldSafeLocs), the corpse is marked. Log:
   `Online death: Release Spirit sent (CMSG_REPOP_REQUEST)`, then `Online death: graveyard on map ...` and `Online death: a ghost at ...`.
3. Run back: within 39 yd of the corpse, once the server's delay is over, **Resurrect** appears; press it and you are alive at the
   corpse. Log: `Online death: Resurrect at the corpse sent (CMSG_RECLAIM_CORPSE ...)`, then `Online death: alive again with ...`.
4. Or walk to the graveyard's spirit healer as a ghost, click him and accept. Log: `Online death: resurrection at the spirit healer
   <guid> sent (CMSG_SPIRIT_HEALER_ACTIVATE)`; Resurrection Sickness comes from the server. After Cancel, clicking him again (twice if he
   is not your target) brings the offer back (`UI: the spirit healer's offer shown again (...)`).
5. A priest or paladin on a normal WoW client resurrects you: the resurrect text shows and **Resurrect** accepts it. Log: `Online
   death: ... (SMSG_RESURRECT_REQUEST) - "Resurrect" accepts it`.

**Quests and NPCs**
1. Walk to a quest giver with a "!" and click it (first click targets, the second talks): WoW's quest dialog with the NPC's real greeting (your name, race and class filled
   in) and its quests; gossip options, if any, are listed as text with "(These options are not available online yet.)". You hear
   the NPC's greeting voice, and its farewell when you close the dialog or walk more than 10 yd away. Log: `Online quests:
   CMSG_GOSSIP_HELLO to <NPC> (...)`, then `Online quests: dialog with <NPC> (gossip, <guid>) - N quests [id Available '<title>']
   ... after <ms> ms` (no "(timed out waiting for ...)" at the end when the server answered everything).
2. Open a quest, read its text and rewards (item icons, money, XP), press **Accept**: the dialog closes (the server closes it, like
   WoW), "Quest accepted: <title>" appears in yellow, the quest is in the quest log (`L`) with objectives 0/N, the NPC's "!" goes
   away, and you hear the quest-accepted sound. Log: `Online quests: accept quest <id> '<title>' at <guid>` and `Online quests: quest
   <id> '<title>' in the log (slot s)`.
3. Kill the quest's creatures or pick up its items: "<Name> slain: 1/8" or "<Item>: 1/5" messages and the log counting up; at the
   end "<title> (Complete)" and the "Quest Complete" banner, and the giver shows a yellow "?". Log: `Online quests: quest <id>
   '<title>' complete`.
4. Go back to the giver, open the quest (yellow "?"), press **Continue**, pick a reward, press **Complete**: "<title> completed.",
   the money message, the item in your bags and the XP message; the next quest of a chain appears on offer. You hear the
   quest-complete sound. Log: `Online quests: hand in quest <id> '<title>' reward index <i> (choice <c>)` and `Online quests: quest
   <id> '<title>' rewarded: <xp> xp, <copper> copper (SMSG_QUESTGIVER_QUEST_COMPLETE)`.
5. Abandon a quest from the quest log: "Quest abandoned: <title>" and the quest is gone. Log: `Online quests: abandon quest <id>
   '<title>' (log slot s)`, then `Online quests: quest <id> '<title>' left the log`.
6. Click a chest, or a herb / ore node when you have the skill: the cast bar runs ("Opening", "Herb Gathering", "Mining") and the
   loot window opens. Without the skill: "Requires Herbalism"; clicking while walking: "Can't do that while moving". Log: `Online
   quests: open <name> (type 3, lock <L>) with spell <S> (castCount <N>)`.
7. Click a wanted poster or another quest object: its quests open like an NPC's. Log: `Online quests: CMSG_GAMEOBJ_USE on <name>
   (type 2, entry <E>)`.
8. Click a vendor or trainer that has no quests: "Not available online yet". Click a hostile creature twice (target, then
   attack): you start attacking it. Click a dead creature with loot twice: its loot window opens.

**Next to a normal WoW client on the same realm**
1. Cast a spell from the normal client at a monster. In the Dreamport its character shows the glow, the missile and the impact on
   the monster and plays the spell's sounds; buffs it casts glow on their target. Log (the first 5 casts): `Online: another player's
   cast 0x<guid> '<name>' <id> -> CastSucceeded at 0x<target> (...)`, and while they cast `SpellVfx: N effects spawned, M missiles, ...`.
2. Fight a monster with the normal client: its character swings with its animation and the monster plays its wound sound (no numbers
   over their fight, like WoW).

### Two Somnium players (the hybrid sync)
1. Two Somnium players in the world, offline (or on different realms). Player 1 casts Fireball. Player 2 sees the fire gather in
   player 1's hands (desktop: both hands in the casting pose; VR: the hand that released the radial menu) and hears the precast
   sound; on the release a burst at the hands and no missile (offline each player's monsters are their own). Player 1's log:
   `Gear: our cast record - Fireball (133) casting, hand ...` and `Gear: our release record - Fireball (133) ...`. Player 2's:
   `Gear: 'Name' (player N) casts Fireball (133) - drawn from their record, hand ...` and `... released Fireball (133) ...`.
2. VR with Immersive Mode on: player 1 casts from the right hand and holds the spell. Player 2 sees a glow grow in player 1's right
   hand, then pulse until the push. Player 2's log: `Gear: 'Name' (player N) gathers (immersive) Fireball (133) in the right hand ...`,
   then `... holds Fireball (133) in the right hand ...`.
3. Both online on the same realm, near each other: player 1 casts at a mob. Player 2 sees the precast on player 1's avatar hands, and
   the missile leaves player 1's casting hand and hits the mob. Player 2's log: `Gear: 'Name' (player N) - the server shows their
   character (guid G): ...` and `SpellVfx: guid G is a Somnium player's character - effects anchored to their avatar as drawn here
   (shown)`.
4. A heal on yourself (Lesser Heal): the other player sees the cast burst on your hands and the heal's glow at your feet (their log:
   `... released Lesser Heal (2050) (hand ..., on themselves) ...`).
5. Draw a weapon (`Z`, or grab it in VR): the other player sees it in the same hand, in both modes (unchanged since iteration 2; log
   `Net: slot k = player N ... main D/...`).

### What was tested (2026-09-27) and what not
- **Harness against a fake AzerothCore** (AzerothCore's packet writers ported line by line, every opcode incl. odd flags): items 36
  checks, combat 39, spells 76 (incl. another normal player's and a Somnium character's casts), loot + death 45, quests 42 (+ 4 on
  the real Lock / QuestXP data); offline death flow on the real game files 49; the network record 31. The older probes still pass:
  online login 26, online world 29, online mounts 69, automatic accounts 50, offline mounts 59, the ride record 32.
- **Review + fix**: an independent review checked every O2 opcode and packet reader against AzerothCore; its findings were fixed
  (a "Use:" trinket now equips from the bags, no false "Resurrect now?" when logging in as a ghost, no "You have died" at a login
  lying dead, the spirit healer's offer comes back on a click after Cancel, the cast bar names Opening / Hearthstone) and its 4
  probes pass (trinket 4, ghost login 2, dead login 2, spell facts); all probes above were re-run and still pass.
- **Compile**: the world builds with 0 errors / 0 warnings three ways (editor, upload server, editor tools) and uses only proven
  Somnium / Fusion members.
- **Not tested anywhere yet**: your realm (no agent logs in anywhere), the Somnium client, a headset, two Somnium players, a normal
  WoW client next to the Dreamport. Each step above has a log line so your Player.log answers it.

## Mounts - iteration 7 (2026-09-27)

### Test mounts offline first (no server, no login, no account)

Offline mode runs everything on your own PC, so you can test mounts **before the AzerothCore server is set up and whatever the
online login does**. Desktop first; the headset steps are further down. What to look for in `Player.log` is in `code font`
(every line starts with `[SomniumWoW]`).

1. **Start a character.** In the black room: Play tab, **Offline** (the default; if you tried Online before, click **Offline** -
   the Play tab remembers your last choice), pick any character or make a new one (for example a Human Paladin - you start in
   Northshire). Wait until you stand in the world.
2. **Check the two testing switches.** Press **`O`** (Settings) > **Gameplay**: **"Unlock all mounts"** and **"Fly anywhere"** are
   ticked. They are on by default and remembered like your other settings (if you unticked one earlier, tick it again); the
   mounts and riding they give are never saved to your character. With them your character knows every mount of its faction
   and class (a Human Paladin: 265 of the game's 309) and can ride as if it had learned all riding skills. Log:
   `Mounts: 265 of 309 unlocked for a Human Paladin (testing switch); riding 300 + Cold Weather Flying (derived, not saved)`.
3. **Open the Mounts window.** Press **`J`**. It is WoW's own Mounts pane: 12 mounts a page, sorted by name, arrows to turn pages,
   hover a mount for its tooltip. The Brown Horse is on page 5 for a Human Paladin.
4. **Summon a horse.** Click the **Brown Horse** (Horde: **Timber Wolf**) and press **Mount** while standing still, outdoors. A 1.5 s
   cast bar runs, then you sit on the horse and the camera eases behind you. Logs: `Mounts: cast 458 'Brown Horse' -> mounted display
   2404 (baked), run 11.20 yd/s ...`, `Ride: mounted Brown Horse (spell 458, display 2404 from Baked) - ground 11.2 yd/s, seat (0.19,
   1.71) m, hips ... m measured ...`, `Ride: desktop chase camera on (brain paused: yes) ...`.
5. **Ride.** **W / S** forward / back, **A / D** turn, **Q / E** strafe, hold the **right mouse button** and move the mouse to steer,
   **mouse wheel** = camera distance, **Space** = jump. Ride round the Abbey, up its steps, across a slope: there should be no stutter
   on the steps, the camera swings behind you on turns, and nothing jerks every kilometre. Every 30 s: `Ride: 30 s - N m, avg 10.2 m/s
   (session 10.24), max step-filter gap Z m, contested 0 frames, rebases K (driven)`. **If "contested" is not 0, send the log** (the
   Somnium client fought the ride for your avatar).
6. **Action bar.** Right-click the horse in the window, then click an action button: the horse is on that key. Press the key:
   you dismount; press it again: you mount. On desktop, right-clicking the horse's buff under your portrait also dismounts.
7. **WoW's own rules** (each shows WoW's own message):
   - walk during the cast: "Interrupted";
   - fight a wolf and try to mount: "You are in combat";
   - stand inside the Abbey or an inn and mount: "Can only use outside";
   - on the horse cast a spell: "You dismount before continuing." and then the spell;
   - on the horse attack (`V` or a click on an enemy): "Can't attack while mounted.";
   - ride through the Abbey door: you are put down at the doorway (`Mounts: dismounted (Indoors)`, `Place: indoors (...)`);
   - ride in Stormwind's streets or Ironforge's halls: you stay mounted (`Place: outdoors (WMO ... WMOAreaTable flags 0x4)`);
   - talk to a flight master while mounted: "You are already mounted! Dismount first.";
   - die while mounted: you fall off, Release Spirit works.
8. **Water.** Ride into a lake (Elwynn: Crystal Lake by Goldshire): the horse swims at 4.72 yd/s (`Ride: swimming (liquid ... > ...)`).
9. **Flying** ("Fly anywhere" on). Summon the **Golden Gryphon** (Horde: **Tawny Wind Rider**), **hold Space** to take off, **W** to fly,
   hold the right mouse and tilt the camera to climb or dive, **Left Ctrl** to go down; land on a roof and on the ground (`Ride: take-off`,
   `Ride: landed`). Fly into a lake: the flying mount is dismissed (`Mounts: dismounted (Water)`), you drop into the water and sink
   to the lake bed (your Somnium avatar cannot swim on foot - walk out; VR: a short blink as you leave the saddle). Untick "Fly
   anywhere" and summon the gryphon again: "You are in the wrong zone." (WoW's own rule: no flying in the old world in 3.3.5a).
   **Tick "Fly anywhere" again afterwards**: the switch is remembered, so it would stay off in your next session.
10. **Back to the room.** Press **`K`** while riding: you get your avatar back first, then the room (`Ride: released (...) ...`).

### Getting, choosing and summoning (the parts that are ours are marked)

- **Which mounts:** offline, the testing switch gives every summonable mount of your faction, class and race from the game's own
  `Spell.dbc` (skill line 777 "Mounts"), 309 in total; 16 are not offered in this iteration (9 riding-scaled script mounts, 3 flying
  carpets, 4 Qiraji Battle Tanks - the window's footer says so). With the switch off offline you have none (there are no riding
  trainers or mount vendors offline, and the offline zones end at level 10). Online, your realm's spellbook decides.
- **The Mounts window** is WoW 3.3.5a's own Companions/Mounts pane (PetPaperDollFrame) drawn from the install's art. The big picture
  is the selected mount's icon with its name and one line **"Model: baked | learned | server | stand-in (display D)"** telling where
  the mount's 3D model number came from (baked = from your realm's database, built into the world; learned = the server told us;
  stand-in = unknown, a labelled horse / gryphon / wind rider is drawn instead). Click a mount = select it; click it again or press
  the big button = **Mount** / **Dismount**; right-click (VR: tap the name) then an action button = put it on the bar.
- **Dreamport additions** (WoW has no such buttons): the **`J`** key on desktop, the **Mounts** button on the VR palm menu (between
  World Map and Backpack), the two **testing switches**, and online the **Testing (GM)** buttons (below).
- **Summoning is casting**: WoW's 1.5 s cast bar; you must stand still, be out of combat and outdoors (AzerothCore's indoor rule,
  including the table that makes Stormwind's streets and Ironforge's halls count as outdoors); flying mounts only where flying is
  allowed (or with "Fly anywhere"). The mount's model is prepared during the cast.
- **Forced dismounts** (AzerothCore's rules): death, going indoors, water (flying mounts only), leaving a flying area, casting another
  spell on the ground (you dismount first; in the air other spells are refused with "You are mounted."). Attacking and flight masters
  are refused while mounted. A Warlock's pet goes away while you ride and comes back after.

### Riding controls

Desktop (the ride takes these keys only while you ride; nothing while you type into a text field):

| Key / mouse | Does |
|---|---|
| `W` / `S` (or the arrow keys) | forward / backwards (WoW's speeds: 11.2 or 14 yd/s on the ground; flying 17.5, 26.6 or 28.7 yd/s) |
| `A` / `D` | turn (WoW's turn rate) |
| `Q` / `E` | strafe |
| `Space` | jump; on a flying mount hold it to take off and climb |
| `Left Ctrl` | go down while flying |
| hold the right mouse button + move the mouse | steer with the mouse; while flying, tilting the camera up / down climbs / dives |
| mouse wheel | chase camera distance (2-25 m) |
| `J` | Mounts window (Dreamport addition) |
| the mount's action button, right-click on the mount buff, J > Dismount | dismount |

VR (the same rules; the view never tilts on slopes or jumps and never bobs - only your heading turns):

| Input | Does |
|---|---|
| left stick forward | half push = walk, full push = run; back = backwards |
| left stick sideways | steer (smooth, like the Euro Truck Sim truck) |
| right stick | your own Somnium snap / smooth turn - it turns the mount too |
| right **A** | jump / take off / climb (hold) |
| look up / down while flying | climb / dive (looking left or right never steers) |
| palm menu > **Mounts** | the Mounts window above your palm (Dreamport addition) |
| mount-up / dismount | a short blink (0.15 s) instead of a rise through the air |

### What other players see

Other Somnium players see you seated on the right mount, its legs moving at your speed and standing still when you stop, drawn
smoothly on one path a moment behind you (a port of the Euro Truck Sim's network smoothing), still mounted beyond 15 m. Only players
on the world's whitelist see anything of WoW. With a normal 3.3.5a client (online) your friends see the server's mount and your
movement with WoW's own flags at the server's speed.

### Online, on your AzerothCore realm

Online, the server decides everything (your spellbook, speeds, can-fly, dismounts); the world only shows it and sends your
movement. **You do not need a terminal for testing: GM automatic accounts get two Testing (GM) buttons** (Dreamport testing tools,
lead decision D13):

1. In the Dreamport: Online -> **Play online as <your Somnium nickname>** (or log in) -> Enter World with your character; stand
   **outdoors**.
2. Press **`J`**. With a GM automatic account (the server owner's GM list) the window shows a **Testing (GM)** row with two buttons:
   - **"Learn riding + mounts"** teaches the riding skills you lack (Apprentice 33388, Journeyman 33391, Expert 34090, Artisan 34091,
     Cold Weather Flying 54197) and every mount of your faction and class you do not know yet. It sends GM commands of the form
     `.player learn <your character name> <spell id>` (not `.learn <id>`: that one teaches whoever you have selected - with an NPC
     selected it answers "Player not found", with another player selected it would teach them). The commands are paced (8 at a
     time 0.2 s apart, then 2.2 s) so AzerothCore's chat flood protection never mutes you; about 260 commands take about two
     minutes. The server's answers appear as yellow messages. The Mounts window fills as the spells arrive. Log: `Online: GM command
     '.player learn ...'`, `Online: learned spell 458 'Brown Horse' (mount)`, `Online: creature 284 -> displays 2404 (learned from the
     server)`.
   - **"Go to Outland (test flying)"** sends `.tele HellfirePeninsula` (your realm's game_tele 475).
   - The row appears **only** online with a GM automatic account: on an account without GM rights AzerothCore would not run the
     command but say its text aloud to the players around you. The commands go out as a normal "say" in your faction's language
     (Common / Orcish) - AzerothCore refuses a "say" in the universal language as a possible hacking attempt, so the command would
     never run.
3. **J -> Brown Horse -> Mount**: the server's cast bar (1.5 s), then you ride at the server's speed. Logs: `Online: cast 458 ->
   SPELL_START 1500 ms -> SPELL_GO`, `Online: mounted display 2404 (server), run 11.20 yd/s acked, collision height 2.88 acked`.
   A friend with a normal 3.3.5a client sees you mounted and running smoothly (no snapping back at each heartbeat).
4. The server's own failures: mount inside a building ("Can only use outside"), walk during the cast ("Interrupted").
5. Dismount (J -> Dismount, the action button, or right-click the mount buff): `Online: dismounted (SMSG_DISMOUNT)`, speed back to 7.
6. Flying: after "Learn riding + mounts", press **"Go to Outland (test flying)"**. After the loading screen: summon the Golden Gryphon
   (Horde: Tawny Wind Rider), Space: you fly. Logs: `Online: can fly set (counter C) acked`, flight speed 17.50 acked. Back in
   Stormwind (Horde: Orgrimmar) summoning the gryphon fails with "You are in the wrong zone.".
7. Forced dismounts by the server: fly into water in Zangarmarsh (a flying mount is dismissed in water); ride a ground mount into an
   inn (the realm's indoor check is on by default). Both: `Online: dismounted (SMSG_DISMOUNT)`.
8. **Test with a NORMAL account too** (a friend's): a GM account keeps the flying flag without a flying aura, which hides bugs.

**Fallback without the buttons** (a friend's normal account, or if you prefer the console) - the worldserver console: `sudo docker
attach ac-worldserver`, press Enter, type the line (the dot is optional), detach with **Ctrl+P then Ctrl+Q** (never Ctrl+C). The
character must be ONLINE for `player learn`.
- `player learn NAME 33388` (Apprentice Riding), then `player learn NAME 458` (Brown Horse) or `player learn NAME 580` (Timber Wolf, Horde).
- Flying: `player learn NAME 34090` (Expert Riding), `player learn NAME 32235` (Golden Gryphon; Horde `32243` Tawny Wind Rider),
  optionally `character level NAME 60`, then `teleport name NAME HellfirePeninsula`. Back: `teleport name NAME Stormwind` (Horde: `Orgrimmar`).
- Water dismount: `teleport name NAME Zangarmarsh` and fly into the water.
- Northrend: `player learn NAME 54197` (Cold Weather Flying) + a Northrend `teleport name` of your choice from the realm's game_tele table.
- Take a mount back: `player unlearn NAME 458`.
- Do not teach vehicle mounts (flying carpets, Traveler's Tundra Mammoth ...) or the Qiraji tanks yet: they are logged and not ridden.

### In the headset (offline, then online)

The same steps with the VR controls above. Check: a short blink when you mount and dismount; the view never tilts on slopes or
jumps and never bobs; looking around does not steer; leaning in your room moves your view up to 0.45 m and no further; standing up
re-rests it; your virtual hands stay on your real hands after mounting. Logs: `Ride: VR seat - pose entry turn N deg undone, hands
reset; eye E m above the saddle; lean clamp 0.45 m`, `Ride: client turn read back: N snaps`. **If you snap-turned but the log says
0 snaps, tell us** (there is a switch to steer with the left stick only).

### Two Somnium players (offline or online)

One rides, the other watches: seated on the right mount, the legs moving at the rider's speed, standing still when the rider stops,
gliding smoothly at a gallop, still mounted beyond 15 m. Viewer log: `Gear: 'Name' (player N) mounted display 2404 (ground), gait 5 x
1.24, anchor relative (...)` and every 120 s `Gear: ride smoothing - ... correction avg C m max M m` / `Gear: ride jitter 'Name': ...`.
(Untested until two clients ride together.)

### What was tested (2026-09-27) and what not

- **Harness (the real game files, pure code):** mount catalogue 27 checks, flying-area rules 14, model table 19, the offline rules 58
  (every cast check and forced dismount, speeds, the taxi refusal, death, the Warlock's pet), the riding motion 164 (11.2000 yd/s at
  every frame rate, ramps, turning, jumps, slopes, stairs), blend times 170, gaits 41, indoor/outdoor table 18, saddle maths 21, the
  network record 32, the smoothing timeline 40, mount footsteps 48, online mounts against a fake AzerothCore 69, plus the older
  online login 26, online world 28 and automatic accounts 50 - all pass.
- **Render lab (the real WowGame, offline, a Human Paladin; no Somnium client, no headset):** the Mounts window (J) -> Mount -> the
  1.5 s cast -> mounted on the Brown Horse (model baked, display 2404) -> the chase camera; up the Abbey steps (largest step-filter
  gap 0.077 m) and through the door -> dismounted "Indoors" in the Abbey's Main Hall -> mounting there: "Can only use outside"; a
  gallop at 10.24 m/s = WoW's 11.2 yd/s; the mount's own spell while riding dismounts; the Golden Gryphon takes off, hovers with the
  rider on its back (not sunk into the body), flies at WoW's 17.5 yd/s and lands; "Fly anywhere" off -> "You are in the wrong zone.";
  Stormwind's street group 'MB02' -> outdoors (WMOAreaTable 0x4) -> mounting works; the Goldshire inn's kitchen -> indoors -> "Can only
  use outside". Also another player's rider seated on a galloping horse, the Testing (GM) row, the Settings rows and the VR palm menu
  with the Mounts button. The lab's stand-in avatar has no Somnium sitting pose, so it stands on the saddle in the pictures.
- **Not tested anywhere yet:** the Somnium client (sitting pose, hips / eye measurement, taking the avatar over, the chase camera
  with the client's real camera), a headset (all VR riding), two Somnium players riding together, a real AzerothCore server. Each
  has a log line above so your Player.log answers it.

## Online (AzerothCore) - iteration 6, stage O1 (2026-09-27)

The real spawns (where every NPC stands, what it is called, where it walks) exist only in a server's database, so the
Dreamport does what the real client does: it gets the world's creatures, NPCs, players and objects from a WoW 3.3.5a server
while you play. The world still ships none of it. The server is **your own AzerothCore 3.3.5a realm** (build 12340).

**1. The server (once).** `ServerKit/` in the project folder (next to `Assets`, not uploaded with the world) holds
`setup-server.sh`, `status.sh`, `connect.md` and a step-by-step `README.md`. In short: make a DigitalOcean droplet (Ubuntu
24.04 x64, 4 GB RAM / 2 vCPUs recommended, your SSH key `~/.ssh/wow_droplet`) or an Oracle Cloud Always Free ARM server, then
either give Claude the IP (it runs the commands in `connect.md`) or copy the two scripts up and run
`sudo PUBLIC_IP=<IP> bash setup-server.sh`. It installs Docker, opens ports 22 / 3724 / 8085, starts AzerothCore with
**Warden off**, keeps MySQL and SOAP on the server itself (localhost only), sets the realm address to your public IP and ends
with `SETUP OK` (or `SETUP FAILED` + the log `/var/log/wow-setup.log`). `status.sh` prints `STATUS OK` / `STATUS PROBLEM`.

**2. Your account (once, you, not Claude).** `sudo docker attach ac-worldserver`, Enter, then
`account create NAME PASSWORD` (name up to 17 characters, password up to 16, neither case-sensitive) and optionally
`account set gmlevel NAME 3 -1`; detach with **Ctrl+P, Ctrl+Q** (Ctrl+C would stop the server). Friends get their own
accounts; with a normal 3.3.5a client they `set realmlist <IP>` in `Data\<locale>\realmlist.wtf`. No two-factor on the accounts.
Since iteration 6.1 Somnium players can skip this step: see "Automatic accounts" below.

**3. Connect in the Dreamport.** In the room, Play tab, press **Online** (right of the tabs; **Offline** brings back the
saved offline characters - the choice is remembered). Since iteration 6.1 the Online tab opens on **Play online as <your
nickname>** (next section); the form below is behind **Use a different account**. Enter **Server** (the IP or host name; `host:port` if the login port is
not 3724), **Account** and **Password**, press **Login**. In the headset, pointing at (or poking) a field opens a WoW-styled
**on-screen keyboard** below your view (letters, digits, `- . @ _`, a symbols page, Shift, Space, Back, Enter, Hide); on
desktop you type into the fields (Enter moves to the next one). "Remember account name" keeps only the name. **The password
goes only to your login server: it is never saved and never written to the log**; the field empties the moment you press
Login. Errors show in red under the fields and on the status line (wrong name or password, server not reachable, ...).
- More than one realm: pick one. One realm is picked for you.
- **Your characters on the server** (the offline list's look): **Enter World**, **Delete** (asks first), **New character**
  (our faction -> race -> class screens, then a name: your Somnium nickname cleaned to WoW's name rules - letters only, 2-12,
  no letter three times in a row - editable, male / female; the server's answer, e.g. "That name is already taken.", shows in
  red), **Log out**.

**4. In the world.** The map's loading screen, then you stand where the server says your character is, facing the same way.
Every creature, NPC, other player and game object the server sends is drawn at its real place: names and sub-names ("Flight
Master"), levels, health, friendly / neutral / hostile from your faction and reputation, quest markers (! / ?), NPC weapons,
sitting / sleeping / kneeling / dead NPCs and their work emotes, other players with their race, looks, gear, weapons (sheathed
or drawn) and mounts, game objects (mailboxes, doors open / closed, chairs, ...) with their full rotation. NPCs walk and fly
the paths the server sends. Your avatar is your character: its position goes to the server (start, stop, heartbeat, facing,
jumps), so everyone else - other Dreamport players and normal 3.3.5a clients - sees you move. Clicking a creature or player
targets it (the server gets the selection); using game objects comes in a later stage. A server teleport (e.g. a GM `.tele`) moves you;
to another map it shows that map's loading screen and loads it. **K** / Settings > Return to the room (stage S1; the wrist button until then) / leaving the world logs the
character out (AzerothCore waits 20 s unless you are resting in an inn or city) and returns to the character list. A lost
connection returns you to the room with the reason.
- **Several Somnium players**: each logs in with their own account. You see the other Somnium players' avatars, not their WoW
  characters twice (your network record carries your character's id and a hash of its name, and the others hide that
  character). Normal-client players are drawn as WoW characters.
- **Warden**: the Dreamport never answers Warden. If the realm has it on, the log says
  `server has Warden on - switch it off (see ServerKit)`; ServerKit switches it off.

**Since iteration 8 (stage O2)** attacking, spells, auras, items, loot, death, quests, talking to NPCs and using chests / herbs /
ore / quest objects work online - see "Online: combat, items, death, quests (stage O2)" above. Still "Not available online yet":
vendors, trainers, flight masters, bankers, the mailbox, readable items, gossip options; chat and groups (stage O3b).

**Not online yet (stage O1)** - these answer "Not available online yet" instead of pretending: attacking, spells, auras,
loot, death (stage O2); talking to NPCs (gossip), quests, vendors, trainers, flight masters, using game objects, chat,
party (stage O3). Also:
ships and zeppelins stay where they appeared (no route animation yet); swimming and falling off ledges are not reported to
the server; NPC weapons are always in hand; helmets and shoulders are not drawn on other players.

## Automatic accounts - iteration 6.1, stage A1 (2026-09-27)

**For players.** Online tab -> **Play online as <your Somnium nickname>**. The server is the one in Settings (new default
`145.241.232.229`, the user's realm; a server you saved before wins). First press: "First time: your WoW account S... is made
for you on this server." - the world makes a random 16-character key on this computer, asks the server's **account service**
(port 8086 on the same server) to create the account, stores the key in your own Somnium storage for this world (two copies)
and logs in; from there it is the normal online flow (realm, your characters, Enter World). Next presses log in with the stored
key. The account name is `S` + the first 16 hex digits of your Somnium ID (Shodah: `SCF5C553C61F843F4`). Plain messages when
something is in the way: not on the server's allow list ("This server doesn't know your Somnium account yet - ask the server
owner to add you."), the account exists but this computer has no key ("... Ask the server owner to unlock it (account S...),
then press Play online again."), too many tries, the service not reachable ("Can't reach the account service on <server>
(port 8086)."), a different program answering on 8086. In the editor there is no Somnium ID: the button is greyed ("Only in the
Somnium client: your Somnium account makes the WoW account.").
- **Settings > Online** shows the server, the account and "Key on this computer: saved (hidden) / none", and **Forget this
  computer's key** (asks first). After that - or on another computer - the account works again only after the server owner
  unlocks it and you press Play online.
- A key the server no longer accepts (the realm was reset, or the owner unlocked the account) makes the world ask the account
  service once more with a new key; characters on the server are never touched.
- **Use a different account** brings back the old form (server, account, password) for any normal account; **Use my Somnium
  account** goes back.
- Safety: the key is never shown, logged or sent anywhere - only the two one-way SRP6 values AzerothCore stores for every
  account (salt + verifier) go to the account service. The Somnium ID cannot be the password: every Somnium world can read it.
  The account name (made from that public ID) does appear in the log.

**For the server owner** (`ServerKit/README.md` section 8, in plain words): install once with `setup-accounts.sh` (ends with
`ACCOUNTS OK`), open TCP 8086 in Oracle's security list, then double-click **`ServerKit/Manage WoW accounts.cmd`** for a menu:
allow a player (Somnium ID or nickname), unlock an account for a new computer (valid 24 h, one re-key), make a GM, list accounts
and players turned away recently, create a normal account for a friend with a normal WoW client. The service only serves players
on `/etc/wow-accounts/allow.txt` (initially Shodah, Wildy and Shodah's Somnium ID), makes GMs only from `gm.txt` (Shodah), never
overwrites an existing account, and derives the name itself. Note: a **nickname** on the allow list lets in only the first
Somnium account that registers with it (anyone can send any nickname); add a player's Somnium ID to let in more.

**Tested on this PC only** (no agent connects to the server): the whole flow against a fake account service + fake AzerothCore
(50 checks: created -> the generated key logs in to the character list, exists, unlock -> rekeyed, not allowed, slow down,
garbage answers, closed port); the Online tab's decisions with the real key store (16 + 44 checks); and the REAL python account
service (`--dry-run`, 127.0.0.1) against the world's real client (57 checks: every answer of the contract byte for byte, the
SQL it would run, python and C# computing the same names and SRP6 verifiers for 87 inputs). The first real use is the first
"Play online" after the server owner ran `setup-accounts.sh`.

## Where it stands (iteration 4 stage A, 2026-09-26)

The integration run drove the real `WowGame` with a Human Priest: Northshire (grass, critters, birds; a heal on a friendly
NPC; low health + party frames; the VR radial menu and far ray placed by hand), then the Stormwind flight master, the
flight map, the flight to Ironforge with the world re-centred 4 times on the way, the landing, and the VR HUD + palm menu.
VR pieces were placed by hand in the lab (no headset there). **Nothing of iteration 4 has run in the Somnium client, a
headset or with two players yet.**

| Feature | State |
|---|---|
| Flight masters at the taxi nodes of your faction (map 0: 18 Alliance / 14 Horde; map 1: 19 / 21; map 530: 23 / 21), node discovery, known nodes saved | works in lab (Stormwind: "Kelgan Deepbarrel <Flight Master>" on the gryphon platform); harness. Ironforge's master stood on the mountain above the city (the terrain there is at z 770, the stand at 502) - fixed after the last lab run (a master now stands at the node's height when the terrain is more than 8 yards above it), not re-rendered |
| Flight map (desktop panel / VR world panel): nodes, route lines, name + cost tooltip, click to fly | works in lab (desktop: 18 nodes, Ironforge 50c hovered, route line); VR panel only in the flights owner's lab |
| Taking a flight: fare, the game's route, taxi mount, level view, streaming ahead, landing, FinishTaxi | works in lab (Stormwind -> Ironforge, path 13, gryphon 6852, 6519 yd, landed at the Ironforge roost, controller back on) |
| Floating origin: re-centre every 1 km (500 m up/down), 256-yard grid, all world objects + effects + grass follow | works in lab (4 re-centres during the flight, 4-12 ms each; the world near Ironforge drawn normally 4253 yd from the start) |
| Other players drawn in your frame (their origin in the network record), hidden on other maps / in the room / beyond 1.5 km, remote mounts | built; offline wire test 21 passed; needs two players in the client |
| VR half-radial menus (grip, twist, release) in WoW action-bar art | works in lab (placed by hand: 6 slots, Smite highlighted with cost, cast time and "Invalid target") |
| Immersive casting (push to fire), aim assist | built; lab: held spell + release by the VR input owner; the push speed needs a headset |
| Far VR target ray (150 m default, terrain blocks) | works in lab (Young Wolf at 30.8 m; VR input owner's lab: 33 m) |
| Palm menu, floating VR HUD with party + target, forearm bar off by default | works in lab (placed by hand in Ironforge: HUD with player + 2 party members, palm menu; no target there - see the Ironforge master; target frame in the UI owner's lab) |
| Red view edges at 20 % health (VR + desktop) | works in lab (desktop at 11/72 health; VR overlay placed by hand) |
| Heals and buffs on friendly NPCs | works in lab (Power Word: Fortitude + Lesser Heal +47 on Corwin Hale) |
| Grass / flowers / bushes (GroundEffect data), critters, bird flocks | works in lab (Northshire) |
| Desktop party frames, target cast bar | works in lab (party frames with a hostile target; the target cast bar only in the UI owner's lab) |

## Where it stands - iteration 3 (unchanged)

"works in lab" = run in the render lab (a throwaway copy of this project in Unity batchmode with the Somnium client's URP
settings) and the pictures looked at; the integration run drove the real `WowGame` end to end (faction -> race -> class
screens, then one new character per starting zone: loading -> arrival -> NPCs and creatures -> back to the room, plus a
fight with the desktop avatar, a Fireball, a burning torch and the settings window). "harness" = pure code run against
the real game files outside Unity. **Nothing of iteration 3 has run in the Somnium client or a headset yet.**

| Feature | State |
|---|---|
| New character: faction -> race -> class, race icons/descriptions/racials from the game files, classes per CharBaseInfo | works in lab |
| Character list with race + class, "Level N Race Class", zone; iteration-2 saves load (as Night Elves) | works in lab; harness |
| The 8 starting zones: arrival at the race's real start (playercreateinfo), creatures, NPCs, quest givers | works in lab (all 8 arrived; 60-70 entities each, every model built); harness: 1500-1740 content checks per zone pass, every spawn on the ground |
| Deathknell start inside the crypt, Ammen Vale start on the wrecked platform | works in lab (crypt floor; the platform waits for its building - fixed in this iteration) |
| Quests per zone (our own text), class quests, class start kits (Gnome kits separate from Dwarf) | harness (XP estimate: level 5.6-6 per zone; every class of Ammen Vale / Sunstrider accepted and handed in its quests); not played in the lab |
| All 9 classes 1-10, racials, imp pet, totems, seals, channels, dispels, creature spells, stun/fear/polymorph | harness (52 race/class pairs kill a level-1 creature; all 9 classes at level 10 win 27 of 27 fights); Fireball + warrior melee in the lab |
| NPC names above their heads (measured head height per model) | works in lab after the fix in this iteration (the lab's first run showed armed NPCs with the plate in the chest) |
| Particle effects and ribbons on world props and creatures, M2 colour/transparency/texture animation | works in lab (torches, braziers, candles in the zones, own SomniumWoW/Particle shader) |
| Spell visuals: precast/cast/channel kits, missiles, impacts, aura kits, level-up | works in lab (Fireball impact on a live creature; VFX owner's run: precast, missiles, Shield, level-up) |
| Sound: spells, weapon swing/impact, UI, quest/level/loot, NPC greetings, race error voices, footsteps by ground, zone music + ambience | harness (3726 sound files decoded, 5 zone MP3s missing in the game's own MPQs); **never heard** (the lab has no audio) |
| Settings window (desktop key O, arm panel button, room tab) and saving per account | works in lab (window, sliders, Defaults); saving in the editor store only |
| Desktop avatar combat animations (swing, stab, punch, bow, cast) and sync to others | works in lab on Somnium's template humanoid, driven by the real session's swings; real avatars, walking blend and two players untested |
| Pet frame + pet bar, totem timers, racials in the spellbook, green channel bar | works in lab (UI owner: Orc Warlock with Imp); totem timers never drawn |
| Shared monsters, server, login | missing (next iterations) |

Iteration 2 (offline Shadowglen; everything still applies to all zones):

| Feature | State |
|---|---|
| Room tabs Play / Sightseeing; character list (Play, Delete + confirm, up to 10) | works in lab |
| Class creation screen: 5 classes, WoW icons, names, colours and descriptions from the game files | works in lab |
| Saves (2 copies, checksum, salvage of damaged copies, newer format never overwritten) | works in lab in the editor's memory store; harness: 9 damaged-save cases pass; the client's LocalStorage untested |
| Kalimdor loading screen, arrival at the saved spot and facing, zone light | works in lab (0.5 s to Shadowglen) |
| Shadowglen content: 69 spawns (12 NPCs), 14 quests, 56 items, all standing on the terrain | works in lab; harness: 1515 checks pass |
| Creatures and NPCs drawn: WoW models, NPC weapons, stand/walk/run/attack/wound/death animations, corpse pose | works in lab (69 of 69 models built) |
| Nameplates, quest markers "!"/"?", selection circle, loot sparkle, floating combat text | works in lab |
| Offline rules: melee on the swing timer, rage/energy/mana, combo points, GCD, cooldowns, casts, ranges, auras, forms, stealth | works in lab (Warrior L1 auto-attack vs a level-1 Young Nightsaber: 43 of 50 damage in 10 s, dead at ~13 s, +50 XP, rage 51); harness: every class checked |
| Class spells from Spell.dbc (names, icons, tooltips), new spells on level-up | harness (quest bot to level 5 for every class); spellbook works in lab |
| Quest dialogs, quest log, rewards, loot window, bags, equipping | works in lab (the UI owner drove the quest flow and Loot All through the UI's buttons) |
| Death -> Release Spirit -> graveyard | works in lab (front owner's run) |
| Desktop HUD (WoW's own art: unit frames, action bar, XP bar, windows, tooltips) and keys | HUD works in lab; keys built untested |
| VR left-arm panel + windows as a lid, ray + trigger, poke | works in lab on a mock arm (ray/poke hit tests pass); headset untested |
| Clicking creatures/NPCs (desktop pointer, VR trigger) | built untested (the lab targets through code) |
| Creature sounds (attack, wound, death, aggro) | built untested (62 sounds decoded in the lab, not heard) |
| Weapons sheathed on the avatar by SheatheType (back / hip) | works in lab on Somnium's template humanoid; on a real Somnium avatar untested |
| VR grab/draw/put back, swing -> auto-attack, haptics; desktop Z | built untested |
| Other players' weapons + nameplates over 50 network slots | built untested (never run with two players) |
| Shared monsters, server, login | missing (next iterations) |

Iteration 1 (sightseeing, still in the Sightseeing tab):

| Feature | State |
|---|---|
| Reading the game from the player's install (MPQ, DBC, BLP, install search, enUS) | works in lab (harness: full census of ~204k files, 0 failures) |
| Black room + WoW panel, 15 locations, time of day, line-up toggle, status line | works in lab; **works in the client** (user test 2026-09-26) |
| Terrain, water, buildings with interiors, trees and props, sky/fog/light | works in lab; **works in the client** ("it all loads in and is AMAZING") |
| Model line-up | works in lab |
| Far terrain (WDL horizon), legacy MCLQ water, WMO liquids | built untested |
| Collision, streaming while walking, fall guard, K key, VR wrist | built untested (not reported on yet) |

## Iterations

| # | Date | Summary |
|---|---|---|
| 1 | 2026-09-25 | Offline world test: black room + location/time picker, WoW loading screen, real WoW terrain/buildings/doodads/water/sky/light read from the player's 3.3.5a install, model line-up (characters, creatures, weapons), K / wrist back to the room. No server. |
| 2 | 2026-09-26 | Offline play test: Play tab with saved characters + class creation screen (5 Night Elf classes, the Somnium avatar is the character), Shadowglen with 69 creatures/NPCs, 10 quests per character and the WotLK rules run locally (levels 1-10, spells from the game files), left-arm UI in VR / WoW HUD on desktop, weapons on the avatar (VR grab + swing to attack, Z to draw) synced to other players, LocalStorage saves per account. Monsters per player; no server. |
| 3 | 2026-09-26 | Offline play test of all races: new character = faction -> race (the 10 races of 3.3.5a, racials from the game files) -> class, the Somnium avatar stays the character; each race starts in its own starting zone (8 zones) with creatures, NPCs and our own quests (levels 1-10); all 9 classes incl. the imp, totems, seals; particle effects and spell visuals from the game data; spell/weapon/UI/NPC sounds, footsteps, zone music and ambience; NPC names above heads; Settings window saved per account; desktop avatar combat animations synced to others. Monsters per player; no server. |
| 4A | 2026-09-26 | Systems: flight paths and flight masters from the game's taxi data (flight map, riding the route on the taxi mount); floating origin (the world re-centres every km: no wobble far away; other players drawn in your frame via the network record, hidden on other maps/beyond 1.5 km, remote mounts); VR half-radial spell menus (grip, twist, release) + Immersive Mode (push to fire); palm menu; floating VR HUD with party + target; red view edges at 20 % health; heals/buffs on friendly NPCs; far VR target ray; grass, critters and birds. Monsters per player; no server. |
| 5 | 2026-09-26 | WoW Map: a live overhead map (orthographic camera above the player, 1024 px, 12 renders/s only while open, fog off, roofs/treetops above you cut away) with WoW's markers (you, other players, quest ! / ?, flight masters, hostiles, target), zone name + WoW coordinates, zoom 60-600 m, rotate, recenter, Classic view with the game's minimap tiles; opened from a "WoW Map" app on the Somnium tablet (beside Somnium's own Map app), the VR palm menu's World Map button, or `N` / a micro button on desktop. Monsters per player; no server. |
| 6 (O1) | 2026-09-27 | Online stage O1: an Offline / Online switch on the Play tab; login to your own AzerothCore 3.3.5a realm (own SRP6 / auth / world protocol client for build 12340, Warden never answered; ServerKit sets up the realm on a droplet with Warden off); VR on-screen keyboard; realm list, the server's characters (enter, delete, create through our faction / race / class screens + a WoW name from the Somnium nickname), enter the world: every creature, NPC, player and game object from the server at its real position (names, levels, reactions, quest markers, NPC weapons, stand states and emotes, players' looks, gear, mounts, doors), server movement splines, our movement to the server, near / far teleports, logout; Somnium players' own WoW characters hidden behind their avatars. Combat, spells, loot, quests, gossip, vendors, chat online: stages O2 / O3. Offline unchanged. |
| 6.1 (A1) | 2026-09-27 | Automatic accounts: "Play online as <Somnium nickname>" on the Online tab - the WoW account is made on the first press by the server's account service (ServerKit `setup-accounts.sh`, port 8086, allow list + GM list, admin menu `Manage WoW accounts.cmd`), name `S` + 16 hex digits of the Somnium ID, a hidden random 16-character key kept only in the player's Somnium storage (only the SRP6 salt + verifier are sent); Settings > Online "Forget this computer's key"; unlock by the server owner for a new computer; the old form under "Use a different account"; default server 145.241.232.229; the login accepts 17-character account names (AzerothCore's limit). |
| 7 | 2026-09-27 | Mounts, offline and online: Settings > Gameplay "Unlock all mounts (testing)" + "Fly anywhere (testing)" (on by default, remembered; the mounts they give are never saved: every mount of the faction / class / race from Spell.dbc line 777, riding 300 + Cold Weather Flying); WoW's Mounts pane (desktop J, VR palm Mounts button - Dreamport additions); summoning = WoW's 1.5 s cast with AzerothCore's rules and WoW's texts (outdoors incl. WMOAreaTable, flying areas, every forced dismount, mounted attack / flight master refused); models baked from the user's realm (289 entries) or learned online; the ride the Euro Truck Sim way (avatar taken over like the taxi, our proxy controller at WoW's speeds, heading-only view, stairs filter, jumps, swimming, flying, desktop chase camera, VR sticks + blink); gaits with WoW's blend times, mount footsteps / wing flaps; other Somnium players see the rider on the mount, smoothed (Gear record ints 17-27); online: the server's spellbook, mount casts, auras, dismounts, speed / can-fly / collision acks, WoW's movement flags, Testing (GM) buttons for GM automatic accounts; the taxi on the new seat + chase camera. |
| 8 (O2) | 2026-09-27 | Online stage O2 on your own AzerothCore realm (the server is the only truth): real gear and bags from the server's item objects + item queries (WoW tooltips, equip / use / food / Hearthstone / quest items, WoW's inventory errors, looted items); melee auto-attack with WoW's checks and texts, every unit's swings and wounds animated with their sounds, XP, level-ups, kills, death; every class spell castable (targets per spell, cast bars, channels, cooldowns + GCD, auras on every unit, the server's action bar, immersive VR casts sent at the push); every caster's (NPCs, normal WoW players, Somnium characters) precast, missile, impact and sounds; loot windows + auto loot; death WoW's way online and offline (Release Spirit, ghost at the graveyard, ghost-world look + music, corpse arrow / marker / WoW Map icon, Resurrect within 39 yd after 30/60/120 s, spirit healer + Resurrection Sickness, resurrection by others online); quests and NPC talk online (greetings, details, rewards, accept, hand in, abandon, quest log, progress messages, chests / herbs / ore / posters); the Somnium hybrid (Gear record ints 28-30: casts, channels, held spells on the avatar's hands; online server effects re-anchored to the avatar); lead: WoW run speed on foot, VR windows close when walking away, the tablet guard. Vendors / trainers / flight masters / mail / chat / groups online: O3b. |
| 9 (S1) | 2026-09-27 | Stage S1, social: chat online (every chat type from the realm incl. NPC speech and system lines, say / yell / emote / party / guild / whisper out in the faction language with AzerothCore's rules for the pipe character and the 255-byte limit; WoW's ChatFrame1 bottom left on desktop, Enter or / to type, WoW's slash commands + /invite, a VR Chat window from the palm menu with Say / Party / Yell / Emote / Reply and the headset keyboard; WoW-style chat bubbles over speakers, over the Somnium avatar for a Dreamport player); groups online (invite, WoW's invite popup, accept / decline / leave, the server group in the party frames with live stats, leader, dead / ghost / offline, Somnium profile pictures from somniumspace.org, the unit menu, a VR Party menu); Settings > HUD (size / place, desktop and VR); clickable / pointable party frames to target a friend; a WoW focus frame + focus key (short X in VR, H on desktop, three presses clear); far targeting and our UI pointer along the Somnium laser (our ray line and yellow dot gone; nameplate glow, soft hover glow, far hover tick); Return to the room in Settings (press twice; wrist button and Press K text gone); the other Somnium players' name tags hidden (Bridge SetPlayerTagDisableState, first use; proven fallback built in); Fusion session diagnostics + a one-time alone hint; immersive casting starts the cast at once and waits for the push on a realm with ServerKit mod-dreamport-hold (only VR Immersive players are ever held), instants at once, 30 s time-out everywhere; +10 % damage on spells released with Immersive casting and the buff "Immersive Mode" (a Dreamport house rule, server-applied online); the game folder browser (Settings > Game). |
| 14.7 | 2026-11-06 | "GingasVR" whitelisted (Core/WowAccess.cs) and allowed on the realm's automatic accounts. |
| 14.6 | 2026-11-05 | "Can't do that while moving": floating still in water (or hovering) no longer counts as moving - OnlineSession.SpMovingNow follows AC isMoving (MOVEMENTFLAG_MASK_MOVING, never SWIMMING / FLYING alone; Player (34).log "cast 331 refused here"). On foot, a slide down a slope with no movement input (feet sinking, no W A S D / arrows / VR left stick) is not moving (WowGame.FootMoving) - mining, gathering and casts work on slopes. |
| 14.5 | 2026-11-04 | VR spell menus default to Gear Select (WowSettings.VrMenuStyle 1, also Defaults); saved settings move to it once (key gear_select_default_144), Twist wrist stays selectable. |
| 14.4 | 2026-11-04 | Remote full armour from the WEARER's own measurement: ArmourShell.ExportShape (thickness of 8 body parts, feet tip, torso profile; 150 bits) in the spare high bits of record ints 33-44 (WowNetWire.ShapeChunks; round-trip tested 2000/2000), viewers ApplyWearerShape and never measure the remote copy (Player (34/35).log: the stand-in / seated waits hid Ydliw's armour for good, remote measurements read chest 3.00/3.00 with the torso centre 34 m off). Measuring itself skips meshes more than 3 m from the hips and vertices too far from their bone. Both players need 14.4. |
| 14.3 | 2026-11-04 | "Shodah Dev" (the user's stage account), "Destos", "Ydliw" and 16 StageUser test accounts (3-9, 11-17, 19, 65) whitelisted; the game folder is also looked for at `C:\Users\XR Lab\Desktop\World of Warcraft 3.3.5a` (WowInstall.KnownPaths). Realm automatic accounts now also allow Shodah Dev, ꨄ Rage Quit ꨄ, denevraut, Destos, Ydliw and the 16 StageUsers. |
| 14.2 | 2026-11-04 | "Hide avatar part" checks the BUILT armour: a part hides only when every body-texture area of it is painted 60 %+ (ArmourShellLook.Cover) or a 3D piece covers it (gloves, boots, robe). 14.1 still hid the forearms under a robe (Player.log "chest, arms, legs"; measured NE druid robe: arm upper 40 %, arm lower 0 %). |
| 14.1 | 2026-11-03 | The menu always opens on "Log In with Somnium Account" (the saved "different details" choice is no longer reused). Armour "Hide avatar part" only hides a body part when that slot has something on (chest: chest / shirt / tabard; waist: belt; arms: bracers or chest / shirt sleeves; hands: gloves; legs: trousers or a robe; feet: boots) - ArmourShell.Worn. Status line without "sightseeing"; version date 2026-11-03. |
| 14.0 | 2026-11-03 | Menu: a big "Log In with Somnium Account" button in the middle (the nickname on the line under it); bottom row "Log in with different details" (on the form: "Log In with Somnium Account") and "Settings" / "Back". Hidden (code kept, WowRoom.ShowOldTabs): Play / Sightseeing tabs, Offline / Online switch (online forced), time-of-day row. Online the world's time of day is the realm's clock (SMSG_LOGIN_SETTIMESPEED, real time; the realm runs on UTC) - sun, sky, fog and water move with it. |
| 13.9 | 2026-11-03 | Quest dialog rewards (and mail attachments) showed "Item N" when the name arrived after the window opened - its layout signature now includes the item names (the quest log has done so since 13.7). |
| 13.8 | 2026-10-30 | The WoW lobby (Main Build DreamPortAreas/WOW_Lobby, 63 assets copied to Assets/WowLobby with their GUIDs + 3 Enviro 3 fog includes at their own path) merged into the scene under the inactive root "WoW Lobby" (WowLobby.cs), its baked lighting kept (scene .meta GUID = the lobby scene's 9e2d97d7...). Whitelisted players spawn on its star (measured centre (0, 0, -5.70) of the model = LoginRoomOrigin) facing the menu, which stands fixed in the door (x 4.0, z -12.33, 2.3 m wide); lobby skybox + ambient while in the menu; hidden in the world; no WotLK login music while it stands (the user added fire / wind AudioSources to WOW_LobbyAssets.prefab). Visitors: the plain plane as before. Backup + tools: D:/Dreamport/WoW/MigrationBackups/2026-10-30_lobby. |
| 13.7 | 2026-10-29 | VR windows you open (bags, quest log, spellbook, character...) no longer close when you move (the 2026-09-27 move-close rule removed). VR world windows (quest dialog, popups, free bags / quest log lid, chat, map, flight map, group / instance / dungeon finder popups) move with the player's rig, and turn with it while a flight / mount / ship steers (UiCarry). Quest log: quests under zone / category headers that fold (online ZoneOrSort via AreaTable / QuestSort.dbc, offline the zone's name), the selected quest in a scrolling pane with full text, objectives, reward icons + tooltips, spell, experience, money. Avatar Magica cloth reset after a jump of 8 m+ in a frame (rebase), plus an avatar report in the log 1.5 s after each rebase. |
| 13.6 | 2026-10-26 | Swim (42) while moving, SwimIdle (41) when still - 13.5 had them swapped. |
| 13.5 | 2026-10-26 | WoW swim animations on the Somnium avatar: full body on desktop, kicking legs in VR; seen by others. |
| 13.4 | 2026-10-26 | On-foot swimming (Somnium avatar, terrain + WMO water), breath / fatigue bars, water reflections + "Water visibility", meeting stone portals, strike ring on hostile flagged NPCs. |
| 13.3 | 2026-10-21 | Photon Fusion 2.1.3 (Somnium's migration tool); networking code updated for 2.1. |
| 13.2 | 2026-10-14 | Outland flights from the Dark Portal and other one-way / shared paths; invisible trigger units no longer named or clickable. |
| 13.1 | 2026-10-12 | VR ground spells and items place again (dynamite), item throws animate; macros (Settings > Macros, /cast /use /target ...). |
| 13.0 | 2026-10-12 | Quests on city maps; hide main hand / off hand / ranged weapon; other players' level-up effect. |
| 12.9 | 2026-10-11 | Login area back to the black room (the login scene / sky removed). VR bag slots and the rest of 12.6-12.8 unchanged. |
| 12.8 | 2026-10-11 | Login area: only WotLK's login sky and clouds, far away, on an invisible platform. |
| 12.7 | 2026-10-11 | WoW's WotLK login scene (mountain, Icecrown, frost wyrm) round the login area. |
| 12.6 | 2026-10-11 | VR bag slots under the open bags (equip / unequip by drag and drop); bag portraits visible. |
| 12.5 | 2026-10-11 | Trainer cast animation plays once; profession recipes show the item they make (icon + tooltip). |
| 12.4 | 2026-10-11 | Held immersive spells loop their precast effects and sound until released (you and other players). Upload fix: no Span types (server compile). |
| 12.3 | 2026-10-10 | Easier weapon grabs (either hand, behind the back / near the hips, wands on the hip); immersive bow with a column of shots; bowstrings with bending limbs. |
| 12.2 | 2026-10-10 | Desktop quest tracker left of the right bars; Quest text size (VR); Sharp VR interface (after DLSS). |
| 12.1 | 2026-10-10 | Gear Select's own bar of six boxed buttons; VR bags to the right of the Character window; spellbook tab icons. |
| 12.0 | 2026-10-10 | Deck rescue on ships / zeppelins, AH money boxes readable, VR nested buttons (mail), VR ground spells, bags beside the Character window, the avatar in the Character window, Gear Select spell menus, Outland skyboxes, stuck ground effects. |
| 11.9 | 2026-10-04 | No huge legs in full body tracking: an arm / leg measured at the limit (trackers mid-calibration) is repaired and measured again. |
| 11.8 | 2026-10-04 | No falling through the world after a hearthstone or any teleport into a place that has not loaded yet (held until its floor is in); other players see your Full armour fitted after you walk away and back (the client's far-away stand-in is no longer measured); the cloak stays visible in VR (only its collar folded); other players sit on their flight griffon (the flight sends the griffon's place like a ridden mount); others see your face in the helm, helm at its size; no armour popping near others; VR eating / drinking kept, Immersive eating (sit down in real life); XP bar on the VR palm menu; VR Accept without scrolling the quest text; inn / tavern / building music; armour top / bottom edges, robe skirt and boot defaults fixed. |
| 11.7 | 2026-10-04 | Armour Taper top / bottom, the waist and chest sized about the body's middle, Hide avatar part per armour piece, WoW's BuffFrame top right, Shift + loot, avatars reflect the zone sky, upright spell light columns, complete item tooltips (random suffixes), duels accepted on desktop, missing sounds, the tram ambience. |
| 11.6 | 2026-10-04 | The full armour measures only a standing avatar (no huge boots after logging in dead), the Armour page's Save writes at once. |
| 11.5 | 2026-10-04 | Stormwind cathedral slabs gone (the client's WMO portal rule), Full armour + WoW full-body animations by default, Defaults keeps the armour fit, no vibrating on griffons, reward-choice and delivery quest hand-ins, trainer spam settles, the Need/Greed icon, dead players lie down, sit / eat / drink / kneel / sleep poses, resurrecting other players and their corpses. |
| 11.4 | 2026-10-04 | Fitting view and 3D portraits draw (layer 30), armour tilt per piece, other players' armour measured again when their avatar loads in, your face inside the helm with the head hidden, quest tracker item names, the WotLK quest map (Show Map, POIs, World Map view), combat animations seen by others and kept after an avatar change. |
| 11.3 | 2026-10-04 | Armour fitting for all 10 pieces with a live fitting view, hands / feet no longer oversized, hair hidden under the helm, cape physics, horse seat at WoW's rider height + saved slider, paladin auras / DK presences on the stance bar, spellbook tabs, loot roll item tooltip, auto dismount to attack, weapon away for ranged casts and on mount-up, hand effects follow the combat animation, Stormwind's floating cathedral copy hidden, NPC helmets and shoulders. |
| 11.2 | 2026-10-03 | After the first tests (11.1 and 11.2): forms hide every avatar copy, elite frames, WoW cursors, rank upgrade on bars, spell sound lengths, live portraits, ships carry like Somnium elevators, desktop walks by default, quest-box socket, floors, sheathed weapons, death aura, other players' jumps, VR 50 % target laser, strike ring size / spread / height, shared origin (nearby Somnium avatars and voice), Armour Adjustment + helm in photos, immersive armour Full (experimental), WoW minimap top right (desktop). |
| 11 | 2026-10-03 | Iteration 11 "Complete": vendors (buy, sell, buy back, repair), WoW's bags and bank (drag between slots, drag out to destroy with WoW's DELETE question), trainers, skills and professions, talents and glyphs, the Character / Reputation / Currency / Achievement windows, friends, guilds, guild charters, mail, chat channels and emotes, trade, duels, inspect, follow, raids, ready checks, raid icons, PvP flag, summons, the Auction House, battlegrounds and the Dungeon Finder, flight paths, ships and zeppelins online, Charge / knockbacks moving the avatar, ground spells with the green circle, area spells on the ground and weapon enchant glows, instances (area triggers, only your group inside), pets, totems, stables, vehicles and possession, druid forms / polymorph / ghost on the Somnium avatar (synced), WoW's full-body combat animation on desktop, immersive armour (helm + shoulders, off by default), the four extra action bars and the stance bar, VR rows (push / pull while holding grip), VR spell slotting from the spellbook, immersive strike (off by default), the Elwynn corpse quest and a more reliable corpse run, WoW's panel rules, cursor and Escape order on desktop. Offline: vendors, class trainers, talents, emotes, offline Charge / Blink / Disengage and ground spells. |

## Player.log lines to look for (all tagged `[SomniumWoW]`)

Iteration 11 (the complete game). Every step of "Iteration 11 - the complete game (2026-10)" names its line; in short:
- Boot: `WoW Dreamport Iteration 11 (2026-10-03): ...`. UI: `UI: 8999 strings read from the install's Interface\FrameXML\GlobalStrings.lua`,
  `UI keys: 78 desktop chords ...`, `UI: N registered windows built for the desktop layout; 0 failed`, `UI windows: <Window> opened at
  <spot>` / `... closed (pushed out by <Window>)`, `UI: Escape -> closed <what>.`, `UI: destroy <name> xN (bag b slot s) - asked (DELETE_ITEM)`.
- NPCs: `Online npc: <name> (..., npc flags 0x..) -> <opcode> - <service>`, `Online vendor: SMSG_LIST_INVENTORY from <name>: N items ...`,
  `Online bank: SMSG_SHOW_BANK of ...`, `Online trainer: <name> (<kind>) - N services: ...`, `Online profession: ...`,
  `OfflineSession: ours - offline vendor of <zone>: N items (...)`, `Offline trainer: <npc> (<class>) - N services: ...`.
- Talents and panes: `Online talents: N points free, spec s/n, ...`, `Offline talents: K learned talents from the record`,
  `Online reputation: list of N rows ...`, `Online currencies: N known ...`, `Online achievements: N earned (P points) ...`.
- Social: `Online social: ours (not WoW's) - ...`, `Online social: login channels in zone <id>: ...`, `Online guild: roster - N members
  ...`, `Online mail: inbox - N mail(s) ...`, `UI: chat ready (online: ...)`.
- Interact and queues: `Online interact: CMSG_INITIATE_TRADE`, `Online duel: SMSG_DUEL_REQUESTED ...`, `Follow: following X`,
  `Online raid: MSG_RAID_READY_CHECK ...`, `Online auction: <house> opened ...`, `UI: CONFIRM_BATTLEFIELD_ENTRY shown (...)`,
  `UI: LFDRoleCheckPopup shown`, `UI: queues hub built (...)`.
- Motion: `Online taxi: flight started - spline S, ...`, `Forced move: Path N ... takes the avatar`, `Online: control of the character
  LOST ...`, `Transports: standing on ...`, `OfflineSession: Charge - X yd to <mob> ...`.
- Spells: `Ground target: <spell> aimed (radius r yd, the mouse)` / `... placed at (x, y, z) (d yd)`, `SpellVfx: ground area of spell N
  ...`, `Enchant visual N: <models>@<attachment>`, `UI: Rune frame attached to the desktop player frame ...`.
- Bars and VR: `UI: action keys - 1..= main bar, ...`, `Online: action button S (<bar> n) = <id> '<name>' (CMSG_SET_ACTION_BUTTON)`,
  `VR input: right hand row -> <bar> (depth 0.06 m)`, `VR input: <spell> placed on <bar> button n (slot S)`, `VR strike: ring on (...)`.
- Avatar: `Forms: local look Creature display D (form f, spell s) - avatar hidden (N renderers) ...`, `Gear: WoW full-body animation
  (ours) - ...`, `Armour: the local player helm <id> -> ...`, `Settings: the row '<row>' is shown (its feature is built).`.
- World and quests: `Online instances: area trigger N (...) - CMSG_AREATRIGGER sent`, `Online instances: map A -> B '<name>' ...`,
  `Instances: remote character N hidden - another copy of map M ...`, `Online quests: CMSG_GAMEOBJ_USE on <object> (...)`,
  `Online death: Resurrect at the corpse sent (CMSG_RECLAIM_CORPSE ..., position synced first)`, `UI: quest tracker - ...`.
- Pets and vehicles: `Online: Pet bar for 0x... '<name>' ...`, `Online: totem n '<name>' for T s`, `Online: possessing <unit> (0x...)`,
  `Online: in vehicle <name> ... mode <Turret|Drive|Passenger>`, `Vehicle: WowMount drives ...`, `UI: main action bar hidden under the
  vehicle bar (VehicleMenuBar.lua).`, `UI: Mounts window - Summon companion '<name>' (spell N).`.

Iteration 9 (stage S1 - chat, groups, HUD, focus, the laser, name tags, the immersive hold). Every step of the test guide in
"Social, HUD, focus, immersive hold (stage S1)" names its line; in short:
- Boot: `WoW Dreamport Iteration 9 (2026-09-27): ...`.
- Chat: `Online: chat - ours (not WoW's): a line from an unknown player waits 3 s ...` (once), `Online: chat <Kind> from <name> (<n>
  chars)` (the first 20, then every 50th - never what was written), `Online: chat sent <Kind> [to <name>] (<n> chars, language <id>)`,
  `Online: chat - no name for speaker <guid> after 3 s (shown as Unknown)`, `Online: chat type 0xNN not shown (...)` (once per type),
  `Online: chat player not found / player ambiguous / wrong faction / party chat without a group: "<WoW text>"`, `Offline: chat line
  (...) not sent - chat is on the online realm ...`, `Offline: group <what> ignored - groups are on the online realm ...`, `UI: chat
  ready (online: ... / offline: ...)`, `UI: chat frame (desktop) - ChatFrame1 430 x 120 at BOTTOMLEFT (32, 95) ...`, `UI: chat -
  holding the EventSystem selection ...`, `UI: chat window (VR) built / opened / closed / placed ...`, `Keyboard: opened for Say.`,
  `Chat bubbles: first bubble (...)`, `Chat bubbles: a Somnium player's line shows over their avatar.`, `Speed: the WoW chat edit box
  is open - the avatar does not walk while typing ...`; problems: `UI: chat failed (logged once): ...`.
- Groups: `Online group: CMSG_GROUP_INVITE '<name>'`, `Online group: SMSG_PARTY_COMMAND_RESULT op <OP> '<name>' result <n> -> <WoW
  text>`, `Online group: SMSG_GROUP_INVITE from <name> - PendingInviteFrom set`, `Online group: SMSG_GROUP_LIST #n ...`, `Online group:
  SMSG_PARTY_MEMBER_STATS(_FULL) #n for <name> ...`, `Online group: CMSG_GROUP_DISBAND`, `Online group: SMSG_GROUP_DESTROYED`, `Party:
  the party frames show the server group (N other member(s)).` / `... the other Somnium players (N).`, `Party: group member <name> is
  Somnium player <nick> (...)`, `UI: group invite popup (StaticPopup PARTY_INVITE) for <name> ...`, `Portraits: loading <nick>'s Somnium
  profile picture (https://...)`, `Portraits: <nick>'s profile picture loaded (...; kept in memory only).` / `... failed (<kind> ...)`.
- HUD / focus: `UI: desktop HUD group size ...`, `UI: VR HUD size ...`, `UI: Settings 'Reset HUD' - ...`, `UI: party frame <name>
  (0x...) clicked - target set (n).`, `UI: party portrait WxH cut round (n).`, `UI: focus frame shows <name>.`, `Focus: button ready -
  ...`, `Focus: set to / <name> targeted / cleared by the X button (VR) | the H key ...`, `Focus: long X press (.. s) left to the client
  (inspector mode).`, `UI: Settings 'Return to the room' pressed once - ...`, `Room: back to the room (the Settings window's Return to
  the room button) from ...`, `UI: palm menu 319 x 78 units (...): Character, Spellbook, Quest Log, Settings, World Map, Mounts, Chat,
  Party, Backpack.`
- Laser: `VR input: target ray aims along the client's laser, Near-Far interactor '...' (far caster, stabilized origin); near the right
  hand: ...`, `VR input: far targeting along the client's laser, nothing of ours drawn on the ray; ...`, `UI: VR pointer = the client's
  laser, ...`, `VR input: target ray trigger on <name> at N m [(the client's laser is on it)]`, `VR input: the client's inspector mode is
  on ...`, `VR input: the client's laser is on a Somnium UI panel / on a Somnium interactable / holds something - that trigger press is
  the client's.`, `VR input: the client's laser is free again - far targeting on.`, `VR input: the chat keyboard is open - far
  targeting paused while typing (logged once).`
- Tags / instances: `Tags: on - ...`, `Tags: player N '<nick>' (has a WoW network record): Somnium name tag hidden with the Bridge
  (SetPlayerTagDisableState(true))`, `Tags: player N: the Bridge hid the name tag (its canvas 'Canvas' is off)`, `Tags: the Bridge
  player-tag member did not work here (...) - this visit uses the proven fallback ...`, `Tags: off (leaving the world) - N player tag(s)
  given back`, `Net: room joined (player id X, master M) - Fusion session #xxxxxxxx (...)`, `Net: player N joined / left the Fusion
  session #xxxxxxxx - ...`, `Net: alone in the Fusion session #xxxxxxxx for 60 s (...)`, `Wrist: the one-time hint 'alone in this
  Somnium instance' is shown for 30 s (...)`.
- Immersive hold and +10 %: `Online: asking the realm once whether it holds immersive casts (DPHOLD HELLO, ...)`, `Online: this realm
  holds immersive casts (Dreamport hold module, protocol 1, time-out 30 s) - ...; spells released with Immersive casting deal +10%
  damage (the module's house rule)` / `Online: this realm has no Dreamport hold (...)`, `Online: <spell> (<id>) cast at once, the realm
  is asked to hold it ... (DPHOLD HOLD, castCount N) #n`, `Online: <spell> armed on the realm (DPHOLD ARMED, ...)`, `Online: <spell>
  held by the realm at the end of its cast (DPHOLD READY after X s) - push to release it`, `Online: <spell> pushed after X s held -
  released on the realm (DPHOLD GO, ...)`, `Online: <spell> released by the realm at the push (...)`, `Online: <spell> (<id>) released
  with Immersive casting - the realm adds +10% damage (DPHOLD BONUS, castCount N) #n`, `Online: <spell> held 30 s without a push - the
  realm cancelled it (DPHOLD EXPIRED; ...)`, `Online: the realm does not hold <spell> (DPHOLD NOHOLD: <why>) - ...`, `Online: buff
  'Immersive Mode' shown (...)` / `... removed (...)`, `OfflineSession: Immersive casting - spells released with it deal +10% damage
  (ImmersiveDamageBonus 0.10, ...; logged once)`, `OfflineSession: <spell> released with Immersive casting - +10% damage for this cast
  (n)`, `OfflineSession: buff 'Immersive Mode' shown / removed (...)`, `VR input: <spell> is ready in the <hand> hand after X s
  (vibration 0.85 for 0.18 s) - ...`, `VR input: pushed the <hand> hand at X m/s -> <spell> released at <target> (...)`. The realm's
  worldserver log: `Dreamport hold: module loaded (...)`, `Dreamport hold: immersive damage bonus +10% (...)`, `Dreamport hold:
  <name> (...) said hello (...)`, `Dreamport hold: <name> released <id> (<castCount>) with Immersive casting - +10% damage for that
  cast (...)`.
- Game folder: `Install: ...` (the saved folder found / used / not usable, the boot, the browser, every listing, the folder chosen,
  saved or could NOT save, old files closed).

Iteration 8 (stage O2 - online combat, items, death, quests; the offline death flow; the hybrid sync). Every step of the test guide
in "Online: combat, items, death, quests (stage O2)" names its line; in short:
- Boot: `WoW Dreamport Iteration 8 (2026-09-27): ...`.
- Items: `Online items: inventory from the server: ...`, `Online items: inventory ready: N equipped, M in bags, F free, item info a/b -
  ...`, `Online items: equip <entry> '<name>' from bag 255 slot <n> (CMSG_AUTOEQUIP_ITEM)`, `Online items: equipment now: ...`,
  `Online items: use <entry> '<name>' -> spell <id> '<spell>' (CMSG_USE_ITEM bag .. slot .., castCount k, ...)`, `Online items:
  SMSG_INVENTORY_CHANGE_FAILURE <code> <NAME> for '<name>' "<WoW text>"` (silent codes: `... (no WoW text for this code - nothing
  shown)`), `Online items: looted item <entry> '<name>' x<n> (<total> in the bags)` / `... received ...`, `Online items: <name> is a
  text to read (CMSG_READ_ITEM) - no item text window online yet`; problems: `Online items: the server does not know item <entry>
  (...)`.
- Combat: `Online combat: melee auto-attack, every unit's swings (SMSG_ATTACKERSTATEUPDATE), XP, levels, kills and death from the
  server; ours: ...` (once), `Entities: click|mouse|VR trigger|VR ray on targeted <name> -> attack` (a second click on your target, both modes), `Online combat: CMSG_ATTACKSWING <name> (<guid>), <reaction>, level N`, `Online combat: SMSG_ATTACKSTART -
  the server swings at <name>`, `Online combat: SMSG_ATTACKERSTATEUPDATE me -> <name>: Hit 12 (HitInfo 0x2, state 1)` / `<name> -> me:
  ...`, `Online combat: SMSG_ATTACKSWING_NOTINRANGE -> 'You are too far away!'`, `Online combat: <name> died - killing blow me
  (SMSG_PARTYKILLLOG)`, `Online combat: SMSG_LOG_XPGAIN +N XP for <name> (M without the rested bonus)`, `Online combat:
  SMSG_LEVELUP_INFO level N (...)`, `Online combat: the player died to <name> (health 0 from the server)`, after a fight `Online combat:
  left combat - this session so far: swings mine N (h hit), at me M, others K; kills k, XP +x; packets p, odd 0`; problems (send the
  log): a WARN `Online combat: <opcode> (...) ... - handled as far as read`, "odd" above 0.
- Spells: `Online: spellbook from the server - N spells: M in the book (cast on the server), K mounts ...`, `Online: cast <id> '<name>'
  sent (castCount c, targets 0x.. ..., ... ms cast) #n`, `Online: cast <id> -> SPELL_START ... ms -> SPELL_GO (k hit)`, `Online: cast
  ... refused here: "<text>" (<why>)`, `Online: CAST_FAILED <spell> result <n> <NAME> "<text>"`, `Online: channel <id> '<name>' <ms> ms
  (MSG_CHANNEL_START)`, `Online: action button N = <id> '<name>' (CMSG_SET_ACTION_BUTTON)`, `Online: action bar from the server
  (SMSG_ACTION_BUTTONS state 1) - ...`, `Online: <name> held in the hand (immersive) - sent to the server at the push`, the first 5
  casts of other players `Online: another player's cast 0x<guid> '<name>' <id> -> CastSucceeded at 0x<target> (...)` (a Somnium
  player's character adds "(a Somnium player's character)"), `Online: cast <id> not sent - not in this client's Spell.dbc ("Not
  available online yet")`.
- Loot: `Online loot: opened <guid> (loot type t): <n> copper, <k> items`, `Online loot: the server closed <guid>`, `Online loot: shown
  after 2 s with N item names still unknown`, `Online loot: the server released <guid> without opening it (...)`.
- Death online: `Online death: the player died (health 0) at ...`, `Online death: Release Spirit sent (CMSG_REPOP_REQUEST)`, `Online
  death: graveyard on map ...`, `Online death: a ghost at ...`, `Online death: Resurrect at the corpse sent (CMSG_RECLAIM_CORPSE ...)`,
  `Online death: resurrection at the spirit healer <guid> sent (CMSG_SPIRIT_HEALER_ACTIVATE)`, `Online death: ... (SMSG_RESURRECT_REQUEST)
  - "Resurrect" accepts it`, `Online death: alive again with ...`; at a login while dead: `Online death: logged in lying dead at ...`
  or `Online death: logged in as a ghost at ... - asking where the corpse is (MSG_CORPSE_QUERY)`.
- Quests online: `Online quests: CMSG_GOSSIP_HELLO to <NPC> (...)`, `Online quests: dialog with <NPC> (gossip, <guid>) - N quests [...]
  after <ms> ms` (a trailing "(timed out waiting for ...)" = the server did not answer everything within 1.5 s), `Online quests: accept
  quest <id> '<title>' at <guid>`, `Online quests: quest <id> '<title>' in the log (slot s)` / `complete` / `left the log`, `Online
  quests: hand in quest <id> '<title>' reward index <i> (choice <c>)`, `Online quests: quest <id> '<title>' rewarded: <xp> xp, <copper>
  copper (SMSG_QUESTGIVER_QUEST_COMPLETE)`, `Online quests: abandon quest <id> '<title>' (log slot s)`, `Online quests: open <name>
  (type 3, lock L) with spell S (castCount N)`, `Online quests: CMSG_GAMEOBJ_USE on <name> (type t, entry E)`, `Online quests: <name>
  offers only services (npc flags 0x..) - not available online yet`.
- Death flow offline: `OfflineSession: the player died`, `OfflineSession: spirit released (Release Spirit | automatically after 6
  minutes, AC m_deathTimer) - a ghost at the graveyard ...; reclaim delay 30|60|120 s ...`, `OfflineSession: the corpse is in reach (..
  yd, delay over) - Resurrect offered.`, `OfflineSession: resurrected at the corpse - N/M health (50%) ...`, `OfflineSession: the spirit
  healer offers his resurrection (targeted, D yd).`, `OfflineSession: resurrected by the spirit healer - ...`.
- Death UI and sound (both modes): `UI: death popup 'DEATH' | 'RECOVER_CORPSE' | 'XP_LOSS' | ... shown.`, `UI: ghost HUD on - corpse
  marker ...`, `UI: ghost-world look ready ...` / `UI: ghost-world look out (weight 0).`, `Audio: ghost - music 36 'GhostMusic
  (DONOTRENAME)' (day 3820, night 3821), ambience 4160.`, `Audio: alive again - the zone's music and ambience.`
- Hybrid sync: `Gear: our cast record - <spell> (<id>) casting|channelling|held, hand ...`, `Gear: our release record - <spell> (<id>)
  ...`, `Gear: '<name>' (player N) casts <spell> (<id>) - drawn from their record, hand ...`, `... gathers (immersive) ... / holds
  ... in the right hand ...`, `... released <spell> (<id>) (hand ..., on themselves) ...`, `Gear: '<name>' (player N) - the server shows
  their character (guid G): ...`, `SpellVfx: guid G is a Somnium player's character - effects anchored to their avatar as drawn here
  (shown)`.
- Lead fixes: `Speed: on foot at the WoW run speed 7 yd/s = 6.40 m/s (desktop walk x4.00, sprint x1.07, VR stick x1.28; the client's
  VR stamina run still adds x1.5).` (again when the speed changes, e.g. as a ghost), `Speed: the client's own movement speed restored
  (...)`; `VR input: right hand at the Somnium tablet - the arm / wrist spell buttons pause ...`, `VR input: grip at the Somnium tablet
  - no spell menu on that hand (said once).`, `VR input: the trigger ray is on the Somnium tablet - no targeting through it (said once).`

Iteration 7 (mounts):
- Boot / data: `WoW Dreamport Iteration 7 (2026-09-27): ...`, `MountCatalog: 309 mounts (220 ground, 75 flying, 9 riding-scaled scripts,
  5 without a speed aura; 16 not offered offline) ...`, `MountAreaRules: AreaTable 2307 areas (...)`, `Mounts: data warmed in N ms (...)`,
  `Mounts: display table - 289 baked (source user's realm ...), L learned, stand-ins 2404/17697/17699 (...)`, `MountDisplays: entry 284 -> 2404 (baked)`.
- Offline rules: `Mounts: 265 of 309 unlocked for a Human Paladin (testing switch); riding 300 + Cold Weather Flying (derived, not saved)`,
  `Mounts: cast 458 'Brown Horse' -> mounted display 2404 (baked), run 11.20 yd/s, flight -, collision 2.88 yd, CanFly no, entry 284`,
  `Mounts: dismounted (Chosen|Spell|Death|Teleport|Indoors|Water|FlyingArea|Ended)`, `Mounts: can fly yes|no (...)`, `Mounts: fly anywhere
  (testing) on|off ...`, `Mounts: first place sample - ...`, `Mounts: no place sample yet - the spot counts as outdoors (mounting allowed)`,
  `Mounts: <name> may not fly here (...)`, `Mounts: in the water (...)`, `Mounts: GM command '...' ignored offline`.
- Where you stand: `Place: ours - sampled every 0.2 s, ...` (once), `Place: outdoors|indoors (WMO Stormwind.wmo group 'MB02' (id ..., MOGP
  0x2841, WMO 61 name set 0, WMOAreaTable flags 0x4), area 1519)` / `(terrain area 9 'Northshire Valley', area 9)`, `Place: in liquid - D yd
  above the feet (surface z Z)`, `Place: out of the liquid`, `Place: unknown yet (...) - sessions treat it as outdoors and dry`.
- The mount model: `Mount: ours - ...` (once), `Mount: preparing display D (...)`, `Mount: display D built (<model>, scale S, ...) - seat
  attachment 0 (bind pose) at (fwd F, up U) m, ...`, `Mount: display D seat measured on the animated model at (...) m ..., fixed from now on`,
  `Mount: display D anim A (sequence S, ground: horizontal only | air/water) saddle mean (...) vs seat (...) -> model offset (...) m`,
  `Mount: display D was still waiting for its art at mount-up - built now (N ms)`, `Mount: display D prepared, server says E - rebuilt`,
  `Mount: display D hidden at once (asked to fade ...)`; `RideGait: ours - ...` (once).
- The ride: `Ride: ours (PLAN7 s4.8) - ramps ...` (once, every value of ours), `Ride: proxy height H m = collision height C yd from the session`,
  `Ride: mounted <name> (spell S, display D from <src>) - ground|flying R yd/s (flight F), seat (fwd, up) m, hips H m measured|fallback, proxy
  r/h/step/slope (...from the avatar controller), camera chase|VR head placed`, every 30 s `Ride: 30 s - N m, avg X m/s (session Y), max
  step-filter gap Z m, contested C frames, rebases K (driven)`, `Ride: desktop chase camera on (brain paused: yes|not found, ...), start
  distance D m, ...` / `Ride: chase camera off (...)`, `Ride: take-off (flight ...)`, `Ride: landed (from flight).` / `Ride: landed after a
  ...`, `Ride: swimming (liquid L yd > T)`, `Ride: out of the water.`, `Ride: world ahead not ready (speed cap 60 %)` / `... ready again after
  ...`, `Ride: released (<why>) at <spot> (floor|terrain), no-drop write, teleport <how>; ...`; VR `Ride: VR inputs armed (...)`, `Ride: VR
  seat - pose entry turn N deg undone, hands reset; eye E m above the saddle; lean clamp 0.45 m`, every 30 s `Ride: client turn read back: N
  snaps, smooth D deg`. Problems (send the log): `Ride: root contested N of M frames ...`, `Ride: cannot ride (...)`, `Ride: the ride ended
  on its own (...)`, `Ride: the proxy was ... (fall-through rescue)`, `Ride: after the rebase the proxy was ... (re-seated)`, `Ride: blink: ...`,
  `Ride: VR controller input unavailable: ...`, `Ride: Player.MountDisplayId is 0 - drawing the labelled stand-in display ...`.
- The flight-path taxi on the new seat: `Flight: ...` lines as before, plus `Flight: seat measured on the animated mount at (...)`.
- Window and sounds: `UI: Mounts window footer - ...`, `UI: Mounts window - Mount|Dismount '<name>' (spell S).`, `UI: Mounts window -
  Testing (GM) row shown (online with a GM automatic account; lead decision D13).`, `UI: Testing (GM) 'Learn riding + mounts' for <name> ...`,
  `UI: Testing (GM) 'Go to Outland (test flying)' - '.tele HellfirePeninsula' ...`, `UI: buff '<name>' right-clicked - CancelAura(id)`,
  `UI: palm menu 253 x 78 units (... m wide): Character, Spellbook, Quest Log, Settings, World Map, Mounts, Backpack.`, `Audio: a ride owns the
  avatar - its footsteps off, the mount's own sounds on.` / `Audio: the ride let go - the avatar's footsteps back.`, `Audio: mount sounds for
  display D (<model>): footstep id F, wing flap ..., take-off / jump start ..., landing / jump end ...; N events (...)`, `Audio: mount footsteps
  on - first step on <surface> ...`, `Audio: mount wing flaps on - sound ...`.
- Other players (the viewer's log): `Gear: remote riders (ours) - ...` (once), `Gear: '<name>' (player N) mounted display D (ground|flying|
  taxi|not said), gait A x R, anchor relative (x, y, z, yaw) | fallback hips`, `Gear: ... mount display D built (...)`, `... dismounted`,
  `... mounted again`, every 120 s `Gear: ride smoothing - N rider(s), lag avg L s, correction avg C m max M m (steady delay offset left out
  avg B m), snaps S, clamped frames K` and `Gear: ride jitter '<name>': root step noise avg X m, max Y m at Z m/s as Fusion delivers it; drawn
  (stage B on) avg .., max ..`; the rider's own side `Gear: our ride record - ...`, `Net: own record written at most every 33 ms (riding ...)`;
  `Entities: <name> (a Somnium player) - click box and target circle follow their avatar ...`, `Entities: <name> rides (another player's
  client): drawn on its dead-reckoned path ...`.
- Online: `Online: spellbook from the server - N spells: B in the book (casting them is not online yet), M mounts (...), riding R; ...`,
  `Online: learned spell 458 'Brown Horse' (mount)` / `Online: unlearned spell ...`, `Online: riding skill 75 (skill 762 from the server)`,
  `Online: asking the server for the models of N mount creature entries`, `Online: creature 284 -> displays 2404 (learned from the server)`,
  `Online: cast 458 'Brown Horse' sent (castCount C)`, `Online: MSG_MOVE_STOP sent before the cast ...`, `Online: cast 458 -> SPELL_START 1500
  ms -> SPELL_GO (1 hit: us)`, `Online: CAST_FAILED 458 result 93 ONLY_OUTDOORS "Can only use outside"`, `Online: cast X got no SPELL_START /
  CAST_FAILED within 3 s - "No answer from the server" ...`, `Online: mount aura 458 'Brown Horse' on us`, `Online: mount-up - display D
  (server), ...`, `Online: mounted display 2404 (server), run 11.20 yd/s acked, flight ..., collision height 2.88 acked, can fly no, kind Ground,
  spell 458`, `Online: run speed 11.20 yd/s acked (counter C)` (run / flight / swim), `Online: collision height 2.88 yd acked (counter C)`,
  `Online: can fly set|unset (counter C) acked`, `Online: hover|feather fall|water walk|gravity off on|off (counter C) acked`, `Online: dismount
  asked (CMSG_CANCEL_MOUNT_AURA)`, `Online: dismounted (SMSG_DISMOUNT) - asked by us|by the server, run speed X`, `Online: the ride drives the
  avatar - ...` / `Online: the ride let go - ...`, every 30 s `Online: rider moves sent N (30 s: HEARTBEAT a, START_FORWARD b, ...), speed
  measured X vs server Y yd/s over T s of straight riding`, `Online: a real client jumped: zspeed X (negative = up), xyspeed Y yd/s, ...`,
  `Online: GM command '.player learn ...'`, `Online: GM commands queued: N waiting (...)`, `Online: GM command refused - ...`, `Online: system
  message "..."`, `Online: notification "..."`, `Online: place outdoors|indoors|unknown (...) - the server applies its own rules`, `Online: mounts
  - ours (not WoW's): cast time-out 3 s, ... GM commands 8 per burst 0.2 s apart then 2.2 s, rider heartbeats ...` (once). Warnings: `Online:
  MountCatalog is not loaded - ...`, `Online: UNIT_FLAG_MOUNT with mount display 0 = a vehicle mount ...`, `Online: the ride says Flying but
  the server has not sent SMSG_MOVE_SET_CAN_FLY ...`, `Online: <opcode> ... left N bytes unread ...`.

Iteration 6.1 (automatic accounts, stage A1) - the key (the hidden password) is never logged:
- Boot: `WoW Dreamport Iteration 6.1 (2026-09-27): ...`.
- Button: `Online: Play online with the automatic account chosen.` / `Online: the login form for a different account chosen.`,
  `Online: automatic account SCF5... key found (world storage: key; ...) - logging in to <host>:<port>.` or
  `Online: automatic account SCF5... - no key on this computer (...): asking the account service at <host>:8086 to make it.`
- Service: `Online account: registering S... (Somnium nickname '<nick>') at <host>:8086`, then ONE result line
  `Online account: S... at <host>:8086 -> Created (HTTP 201), game master, N ms` (or Rekeyed / Exists / NotAllowed / SlowDown /
  BadRequest / ServerError / Unreachable / WrongAnswer with the reason), `Online: automatic account SCF5... created (GM)` /
  `... re-keyed (unlocked by the server owner)`, `Online: automatic account SCF5... logged in.`
- Problems: `Online: the server did not accept this computer's key for automatic account SCF5... (<reason>) - asking the account
  service ...`, `... exists without key on this computer`, `... the account service does not allow this Somnium account (allow list).`,
  `Online: the account service gave no answer in 30 s - given up.`, `Online: opening the automatic account keys' storage failed: ...`.
- Settings: `Settings: forgetting this computer's key asked (automatic account SCF5...).`, `Online: automatic account SCF5... - this
  computer's key for <host> forgotten in Settings (...)`.
- On the server (`sudo journalctl -u wow-accounts`, or menu choice 2): `register ip=... id=... nick=... name=S... allowed by Somnium ID
  -> created account id N GM` / `-> exists (nothing changed)` / `-> not_allowed (...)`; never salt, verifier or key.

Iteration 6 (Online, stage O1) - neither the password nor the account name is ever logged:
- Boot: `WoW Dreamport Iteration 6 (2026-09-27): ...`; `Room: Play tab shows the online login / offline characters (why)`.
- Login: `Online: logging in to <host>:<port> (locale enUS; the account name and password are not logged)`, `Online login: <State>`
  (Connecting, Authenticating, Realms, Characters, EnteringWorld, InWorld, Failed), `Online auth: ... logged in at <host>:<port>, N realm(s)`,
  `Online auth failed at ...` (the reason in plain words, also on the panel), `Online: realm ... chosen`, `Online world: connected to ...`,
  `Online world: session ready (AUTH_OK) ... on realm N`, `Online world: in the login queue, position N`, `Online login: N character(s) on <realm>: ...`.
- Characters: `Online: naming a <race class> - suggested '<name>'`, `Online: creating '<name>' race R class C gender G ...`, `Online: deleting ...`,
  `Keyboard: opened for <field>` / `Keyboard: closed.`
- World: `Online: Enter World as ...`, `Online: <name> level N <race> <class> enters map M (<map>, <zone>) at x y z o - loading`,
  `Online world: session for <name> on map M at ..., DBC FactionTemplate 841, Item 46096, GameObjectDisplayInfo 3790`,
  `Online: session started in N ms - ... N entities, latency N ms`, and after 5 s the summary to send us:
  `Online world (first 5 s): map M, N objects, entities: C creatures, N NPCs, P players, G gameobjects (K named); update packets U (B blocks, X malformed),
  queries S sent / A answered, quest markers Q, unhandled: <opcodes>` - malformed update packets also warn (first 5).
- Views: `Entities: N in range (P players, S of them Somnium avatars, G gameobjects) ...` every 60 s, `Entities: X rides mount display ...`.
- Teleports / leaving: `Online world: ... teleport ...` / `new world map A -> B at ...` / `transfer aborted ...`, `Online: far teleport N to map M ... - loading screen, map reload`,
  `Online: arrived on map M ...`, `Online world: the avatar is N yd from where the server put the character ...` (warning: tell us),
  `Online: logging the character out (why)`, `Online: logged out - back at the character list`, `Online: session ended (why) after N events, P packets in / Q out`,
  `Online: connection closed (why)`, `Online world: closed (...)`.
- Warden (must never appear with the ServerKit realm): `Online world: server has Warden on - switch it off (see ServerKit). Warden packets are never answered.`

Iteration 5 (WoW Map):
- Boot: `WoW Dreamport Iteration 5 (2026-09-26): ...`; `Map: WoW Map ready - tablet app 'WoW Map' (searching for the client's tablet), VR palm menu World Map button, desktop key N / World Map micro button; zoom 60..600 m.`
- Tablet: `Tablet: linked - ...`, `Tablet: the WoW Map button is cell 1 of N shown in the home grid (...)`, `Tablet: WoW Map opened (...)` / `closed (...)`, `Map: panel built on the tablet (desktop|XR tablet).`; not found: `Tablet: not linked after N s - ...`; problems: `Tablet: injection failed (...)`, `Map: the tablet link could not start (...)`.
- Map: `Map: live camera created (1024 px RenderTexture, 12 renders/s while visible, ...)`, `Map: live map: N renders at 1024 px, CPU avg X ms (after the first 10: Y ms), max Z ms, light lift xL`, `Map: minimap table N entries.`, `Map: VR map window opened (palm menu).`, `Map: desktop map window opened (...)`.

Iteration 4 (stage A):
- Boot: `WoW Dreamport Iteration 4 (2026-09-26): ...`; `WowTaxiDb: 364 nodes, 915 paths (22586 points, 63 quest paths), network 83 Alliance / 83 Horde nodes, N ms`
- Floating origin: `Origin: rebased map M to cell (x,y) delta D m in N ms (K objects shifted, ..., landed at <where>...)` - "landed at driven"
  during a flight; a residual over 2 m after a rebase teleport is logged as a warning (tell us: it means the client placed the head,
  not the feet); "was shifted twice" = a bug.
- Flights: `OfflineSession: N flight masters on map M (Alliance|Horde), K known flight paths`, `OfflineSession: discovered flight path
  N '<name>'`, `OfflineSession: flight A -> B '<name>', H hop(s) (paths ...), D yd, C copper, mount display M`, `Flight: TaxiStarted path
  P, route of N path(s) [...] -> K nodes`, `Flight: NN% (a / b yd)`, `OfflineSession: landed at N '<name>' (x, y, z, o)`; problems:
  `Flight: cannot fly (...) - landing at the destination at once.`, `Flight: ended early - ...`.
- World details: `Details: ground effects N (of 24981 GroundEffectTexture rows), M detail models (...), N ms`; `Details: shader
  SomniumWoW/Clutter not found` = the WowClutter carrier is missing from the scene.
- UI: `UI: VR arm UI built in N ms (...); floating HUD on, forearm action bar off, palm menu: look at your left palm.`, `UI: palm menu
  open (palm facing 1.00).`, `UI: party frames show N of M other player(s) (...)`, `UI: low-health vignette on (15% health, ...)`.
- Other players: `Gear: 'Name' (player N) drawn|hidden (Shown|Unmapped|OtherMap|Room|Far|NoBody ...; their origin map M cell (x, y, z)
  ...)`, `Gear: 'Name' (player N) rides mount display D ...`, every 120 s `Gear: remap - N root shifts so far, K roots moved by someone
  else before the restore` (K should stay 0).

Iteration 3:
- Boot: `WoW Dreamport Iteration 3 (2026-09-26): ...`, `WowZones: <Key> built - '<Zone>' map M, N creature templates, S spawns, Q quests, C class starts` (8 zones), `SpellDb: 49839 spells read`, `Race screen: 10 races ...`
- Characters: `Character create: ...`, `Class screen: created ...`, `Play: <nick> <Race> <Class> ... in <zone> (map M)`, `Arrive: <zone> (ready after N s): ground collider '<name>' D m below the position ...` (a WMO group name for the Deathknell crypt / the Ammen Vale platform), `Play: difficulty ...`, `OfflineSession: difficulty easy|normal|hard (creature damage xD, health xH)`, `OfflineSession: summoned Imp`
- Settings: `Settings: N values loaded` / `Settings: saved`, `Settings: <label> = <value> (room panel)`, `UI: gameplay settings applied`, `UI: desktop interface scale ...` / `UI: arm panel scale ...`, `Gear: settings - swing speed ...`
- Names over heads: `Entities: head of <name> (display N, <model> xS): H m from the standing pose (bind-pose bounds B m)` - H should be about 1.7-1.9 m for humans (a value like 0.2 m means the plate sits in the body), `Entities: dialog with <NPC> - talk emote, facing the player`
- Effects: `M2Effects: S static + A animated instances, N particle systems, R ribbons, ... shader SomniumWoW/Particle ...` (`shader SomniumWoW/Particle missing` = the WowParticle carrier is not in the scene), `SpellVfx: SpellVisual 9406, SpellVisualKit 8663, ...`, `SpellVfx: E effects spawned, M missiles, I impacts, F skipped (far) ...`
- Sound: `Audio: driver awake ...`, `Audio: ready in N ms - SoundEntries 12941 ...`, `Audio: interface sounds 22 of 22 found`, `Audio: zone '<zone>' (area N): music ..., ambience ...`, `Audio: music ...`, `Audio: footsteps on ...`, `Audio: error voice ...`; problems: `... threw ...`
- UI: `UI: game UI ... O settings`, `UI: pet bar for Imp (level N): 2 abilities`
- Avatar animations: `Gear: avatar of <who> can be posed: arms True ... fingers True`, `Gear: avatar of <who> plays Slash1H (motion n)`

Iteration 2 (unchanged):
- Boot: `WoW Dreamport Iteration 2 (2026-09-26): ...`, `Class screen: 5 classes (names ChrClasses.dbc, texts GlueStrings.lua, icons CharacterCreate.lua, colours Constants.lua)`
- Characters: `Characters: waiting for the Somnium account`, `Characters: N loaded for <nick> (<id>...) from world storage (2 copies) in N ms - ...`, `Class screen: create <Class> for ...`, `Class screen: created ...`, `Characters: delete asked/confirmed/cancelled`
- Saves: `Save: ok N bytes, gen G, M character(s), T ms - world storage ok; world storage_2 ok (<why>)`; problems: `Save: FAILED ...`, `Save: ... could not be decrypted - the raw file was kept as ..._bad`
- Play: `Play: <name> <Class> <level> (id ...) at (...)`, `SpellDb: 49839 spells read in N ms`, `Play: spells, items and game tables read in N ms (worker thread)`, `Arrive: Shadowglen, Teldrassil (ready after N s): ...`, `OfflineSession: Shadowglen map 1, 69 spawns (20 templates), 14 quests, 56 items; ...`, `Play: session started in N ms - ...`, `Play: facing from the headset|avatar root|camera`
- Game: `OfflineSession: level N (...)`, `OfflineSession: quest 9000N '<title>' accepted|rewarded ...`, `OfflineSession: the player died to <creature> (level N)`, `Play: teleport to (...) (the game moved the player)`, `Play: session ended (...) after N s, E events`; should never appear: `Play: OfflineSession.Tick threw`
- Creatures: `Entities: art ready after N ms ... circle ok, plate ok/ok/ok, sparkle ok`, `Entities: creature sounds for N displays ...`, `Entities: 69 views, 69 models built, 0 failed ...`, `Entities: click|VR trigger|mouse on <name> -> target|talk|loot`; warnings `Entities: no model for ...`, `Entities: VR interactable not added`
- UI: `UI: desktop HUD built in N ms ...` / `UI: VR arm UI built ...`, `UI: desktop HUD active.` / `UI: VR arm UI active.`, `UI: game UI for <name> ...; keys ...`, `UI: arm panel on the left hand '<name>' (0.31 x 0.15 m, lift 0.03 m, back 0.13 m; ...)`, `UI: VR pointer = the client's ray interactor '<name>' (right hand).`, `UI: '<button>' on WoW Arm pressed by the trigger|poke ...`, `UI: N icons loaded, M missing.`; errors `UI: refresh failed: ...`, `UI: vr failed: ...`
- Weapons: `Gear: ready (...)`, `Gear: local player in VR ...` / `in desktop mode - the Z key draws/sheathes`, `Gear: body of the local player: humanoid Animator '<name>' ...` (or `... no humanoid avatar found ...`), `Gear: local player main hand display N (<model>, ... SheatheType S ...) sheathed at <spot>`, `Gear: VR right hand drew the main hand ...`, `Gear: VR swing (right, tip X m/s, peak Y) hit <creature> ... -> StartAttack`, `Gear: the Z key -> SetSheath(...)`
- Network: `Net: room joined (player id N, master True|False)`, `Net: 50 player slot object(s) found ('Wow Net Player 0..49'), 50 usable.` (`... 0 ... the scene needs the 'Wow Net' objects` = the scene objects are missing), `Net: slot k claimed (generation g) in N ms.`, `Net: slot k = player N '<nick>': level L class C, main ..., VR`, `Gear: drawing player N '<nick>' (slot k)`, every 2 min `Net: 50 slots, mine k, M other player(s) drawn, ...`

Iteration 1 (unchanged): `Boot: game files ready in N ms (...)`, `Load: ready around the spot after N s`, `Arrive: <location> ... - DoTeleportToPoint.`, `Lighting: map ...`, `Map: WMO ... (prepared on the worker)`, `Line-up: N built, M missing`, `Fall guard: ...`, `Wrist: ...`, `Room: back to the room (the K key|the Settings window's Return to the room button)` (stage S1: the wrist button is gone); problems: `... threw ...`, `shader SomniumWoW/<Name> missing (carrier ...)`, `WowTextures: <path> -> Missing texture: <why>`.

## Known gaps (iteration 11)

Not tested yet: nothing of iteration 11 has run on your realm, in the Somnium client or in a headset. Online code was tested only
against the harness fake server; the VR values (row step 7 cm, hold 1.2 cm, strike-ring place and swing speed, the 0.5 s long press)
are our own first guesses. The Unity-side checks of the window click blockers (a click on a window does not reach the world, a
right-drag over a window does not turn the camera, dragging a spellbook icon onto itself does not cast) need the editor or the client.

Mouse, windows and keys:
- No Help button (no HelpFrame / GM tickets). The Game Menu has Interface, Key Bindings, Logout, Exit Game and Return to Game - not
  Video and Sound (they are in our Settings), Macros or Ratings. Key Bindings is a read-only list (keys cannot be rebound).
- The bar toggles are not sent to the realm (CMSG_SET_ACTIONBAR_TOGGLES: the client's bit order is not documented), so the WoW client's
  bar choice and ours are separate.
- The destroy question names the item in its quality colour plus " xN" (the real client formats an item link in C code). Escape with an
  item on the cursor puts it back (ours).
- The cursor art per hovered thing (Attack / Speak / Buy ...) is our own mapping. VR drag uses the laser + trigger only (the poke
  finger only taps). WoW's child windows, the "loot under mouse" option, the reputation / max-level / tutorial managed offsets and the
  quest log's double-wide layout are not done (ours is a 384-wide panel).
- Popups cannot change their text while shown: the duel-area and summon countdowns re-show the popup each second, and a Shift-click on
  a chat name re-shows the Add Friend popup with the name in it.
- The reputation bar above the action bar ("Show as Experience Bar") only sets the server field; the bar is not drawn. The rest icon on
  the player frame and the rested part of the XP bar are not drawn.

Vendors, bags, bank (npc / npcui):
- No item refunds (selling a refundable extended-cost item does nothing), no Currency tab in the bags (token slots), no guild-bank
  repair; the repair cost shown is the client's own estimate (no reputation discount).
- No sound on a sale or purchase. Gossip points of interest show as a yellow chat line only. A lone service option is always chosen
  directly. Gems, gift wrap and item enchants from the bags are not here. The bind question names the area you stand in.
- No item links (Shift-click into chat), no tooltip compare, no cooldown sweeps on bag and bank slots, no quest-start "!" on bag items.
  The item-text window has no "From, <name>" line and no translation bar. VR bags are scaled down to fit the arm window.
- Offline: no extra bags, bank, innkeeper, repair or readable items; offline vendors sell only the zone's food / drink / potions at our prices.
- The tabard designer answers "That window is not in this version yet.".

Trainers and professions:
- No professions offline (trainers, nodes, fishing, recipes); offline Skills lists only the class skill lines.
- The profession window has no search box, sub-class / slot drop-downs or recipe link button; the trainer filter is three check boxes
  instead of WoW's drop-down. List order, hidden skills and the per-recipe verb are our rules. Create N stops when the window closes.
- The fishing splash is only the bobber's own animation. No pet trainers, no trade-skill links in chat.

Talents, character, achievements:
- Offline talents: only talents made of the stat auras the offline rules read are simulated; others are learned and saved but logged
  "not simulated offline". Offline glyphs, dual spec and pet talents are online only. Online pet talents are not probed.
- Achievements: no Statistics tab, comparison or tracking; realm-first broadcasts are not shown; a series shows only its next step.
- Character pane: no 3D model (health, power, experience and money are shown there instead - ours); resistances have no buff colours;
  stat tooltips leave out the class-formula lines; the stat drop-down choice lasts for the session only.
- Offline: reputation, currency, titles and achievements are online only.

Social, chat, mail, guild:
- No arena charters / arena teams, tabard designer or guild bank. Emote voices are not played. Channel moderation (password, owner,
  moderators, mute, kick / ban, invites, announce) is not done; /chat lists only what works.
- The auction-house invoice mail shows its body text. No report spam, whisper pop-out windows, Battle.net friends or voice rows.
- Guild names over players are not drawn. No name completion in chat or in the Send Mail "To" box.

Trade, duels, inspect, follow, raids:
- Trade: the portraits are class icons; offered items are not greyed in the bags. Duels: the countdown is the yellow message line.
- Inspect: the PvP tab is greyed; the Talents tab is a summary (points per tree, glyphs), no 3D model.
- Unit menus: no Compare Achievements, Recruit-a-Friend, voice, Opt Out of Loot or Promote to Master Looter.
- No Raid Browser; raid pullouts are desktop only and have no class filter; the ready check lasts 30 s (ours).
- Follow on desktop: the avatar glides with idle legs while it is walked; following another Somnium avatar offline is not offered.

Auction House, battlegrounds, Dungeon Finder:
- No multi-stack auctions or price-per-unit; no armour-slot sub-filters, "Show on player" or auctioneer portrait; your own auctions show
  "High Bidder" instead of a name; random-suffix item names are missing.
- No arena teams or lifetime honor; battleground objective columns are numbers; no battle map, no Wintergrasp queue.
- No raid browser (LFR), party members' locks are not shown, no holiday dungeon UI. Drop-downs are cycling buttons (ours).

Movement, flights, transports:
- A knockback or pull while mounted is not applied to the avatar (the server position is right).
- Hover (Levitate) does not lift the avatar; a knockback against a wall stops instead of sliding; no swimming on foot (a knockback
  into deep water ends at the surface).
- Flight costs on the online map are DBC prices without the reputation discount (the server charges the right amount).
- Transports inside instances (ICC gunships, Ulduar) are not animated; static transports move but do not turn.
- Offline: no transports, and Flight Form does not fly. Forced moves, transports and the two-map flight's loading screen are not
  covered by probes (needs your test).

Spells and effects:
- Eagle Eye / Far Sight / Mind Vision: the cast works but our camera does not follow the far viewpoint (VR comfort rule).
- Online range / radius talents are exact only for the modifiers the talents area reports; otherwise the server decides "Out of range".
  Line of sight to the chosen spot is checked by the server only. Shaman imbues go to the main hand only.
- Offline: Distract and the enemy side of Mass Dispel are not simulated; Inferno lands next to the player; slows, Flare's reveal and
  Hurricane's attack-speed cut are only drawn; only main-hand temporary enchants exist offline.

Action bars and VR casting:
- Macros and equipment sets are shown and can be moved but only run in the WoW client. Item buttons have no cooldown sweep.
- Only the Bottom Left Bar has keys (Alt+1..=, ours); the other three bars have none. In VR, pages 2-6 of the main bar cannot be reached.
- The stance bar shows forms only (auras, presences and aspects are cast from the bars). Vehicle pages show only through the pet rows.
  No bonus-bar slide. Offline, equipped "Use:" items cannot be used and there is no spell queue.
- Pull-to-cancel is gone: release in the middle of the ring to cancel. Online warriors and druids in a form now see the form's bar (72+).

Avatar, forms, armour:
- Other WoW players (their WoW models) still have no helm / shoulder models; NPC weapons always show in hand (11.3).
- WMO portal clipping is not done: from Stormwind's streets WoW shows the cathedral's outside facade only through the streets' sky
  openings; we draw the whole facade, so some of its slab edges look cut off above the rooftops (11.3).
- No stance bar in VR (11.3): paladin auras and forms are reached by slotting them onto a VR action row from the spellbook.
- "Immersive armour: Full" (11.2, experimental): a strip of clothes can show at a low collar, the thumb beside a gauntlet, chunky
  shoes at the soles and slivers under a raised arm; mirrors and the tablet camera do not show the shell; each gear change or new
  avatar costs one short measuring hitch. The minimap (11.2) is desktop only and keeps the outdoor map indoors (WMO-only dungeons:
  black with blips); its yards per zoom step are ours (the table WoW map addons use).
- Ghost / stealth / shadow shells copy skinned meshes only. Offline has no hair / skin colour or gender: default form models, male
  animations and helms. Remote form gait comes from the synced root (a small delay); form sounds are not played on the avatar.
- WoW's "upper body only while moving" is approximated; the pelvis turn is capped at 45 deg (ours). The retarget was not checked in a
  render; helms are sized from the head height and hair may poke through. First person is detected by camera distance (0.35 m, ours).

Instances and the world:
- WMO skyboxes (Dire Maul) are not drawn; no portal culling inside WMOs (frame time in big dungeons unmeasured, also in VR); 26
  instance maps without a Light.dbc row use the default light.
- Zone text on entering and the minimap / WoW Map inside WMO-only dungeons are missing. A group member in another copy of the same
  dungeon is drawn for up to 60 s. No in-instance heroic switch (ICC / Ruby Sanctum). Offline has no dungeons.
- The world-port ack is sent at once, not after loading. A trigger you arrive inside counts only after leaving and re-entering.

Quests and death:
- Quest areas on the WoW Map (quest POIs) are not requested. The tracker has no sort / filter; watched quests reset after a relog; the
  title menu is desktop only. Repeatable quests' markers over NPCs are yellow, not blue.
- No self-resurrection offline. Abandon uses the plain question. Offline a corpse from a death in the air lands on the terrain.
  "You must enter the instance to recover your corpse" shows only on the corpse compass.

Pets and vehicles:
- No pet paperdoll (Pet tab of the character window), happiness damage % not shown; the stable has no 3D model or diet line and uses
  Stable / Take buttons instead of dragging.
- The vehicle bar's skin art is not drawn (a dark panel); seat arrows replace the seat indicator; pitch buttons step per click. The
  totem bar is a plain list (no flyout art). Pet sounds and pet combo points are only logged.
- The passenger seat point is our choice (some riders sit a little off); vehicle enter / exit animations are not played; VR turrets
  aim with the sticks only; aimed vehicle shots use our own maths. While you possess a unit, other Somnium players see your avatar
  riding it plus its own model.
- Offline: no hunter pets, stables, companions, vehicles, possession or charm; offline pet states are not saved.

## Known gaps (stage S1)

Server step:
- The hold module (`ServerKit/dreamport-hold`) has not been compiled against AzerothCore yet: the lead's `setup-hold.sh` run is its
  first compile (it prints the compiler errors). Until HOLD OK, online immersive casting works as in stage O2 (held on your side
  before the cast, the cast bar runs after the push) and online there is no +10 % and no "Immersive Mode" buff.
- On x64 servers `docker compose pull` brings back AzerothCore's prebuilt worldserver without the module: run `setup-hold.sh` again
  after every update (`ServerKit/README.md` section 9). ARM compiles it in again by itself.

Chat:
- Emote commands (`/wave`, `/dance` ... = CMSG_TEXT_EMOTE) and other players' text emotes are not done (an unknown slash command
  prints "Type '/help' for a listing of a few commands."). Chat channels (General, Trade, `/1`, `/2`) are not joined, so no channel
  chat; achievements, AFK / DND auto replies and loot / skill chat lines are not shown.
- A language your character does not know reads "says something unintelligible." (WoW's client scrambles the words instead).
- No raid chat kind: raid / battleground group lines show as party lines.
- Desktop: no chat tabs, no moving / resizing the chat frame, no clickable names or item links (links show as their [text]), no
  timestamps, no ignore list. VR: the headset keyboard is the room's keyboard (no chat history there).
- While the desktop edit box is open the avatar does not walk (our rule, as in WoW); the Somnium client's other keys (Tab, M, I ...)
  still act (a world cannot switch off the client's own input actions).

Groups and portraits:
- Uninvite (kick), Promote to leader, loot method / threshold, raid conversion, ready checks and raid target icons are not offered
  (no session call for them yet); the party member's menu has only Leave party (ours) and Cancel. The inviter cancelling an invite is
  not seen (AzerothCore sends nothing): the popup stays until Accept / Decline / 60 s.
- Party frames show at most 4 members; a raid is listed but not drawn as raid frames. Party members' buffs / debuffs are not shown.
- Group members who are not Somnium players show their class icon. While in a group, other Somnium players outside it are not party
  frames (WoW's rule) - and so not the tablet map's party marks.
- Profile pictures: the address is fixed to Somnium's release identity API (`https://somniumspace.org/userimage/<id>/userprofileimage.png`);
  an animated (GIF) picture is not decoded (the class icon stays). A picture that is not CPU-readable is shown square and smaller inside
  the ring instead of round. The pictures show in the party frames only (the target / focus frame portraits stay WoW's).

HUD and focus:
- The HUD rows are in the in-game Settings window only, not in the room's Settings tab.
- VR: only the party frames and the focus frame can be pointed at on the floating HUD (not the player, target or pet frames); no
  tooltips on the VR HUD.
- The focus frame is WoW's small size only (no "Larger Focus Frame" option) and has no target-of-target; it hides while the focus is
  not around, and the button then says "Your focus is out of range." (online a server-group member out of view is still targeted).
  The focus is not kept across sessions, characters or relogs. While the Somnium inspector mode is on, a short X press is the
  inspector's (its Mute / Hide of the inspected object), not the focus.
- Offline, other Somnium players are not units of your offline game, so their party frames cannot target them; online a group member
  the realm has not shown yet cannot be targeted from the frame ("... cannot be targeted yet"). The offline (disconnected) portrait is
  tinted grey rather than desaturated.

The laser:
- Beyond the client's own 5 m reach the Somnium client draws no laser line at all (its own settings): far aiming shows only the
  highlight on the unit and the tick. Lengthening the client's line would change the client's hand rig - not done.
- The white dot stays on our own game panels (the client draws no line there: they are not Somnium UI). Untested in a headset by us;
  every rule comes from the client's source.

Name tags, wrist, instances:
- The client's inspector mode no longer shows name tags in this world (it uses the same client flag).
- Tags are hidden in the black room too, not only in the WoW world.
- Fallback mode only: a tag can show for up to 1 s when the client re-writes its transparency.
- The "alone" hint shows only in the WoW world (a player waiting in the room gets it on arrival); the room panel has no hint.
- The Fusion session name is logged only as a fingerprint (the client itself logs it encrypted), read by reflection (not a proven
  member): on an unexpected client build it just says "not readable".
- The Bridge name-tag call (`SetPlayerTagDisableState`) is first used in this upload. If the upload's server compile refuses it, the
  lead switches line 5 of `Game/Access/WowTags.cs` off (the proven fallback only) and re-uploads.

Immersive casting and the +10 %:
- Online the held spell goes to the target it was started on (a WoW cast in progress keeps its target); offline it still re-aims at
  the push to the selected target if that is valid.
- Every Dreamport client, desktop too, sends one `DPHOLD HELLO` per login (it changes nothing, nobody sees it).
- Any client that knows the messages could hold its own casts (up to 30 s, only its own, still a cast in progress) or flag its own
  casts for the +10 % - it can never touch another player's. On your private realm that is the trade-off of the house rule.
- Other players see your cast bar run and then stay full until you push.
- READY (the vibration) comes up to one server update plus the round trip after the cast bar ends; a very short cast under heavy lag
  may go off unheld and is then a normal cast (no push, no bonus).
- The +10 % counts only damage done under the released spell's own id: damage of spells it triggers under another id (Arcane
  Missiles' missiles, procs) is normal; a spell whose aura both damages and heals is not boosted; online, a normal cast of the same
  spell within 6 s of a released one ends the bonus of the released one still in flight (never the other way round).
- The buff is our own: normal WoW clients do not see it, and online it is not shown on a realm without the module.
- The push threshold of 1.5 m/s is still not measured in a headset (an older gap).

Game folder:
- The folder browser pages (14 folders a page) instead of scrolling; there is no path field in VR (use the list).

## Known gaps (stage O2)

Not online yet (stage O3b, by plan): vendors, trainers, flight masters, bankers, the bank, the mailbox, the auction house (chat and
groups came in stage S1 - still missing: party loot rolls, quest sharing), trade, duels, inspecting another player. They answer "Not available
online yet" or, for another player, only target it.

Items:
- The bag window still has 16 slots: items inside extra bags are counted in "N free" and listed after the backpack, but only the
  first 16 are drawn. No bag bar, no drag and drop, no splitting, destroying or selling items.
- No item cooldown display (the Hearthstone etc.: the server refuses an early use with its own text). Readable items (letters,
  books) say "Not available online yet" (no item text window). Keyring, currency tokens and the bank are not shown.
- The loot line always reads "You receive loot: X", also for quest rewards and created items (the log names the kind). The
  Hearthstone tooltip says "Returns you to your home." instead of the bind location's name.
- Using a mount-teaching item sends it to the server like any item with a "Use:" spell; this was not tested with a mount item.

Combat:
- Floating combat numbers only for your own, your pet's and your totems' fights (like WoW). Other fights animate (attack and wound)
  and play the creatures' attack and wound sounds, but other players' weapon swing / impact sounds are not played.
- The client does not turn your character toward the target, while the server checks that you face it (120 degrees): "You are
  facing the wrong way!" means turn toward it.
- The texts for SMSG_ATTACKSWING_DEADTARGET / SMSG_ATTACKSWING_CANT_ATTACK are chosen by their GlobalStrings names, not checked in
  the client (AzerothCore never sends these two).
- The killer named at a death: your kills (SMSG_PARTYKILLLOG) or the last hit within 10 s; otherwise none.
- XP messages online never name the quest (SMSG_LOG_XPGAIN does not say which one).
- A character that logs in dead or as a ghost gets no "You have died" message and no death cry (the popups and the ghost state
  still come from the server's fields). A ghost's corpse marker appears once the server has said where the corpse is (about one
  round trip after the login).
- Duels and vehicles' melee are not covered.

Spells:
- No ground-target circle: spells aimed at the ground (Blizzard, Flamestrike, Rain of Fire ...) are cast at the target's feet, else
  your own.
- An area / multi-target spell draws its impact on the first hit target only. Ground effects of area spells (the Blizzard or
  Consecration area) are not drawn.
- Item and macro buttons of the server's action bar stay empty (the bar holds spells only; the log says "not shown here: N item(s)...").
- Talent and gear changes to cost, range and cooldown are not known client-side: the spellbook shows the game data's values, the
  server decides (its refusal text shows; changed cooldowns arrive from the server).
- Spells aimed at an item (enchants, weapon poisons, opening a lockbox) are sent without the item and the server refuses them with
  its own text. No trade-skill window online (a recipe in the spellbook is cast once, like a button). Pet action bars are not online.
- Other players' instant spells show no precast (as in the real client); their cast bar needs them in view when their cast starts.

Loot and death online:
- Group loot (need / greed rolls, master looter) is not shown (groups are O3b).
- A resurrection request from another player cannot be declined: it stays until you accept it, resurrect another way or another
  request replaces it (Release Spirit is offered beside Accept). Its "will be able to in N seconds" wording is not used: the plain
  text shows and Resurrect waits for the delay.
- The release countdown in the death popup is the popup's own (6 minutes from when it opened), not the server's timer; the server
  still releases you automatically after its 6 minutes.
- Player corpses and bones are not lootable (battleground insignia).
- Self-resurrection (Soulstone, Reincarnation) is not offered online: the server's PLAYER_SELF_RES_SPELL is not read and
  CMSG_SELF_RES is not sent, so the death popup has no "Use Soulstone" / "Reincarnation" button; release and run back, or use the
  spirit healer.

Quests and NPCs:
- Gossip options (vendor, trainer, innkeeper, flight master, scripted "I am ready" lines) are listed as text only and cannot be
  chosen: the dialog has no option buttons yet (it needs an addition to the session contract; O3b).
- Right-clicking another player only targets it; attacking a hostile player goes through Attack on the action bar.
- Timed quests: the countdown is not shown; a timeout still fails the quest ("<title> failed.").
- Game objects with no use online yet: chairs, books and plaques (page text), fishing, the mailbox (clicking them only writes a log
  line; the mailbox says "Not available online yet").
- The NPC greeting uses the NPC text's first variant, else the second (AzerothCore's own choice for quest-list greetings); the real
  client's male / female text choice is not made.
- After a hand-in, the next quest of a chain shows up in the same NPC's list instead of its details page opening at once.
- Before any quest giver has named an XP figure, the quest log shows the client's own XP formula (QuestXP.dbc) without the realm's
  XP rate; the giver's figure replaces it once you talk to them.

Death flow (both modes, owner ghost):
- Offline items have no durability, so the spirit healer's 25% durability loss and the 10% on death are not applied offline (online
  the server applies them); the popup still shows WoW's text.
- Your avatar is not held in place while lying dead (the corpse stays where you fell). Night elves do not turn into a wisp (the
  avatar is never changed; the wisp's speed applies).
- No corpse body is drawn (the avatar is the character): the world marker, the HUD arrow and the WoW Map icon show where it lies.
  The Spirit Healer's loop sound is not played (no creature loop sounds yet).
- The ghost-world look is a grey-blue post-processing tint with our own values; WoW's own death fog and skybox are not used yet. In
  VR the world-space UI panels are greyed with the world while you are a ghost.
- Offline, the 30 / 60 / 120 s escalation counts deaths of this session only; leaving the world as a ghost brings you back alive at
  the graveyard next time.
- Offline, you "talk to" the spirit healer by targeting him (click or trigger), because talking is refused to the dead.
- The ghost screens were checked by compile and the harness only (no render-lab pictures).

Somnium players (hybrid sync):
- The VR cast hand is taken from the radial menu's grip release (ours, 0.6 s window); a VR cast from a key or from the desktop bar
  says no hand, so others see WoW's both-hand effects.
- Offline, and for characters your server does not show, other Somnium players' casts show only the caster's look: no missiles, no
  impacts on monsters (each player's monsters are their own), no buff / aura glows on their avatars. Online the server shows all of it.
- Other Somnium players' cast bars are not shown in the party frames (the record carries spell and progress; a later UI change).

Lead fixes in this upload:
- The run-speed match cannot switch off the Somnium client's VR stamina run (the stick click x1.5 still adds on top).

## Known gaps (iteration 7, mounts)

Not in this iteration (by plan):
- No live 3D model in the Mounts window (its big icon, name and "Model: ..." line instead).
- Not offered: the 9 riding-scaled script mounts (Headless Horseman's, Invincible, Celestial Steed, X-53, Big Love Rocket ...), the
  3 flying carpets and every vehicle / passenger mount, the 4 Qiraji Battle Tanks (the window's footer lists them). Online a vehicle
  mount (the server's mount flag with display 0) is logged and not ridden - do not teach those on the realm yet.
- Offline there is no way to learn riding or mounts without the testing switch (no riding trainers or mount vendors offline; the
  offline zones end at level 10, Apprentice Riding needs 20). Online, mount ITEMS do not teach their spell yet (using items online is
  stage O2): use the Testing (GM) buttons or the console.
- WoW's "Auto Dismount in Flight" option: casting in the air is refused with "You are mounted." (WoW's default).
- No fall damage offline (walkers have none either); online the server applies WoW's rule to the rider's falls.
- WMO liquids (Stormwind's canals, pools inside buildings) are not water for the mounts: ground mounts walk their bed. Mounts swim at
  the surface only (no diving on a mount).
- The mount model does not lean into climbs and turns (the view never would); the rider's legs pass into the mount's sides (the
  Somnium chair pose, decision D7).
- No first-person view on a mount on desktop, no autorun key, no Companions (pets) tab, no WoW "mount special" flourish.
- Only your own mount makes footstep / wing sounds (other players' and creatures' mount footsteps: not yet).
- No VR comfort vignette and no settings for VR comfort, steering speed or mouse steering sensitivity (fixed values, logged once).
- Online: non-mount spells (stage O2), flight paths and a GM chat line in the Dreamport (O3); the Dalaran / Wintergrasp no-fly auras
  offline (those places are not reachable offline).

Not known (not in the game data or AzerothCore's source, so not guessed):
- WoW's jump take-off speed (a client constant): ours 7.5 m/s. When a normal 3.3.5a client jumps near you online, the log line
  `Online: a real client jumped: zspeed X ...` gives the real value.
- The real client's climb / descent rate while flying and its exact packet pattern while mouse-flying: ours is 0.6 x the flight speed
  and only packets AzerothCore accepts; normal clients may draw our climbs in steps. We never send CMSG_MOVE_SET_FLY (take-off is
  MSG_MOVE_START_ASCEND with the flying flag, accepted by AzerothCore).
- Which animation the real client plays for a hovering flyer (193 "Hover" vs 41 "SwimIdle"): 193 when the model has it.

Built but untested (no Somnium client, no headset, no second player, no real server in the tests):
- In the Somnium client: the sitting pose on the mount and the hips / eye measurement, taking the avatar over and giving it back, the
  chase camera pausing the client's own camera brain (also with the Tab tablet or the WoW Map open), the "world ahead not ready"
  slow-down, the fall-through rescue and the proxy re-seat after a re-centre (coded and logged, not triggered in the lab).
- In the headset: all VR riding (sticks, steps, steering, the client's snap / smooth turn read back, right A, head-pitch climb), the
  pose-entry undo, the head placement and 0.45 m lean clamp, the 0.15 s blink; the flight-path taxi on the new seat in VR.
- Two Somnium players: the remote rider's placement through the client's interpolation, the 0.12 s state delay, the smoothing
  (stage B, the rider's samples stamped with its frame time in ms) against the client's own delay, a VR rider's relative pose; beyond 15 m the client swaps remote avatars to its culling
  holder, which may not show the sitting pose (the mount stays put). The remote-lab moving shot is not a jitter measurement (the
  lab's frame rate); the harness timeline test is.
- A real AzerothCore: the real packet order (if the server's values update arrives before the mount aura, the Mounted event carries
  spell 0); the Testing (GM) row and the online window footer; the pacing of the ~270 learn commands.
- The mounts' Stand -> Run cross-fade has no lab picture (the lab ran too few frames per blend to catch one); the blend times are
  verified from the files.

Behaviour to know about:
- The mount is hidden at once when you dismount (no fade: WoW's model materials cannot fade without changing how they draw).
- No swimming on foot: when a flying mount is dismissed in water (or you dismount over a lake) you drop into the water and sink to
  the lake bed - terrain water has no surface to hold a walker and the Somnium avatar cannot swim; walk out. Only a mount swims.
- A dismount in the air: you fall from the mount's feet height (on desktop the avatar and the chase camera drop from the saddle to
  there in one frame as the mount disappears; in VR a 0.15 s blink hides that drop).
- The two testing switches are remembered between sessions like your other settings (what they give is never saved): after
  trying "Fly anywhere" off, tick it again.
- VR: when the eye height above the saddle has been measured (about 0.2-0.3 s after mounting, and again after an avatar reload),
  the view eases to it over 0.3 s instead of stepping.
- Indoors is sampled 5 times a second: a galloping rider can get about 2.5 m through a door before being dismounted. Until the terrain
  tile under you is read the spot counts as outdoors and dry (logged once); with "Fly anywhere" off that also means "no flying here".
- At the border of two street groups in Stormwind the indoor check may name the neighbouring group (the lab: 'cathstairs01' next to
  'MB02'); both are outdoors in the game's table.
- Swim, fly, hover and jump animations play at their normal speed (their authored speeds are not in the data we read); a flyer
  hovering within 1-2 m of the ground (take-off, landing) cannot be lowered below the ground, so for a moment the saddle may be up
  to 0.8 m (gryphon) / 1.9 m (proto-drake) above the rider.
- Other Somnium players' flying mounts do not get the per-animation saddle offset your own mount has: a remote rider on a hovering
  gryphon or drake may look sunk into its back.
- Online units (normal clients' riders, NPCs): no jump / land gaits (the server's movement has no jump flag for them), walk vs run
  from their walking flag.
- A Somnium player's WoW character (online) now has its click box and target circle at their drawn avatar also on foot.
- The vertical step filter is a rate limit only (the plan's extra smoothing stage would lag every steady slope by 0.25 m).
- Desktop: A / D always turn (WoW strafes with A / D while the right mouse is held - not copied); the chase camera keeps the pitch
  it started with until the right mouse changes it; the mouse wheel also zooms the ride camera while a WoW window is open.
- The flight-path taxi (on the new seat) is not yet sent as a "taxi" in the network record's new ride fields (other players still
  see it through the iteration-4 taxi flag). If your Player.log shows `Flight: seat measured on the animated mount at (..., 0.3x,
  ...)` for the taxi gryphon (0.3x instead of about 1.4), send it: the taxi's seat was then measured before the model was drawn.
- Offline rules we had to read ourselves: potions and food while mounted dismount you first on the ground and are refused in the
  air (offline items are not spells); a hit now interrupts any cast WoW marks "interrupt on damage" (of the offline spells only Pick
  Lock, which does not work offline anyway); a mount's "required riding" is informational (AzerothCore has no cast-time riding check);
  AzerothCore's male / female model variant of a mount is not applied (server data).
- VR: putting a mount (or any spell) on an action button needs an action bar on screen, i.e. Settings > Display > "Forearm action
  bar" on (it is off by default; the radial menus show the slots but cannot receive a pick).
- Online chat other than system messages and notifications (say, whispers, channels, NPC speech) is still not shown (stage O3); the
  Testing (GM) buttons' commands are a "say" (hidden unless you are a GM automatic account); riding-scaled script mounts cast online
  by the server's script leave the mount spell unknown (0); the spell list's cooldown part is ignored (mounts have none).
- Cosmetic, from before: on desktop the buff row under the player frame touches the top of a left-hand window.

## Known gaps (iteration 6.1, automatic accounts)

- **The account service has never run on the real server** (no agent connects to it): on this PC it ran with a real python
  3.12 but without systemd, Docker or MySQL, so its SQL was only printed (`--dry-run`), never run. The server owner installs it
  with `setup-accounts.sh`, which refuses to finish (`ACCOUNTS FAILED`) unless the service itself reaches the database through
  `docker exec` inside its systemd limits; `sudo wow-account check` checks every database column it writes (read-only). The first
  real account is made by the first "Play online". Oracle's security list must allow TCP 8086 (the user adds that rule).
- If the service's answer is lost on the way back after it made the account (a network cut in that second), the next press says
  the account exists without a key on this computer: the server owner has to unlock it once.
- Somnium LocalStorage (where the key lives) was only used through the members the project already relies on; the render lab
  showed every screen of the flow, but not in the Somnium client or a headset.
- A nickname on the allow list admits only the first Somnium account that registers with it (deliberate: anyone can send any
  nickname); for everyone else the server owner adds the Somnium ID (menu choice 2 lists the players turned away, with their IDs).

## Known gaps (iteration 6, online O1)

- **Never run against a real server yet** (by rule no agent logs in anywhere): the protocol was written from AzerothCore's own
  source and tested end to end against a fake AzerothCore written from that source on the same PC (login -> realm -> characters ->
  create / delete -> enter world -> 220-creature update packets, compressed packets, monster moves, other players, teleports ->
  logout: 26 + 28 checks pass). Only the first real login can prove: the real SRP6 handshake with a real account, the real realm
  list, a real Stormwind / Orgrimmar full of objects (packet sizes, parse speed, drawing hundreds of NPCs in VR), NPC movement
  timing against the real server clock, whether the server accepts our movement (no "cheater" teleports back, no fall damage),
  how normal 3.3.5a clients see a Dreamport player, the 20 s logout, and far teleports (GM `.tele`, instance portals).
- The online login panel and the VR keyboard were rendered in the lab (no headset there): typing with the ray / a poke
  in the headset is untested. On desktop the Somnium client's own keys (W A S D, Q, E, Tab, M, I, Space ...) still act while
  you type into the fields (the client blocks them only for its own chat).
- Ships and zeppelins are drawn where they appear and do not sail; units riding them are not moved with them.
- Swimming and falling off ledges are not reported to the server (only jumps and landings), so no fall damage online.
- The server's time of day is not used: the room's time-of-day buttons still choose the lighting.
- NPC weapons are always drawn in hand; helmets and shoulders are not drawn on other players; flying mounts use the
  walk / run / stand animations (fixed in iteration 7: online units now fly, hover and swim with the mount's own animations).
- The server's real memory and CPU use (Oracle free: 2 cores / 12 GB) is unmeasured until `status.sh` shows it.

## Known gaps (iteration 5)

- The WoW Map has only run in the render lab (Northshire, stand-in markers): the live and classic views line up and the
  coordinates read 48.1, 43.7; map render CPU 2.1-2.7 ms per render in the lab editor (GPU not measured). Untested in the
  Somnium client: the tablet link (the same discovery and injection code as the Euro Truck Sim Dreamport's tablet app, which
  works in the client), pointing at the VR window and the palm button, the desktop `N` window and cursor, other players' dots
  (needs two players), real quest / flight master / hostile markers from the game session.
- The live picture is warmer and darker than WoW's baked minimap tiles (it shows the world as our lighting renders it).
- The map is the world around you only (no continent or zone overview map yet).

## Known gaps (iteration 4 stage A)

- Nothing of iteration 4 has run in the Somnium client, a headset or with two players. Untested there: whether the client's
  teleport (used to re-centre the world every km) blinks or fades, whether it places your feet or your head, the radial menu
  size and twist direction, the 1.5 m/s push of Immersive casting, the palm angle, the HUD's readability, VR flight map clicks,
  and other players seen across origins (their low-detail stand-in may show when they are near you but in another cell).
- On a flight in Somnium you sit on the mount with the client's sitting pose (in the lab the stand-in stands); the seat height
  was measured once and adjusted after the lab (not re-checked).
- Flights: the whole fare is paid at the start (the real server charges per hop), the shortest route is taken; flight master
  names, looks and level 55 are ours; a few odd Outland nodes are listed. Boats and zeppelins are not in (map changes).
- (Iteration 5 added the palm menu's World Map button.) Party frames cannot be clicked (party members are not
  targets offline). The target cast bar has no "not interruptible" shield.
- Grass in 33 Tirisfal detail models is missing from the game files' list; only the zones' climates tested are Elwynn-like.

## Known gaps (iteration 3)

- Nothing of iteration 3 has been heard or seen in the Somnium client: sound (music, footsteps, voices) was only decoded,
  never played to speakers; the settings saving, the new micro button in VR and the avatar animations on a real Somnium
  avatar (and seen by a second player) are untested.
- Seven of the eight zones were built, arrived in and looked at in the lab, but their quests were only played by the
  harness bot (Shadowglen was played by hand in iteration 2). Tell us about quests that do not complete.
- Troll characters get the Orc starting kit in the Valley of Trials (the Troll weapons are not defined in the zone yet);
  Gnomes now get their own warrior/rogue weapons in Coldridge Valley.
- Hunter pets, tracking, Pick Lock/Pocket, resurrection and teleports are not simulated offline; pet commands are greyed
  out (the imp acts on its own). Pet stats, threat numbers and conjured food amounts are our approximations.
- In Valley of Trials the level-1 boars are neutral and the nearest hostile scorpids (level 2-3) can be hard for a level-1
  Hunter without a pet; the named zone leaders (Harlan Crowe, Magistrix Velanya ...) are strong for their level.
- Effects: particle tails are not drawn (the Fireball impact shows small shards), texture rotation is not applied (43 world
  models), missiles fly straight; stereo (both eyes) particles untested. Five zone music tracks listed by the game are not
  in its MPQs (the 'BarrenDry' and 'CursedLand' music sets - silence where they would play). No creature footsteps.
- In VR, walking around your room without using locomotion makes no footsteps.
- Draenei and Blood Elf zones are on map 530: the race screen names it "Outland" (the game's own map name).

Still open from iteration 2: difficulty tuned with a bot (Settings > Gameplay > Difficulty now has easy/normal/hard);
Shadowglen's spiders stand at the cave mouth; sentinel glaives are a polearm; placeholder portraits in the unit frames and
greetings in the quest window instead of speech bubbles; the selection circle is a flat disc; VR arm panel placement and
swing threshold are first guesses; other players' weapons appear only while you play a character. From iteration 1: no
helmets/shoulders on characters; WMO portals unused; Stormwind hitches when its WMO objects are created; animated terrain
layers do not animate; our own sun path; Eastern Kingdoms water defaults.

## Next iteration

- Iteration 11: the first test on your realm, on desktop and in the headset, with friends in the WoW client - vendors, bags, trainers,
  talents, guild / mail / chat, trade and duels, flights and ships, ground spells, forms on the avatar, immersive armour and strike,
  the VR rows and slotting, instances, pets and vehicles - and fixes from its Player.log. The VR values (row step, hold, strike ring,
  long press) are first guesses that need the headset.
- Guild creation with a small group needs your OK for AC_MIN_PETITION_SIGNS (see "Iteration 11 - the complete game", Before you test).
- The pet paperdoll, offline professions, tabard designer, guild bank, arena teams
  and the other items of "Known gaps (iteration 11)".
- Iteration 4 stage B (offline): zones and content for levels 1-30.

## Code layout (Assets/#User/SomniumWoW, asmdef `SomniumWoW`)

| Folder | What |
|---|---|
| `Core/` | WowSpace (WoW <-> Unity: 1 yard = 0.9144 m, (x,y,z) -> (-y,z,x), relative to the current floating origin - a map + a 256-yard grid cell; `Rebased` event), WowShaders (carried shaders), WowLog, WowVersion |
| `Data/` | MPQ reader, DBC tables, BLP decoder, WowInstall, WowFiles; `Data/Unity/WowTextures` |
| `Models/` | M2/.skin parser, particle emitter/ribbon reader (M2Emitters), DBC lookups, character texture composition; `Models/Unity` M2Factory, M2Animator, M2Effects (particles, ribbons, colour/texture animation), WowCharacter/Creatures/Items |
| `World/` | WDT/ADT/WDL/WMO parsers; `World/Unity` WowMapView (streaming), terrain, water, WmoFactory |
| `Play/` | pure C#: `IWowSession` + `WowPlayTypes` (the contract between the game and every view; the server client will implement it later), `Offline/` OfflineSession (combat, spells, summons, AI) + CharacterRecord (save format), `Rules/` formulas, races, gt tables, `Spells/` Spell.dbc + class kits + tooltips, `Content/` zone schema, WowZones registry + the 8 zones |
| `Game/` | WowGame (the scene root), black room, tabs (Play, Sightseeing, Settings), faction/race/class screens, character list, saves, loading screen, lighting/sky, line-up, VR/desktop input |
| `Game/Play/` | WowPlay (session hub), WowSettings, WowEntityViews + `Entities/` (creatures/NPCs, pets, totems, plates, markers, combat text), WowArmUi + `Ui/` (arm panel, desktop HUD, windows, settings, pet bar), WowGear + `Gear/` (weapons on avatars, VR grab/swing, desktop combat animations, other players' plates), `Audio/` (WowAudio + WowAudioDriver: sounds, music, ambience, footsteps), `Vfx/` (WowSpellVfx: spell visuals) |
| `Game/Play/` (iteration 4) | `Origin/` WowOrigin (re-centres the world past 1 km / 500 m, moves every child of the WoW Game object, fires Rebased), `Flight/` WowFlight + FlightMap + FlightRide + `Pure/FlightData` (flight map, riding the route, streaming ahead), `VrInput/` WowVrInput (half-radial menus, immersive push, far target ray), `Details/` WowWorldDetails (ground clutter drawn instanced, critters, bird flocks; pure ClutterDb/ClutterPlacer/CritterTable), `Ui/` UiVrHud (floating HUD), palm menu, UiVignette (low health), WowParty (other players for the party frames); `Gear/` also carries each player's origin, taxi flag and mount in the network record and re-maps other players into your frame |
| `Game/Play/Map/` (iteration 5) | WowTabletMap (the WoW Map: hosts, view, markers, zone + coordinates, `N`, VR/desktop windows), WowMapTabletLink (the "WoW Map" app on the Somnium tablet, ported from the Euro Truck Sim Dreamport; everything it adds is named `WoW.Tablet.` and removed on leave), WowMapCamera (live orthographic camera, fog off and roof cut for its own render), WowMapTiles (classic minimap tiles via md5translate.trs), WowMapPanel (the map panel UI) |
| `Play/Rules/` (iteration 4) | WowTaxiDb (TaxiNodes/TaxiPath/TaxiPathNode: routes through known nodes, race start nodes); flight masters, discovery, TakeTaxi/FinishTaxi, held casts and friendly-target spells in `Offline/OfflineSession.Iter4.cs` |
| `Play/Online/` (iteration 6) | pure C#, harness-tested against a fake AzerothCore: `Net/` the 3.3.5a (12340) client - own BigNum + SRP6, AuthClient (challenge, proof, realm list), WorldClient (RC4 header crypt, large headers, zlib, ping / time sync on the socket thread, Warden never answered), the opcode table generated from AzerothCore's Opcodes.h, CharInfo, NetCodes (readable results), WowLogin (the front-end facade); OnlineSession (the IWowSession: update fields generated from UpdateFields.h, UpdateObjectParser, OnlineMovement + splines, OnlineObject, OnlineDb reactions / item displays / game object models, queries, quest markers, movement out, teleports) |
| `Game/Front/` (iteration 6) | WowGameOnline (login polling, enter world, far teleports, logout), WowOnlineScreen (login / busy / realms / characters / name dialog), WowKeyboard (WoW-styled TMP fields + the VR on-screen keyboard); `Game/Pure/WowOnlineNames` (WoW name rules, server parsing, default looks) |
| `Play/Online/Net/` + `Game/` (iteration 6.1) | WowAutoAccount (the account name from the Somnium ID, the random key, the SRP6 salt + verifier), AccountServiceClient (POST /register to the account service on a worker thread, every answer mapped to a plain result; own small HTTP + JSON reader); `Game/Front/WowGameAutoAccount` (the "Play online as" flow), `Game/Pure/WowAutoKeys` (the key in the player's world storage, two copies), the Settings tab's Online page |
| `Play/Riding/` (iteration 7) | pure C#, harness-tested: MountCatalog (the 309 mount spells from Spell.dbc + SkillLineAbility 777, speeds, cast times, class / race / faction limits, exclusions), MountAreaRules (AzerothCore's flying-area and indoor-only rules, Cold Weather Flying), MountDisplays + MountDisplayTable (the 289 creature entries baked from the user's realm, models learned from a server, labelled stand-ins; emptying the one generated array = learn everything from the server), RideTypes + RideMotion (the ride's pure motion step, the vertical step filter, VR stick steps and shaping), RideGait (which animation at which rate), RideWire + RideTimeline (the Gear record's ints 17-27 and the Euro Truck Sim smoothing timeline port) |
| `Game/Play/Mounts/` (iteration 7) | WowMount (the local rider hub: Update order -500, LateUpdate after the pose), MountSeat (takes the avatar over like the taxi and the ETS seat - the taxi uses it too now), MountMotor (the hidden proxy CharacterController 'WoW Mount Proxy' on layer 2, touching only terrain and buildings), MountInput (desktop keys, VR sticks), MountCamera (the desktop chase camera), MountModel (the mount model, its fixed seat, the per-animation saddle offset), MountPlace (indoors / outdoors / liquid as AzerothCore decides) |
| other iteration 7 files | `Play/Offline/OfflineSession.Mounts.cs` (the offline mount rules, speeds, forced dismounts), `Play/Online/OnlineSession.Spells.cs`, `.Mounts.cs`, `.Rider.cs`, `.Chat.cs` + `SpellCastResults.cs` (the server's spellbook, casts, auras, the rider's movement sender, GM commands), `World/WmoAreaTable.cs` + `World/Unity/WmoGroupInfo.cs` (namespace `World.Tags`: the WoW MOGI struct already has that name), `Models/M2Events.cs` (footstep / wing-flap event times), `M2Animator.CrossFade`, `Game/Play/Ui/UiMounts.cs` (the Mounts pane), `Game/Play/Audio/AudioMountSteps.cs`, `Game/Play/Gear/GearRideGait.cs`, `Networking/WowNetWire.cs` (Used = 28, WowRideWriter) |
| stage O2 files (iteration 8) | `Play/Online/OnlineSession.Items.cs` (inventory, item queries, equip / use, inventory errors, item push), `.Combat.cs` (melee, every unit's swings, XP, levels, kills, death), `.Spells.cs` (every class spell, every caster's cast events, auras, cooldowns, action bar; with the iteration-7 mount code) + `SpellCastResults.cs`, `.Loot.cs` + `.Death.cs` (loot window, release, ghost, corpse, reclaim, spirit healer, resurrect requests), `.Quests.cs` (Interact routing, gossip, quest giver dialogs, quest log, game object use); `Play/Offline/OfflineSession.Ghost.cs` (WoW's death flow offline, AzerothCore's rules); `Game/Play/Ui/UiGhost.cs` + `UiDialogs.cs` UiDeath (death popups, corpse arrow / compass / marker, the ghost-world look), ghost music in `Game/Play/Audio/AudioMusic.cs`; the corpse on the WoW Map (`Game/Play/Map`); `Game/Play/Gear/GearCast.cs` + `GearLocal.cs` + `Networking/WowNetWire.cs` (Used = 31: the cast / timing / release ints 28-30), `Game/Play/Vfx/WowSpellVfx.cs` (a Somnium character's server effects on the avatar); lead: `Game/Play/WowRunSpeed.cs`, `Game/Play/VrInput/VrTabletGuard.cs`, `WowArmUi.CloseWindowsWhenMoving` |
| stage S1 files (iteration 9) | `Play/Online/OnlineSession.Chat.cs` (every chat type in and out) + `.Group.cs` (invites, the group list, member stats, WoW's party texts), `Play/Offline/OfflineSession.Social.cs` (the offline answers), `WowPlayTypes.cs` (chat / group contract, `ImmersiveMode`: the +10 % constant and the "Immersive Mode" buff); `Game/Play/Ui/UiChat.cs` (desktop ChatFrame1 + edit box, the VR chat window), `Game/Play/Entities/WowChatBubbles.cs`, `Game/Play/Ui/UiGroup.cs` (invite popup, unit menu, VR Party menu), `UiPortraits.cs` (Somnium profile pictures, memory only), `Game/Play/WowParty.cs` (the server group merged with the Somnium players); `Game/Play/WowFocus.cs` + `VrInput/VrFocusButton.cs` (focus, X / H), `Ui/UiHud.cs` + `UiVrHud.cs` + `UiSettings.cs` (HUD placement, party / focus frames, the HUD page, Return to the room, the palm Chat / Party buttons); `VrInput/VrTargetRay.cs` (VrClientLaser: the Somnium laser) + `WowVrInput.cs`, `Ui/UiPointer.cs`; `Game/Access/WowTags.cs` (name tags), `Game/Vr/WowWrist.cs`, `Networking/WowNet.cs` + `WowNetAlone.cs` (session diagnostics, the alone hint); immersive hold: `Play/Online/OnlineSession.Spells.cs` (DPHOLD), `Play/Offline/OfflineSession.Spells.cs` / `.Iter4.cs` (start first, the offline +10 %), `Game/Play/VrInput/VrImmersive.cs`; the game folder: `Data/WowInstall.cs`, `Game/Front/WowInstallPicker.cs`, Settings > Game in `WowSettingsTab.cs` |
| `ServerKit/` (project root, not in Assets) | setup-server.sh, status.sh, connect.md, README.md: the AzerothCore realm on a droplet / Oracle Cloud with Warden off; iteration 6.1: setup-accounts.sh, `account-service/` (wow_accounts.py = the account service + admin code, the `wow-account` menu, the systemd unit), `Manage WoW accounts.cmd`; stage S1: `setup-hold.sh` + `dreamport-hold/` (mod-dreamport-hold, the AzerothCore module for the immersive hold and its +10 %) |
| `Networking/` | SceneNetworking (Community Modules copy), WowNet + WowNetWire (the 50 player slots) |
| `Shaders/`, `Resources/` | SomniumWoW/M2, Sky, Terrain, Water, Wmo, Clutter (every opaque one with DepthOnly + DepthNormals + ShadowCaster passes and single-pass stereo), Particle (transparent) and their carrier materials |
| `Editor/` | WowSceneGuard (keeps the carriers and the linear fog in the scene) |

Bump `Core/WowVersion.cs` (Iteration, Date, Summary) and add a row to "Iterations" on every upload.
