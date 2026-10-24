# SCP:SL × Minecraft

**Authors: Claude x Vamatrik** · [Русский](README.md)

Two games run **at the same time**, and each one draws its own part:

* **Minecraft 26.3 (Fabric)** — the world, blocks, mobs, light, sky, the Nether, the End, the dragon;
* **SCP: Secret Laboratory** — your character, movement, the weapon in your hands, inventory, interface, admin panel (M).

The bridge connects the logic: you walk on Minecraft blocks with SL physics, your shots break blocks and hurt mobs,
mobs hit you (damage ×5 for SL's 100 HP), you pick up items with E, craft on a real crafting table, smelt in a furnace
and travel to the Nether and the End. **The SCP:SL client is not modified** — it is the regular game from Steam.

The whole mod (launcher, SL plugin, Minecraft mod) speaks **Russian or English** — pick the language in the launcher sidebar.

## How it works

```
 SCP:SL (Steam, unmodified) ──game── Dedicated Server + LabAPI plugin ──TCP 25599── Minecraft + Fabric mod
            │                                                                          │
            └──────── window ─────►  overlay (Windows Graphics Capture)  ◄──── window ─┘
```

| Part | Folder | What it does |
|---|---|---|
| SL plugin (LabAPI) | `src/sl-plugin` | invisible colliders for Minecraft blocks, camera pose, shots, damage, item stacks, menu, commands |
| Minecraft mod (Fabric) | `src/fabric` | camera = SL camera, hits, colliders, items, MC windows, portals, falling, armor, SL item recipes |
| Overlay | `src/compositor` | captures the SL and Minecraft windows; wherever SL shows the green key backdrop, Minecraft is drawn. Input goes straight to SL |
| Launcher | `src/launcher` | setup (finds the games, installs mod/plugin), picks/creates a world, starts and stops everything |

The protocol between the parts is in [`docs/PROTOCOL.md`](docs/PROTOCOL.md).

## Play

1. Download the archive from [Releases](../../releases), unpack it, run `SlBridgeLauncher.exe`.
2. **Setup** tab — go through the steps: SCP:SL and SCP:SL Dedicated Server from Steam (same version),
   Minecraft 26.3 + Fabric (easiest with [Prism Launcher](https://prismlauncher.org)), the “Install mod” button.
3. **Play** tab — pick or create a world, press “Play”. In SL: Play → Direct connect → Ctrl+V. Old worlds can be removed right there with **“Delete”** (to the Recycle Bin); **“Worlds folder”** opens them in Explorer.

> **SL window mode must be Borderless.** In exclusive fullscreen Windows shows no windows on top of the game, so you see the green backdrop instead of Minecraft. The launcher detects this and tells you. Any resolution or stretching (e.g. 4:3 via NVIDIA) is fine. The launcher starts SL through Steam without launch options, so the mode comes from the game's own settings.

If the overlay covers something: **Ctrl+Shift+F12** turns it off/on, **Ctrl+Shift+F11** shows the mask.
**Ctrl+Shift+F10** — quick exit (closes SL, Minecraft with saving, the server and the overlay; same as the “Quit” button in SL: Esc → Settings → Server-specific).

!!!IMPORTANT!!! Minecraft pause menu can be over your game wingow while playing, press F3 + P buttons in Minecraft (in the minecraft window). After this if you change windows minecraft won't be paused.
!!!!!!!!!!!!!!!!!!!!!!!!Also you must turn off SCP: SL facility lights to correctly see greenscreen.

You start with a machete (to break blocks and fight) and one medkit — everything else is crafted. When you die, everything, SL items included, drops on the ground as Minecraft items.

## Controls

The main options (role, jump, light, green screen, performance) are in the launcher, **“⚙ Settings”** tab. Bridge keys live in SL: **Esc → Settings → Server-specific**. The launcher sets them up on the first run; you can change them there.

| Key | What it does |
|---|---|
| walk, sprint, jump, crouch, Tab, M | same as in SCP:SL (SL inventory, admin panel) |
| LMB / RMB | shoot and hit; a block breaks, a mob takes damage |
| **E** | Minecraft interact: place the held block, door, lever, button, food, flint and steel, bucket, eye of ender, bed; open a crafting table, furnace, chest |
| hold **E** on an item | pick up a Minecraft item from the ground |
| **Q** | drop an item (it becomes a Minecraft item) |
| **K** | Minecraft inventory with 2×2 crafting |
| **U** | SL crafting book — recipes in Minecraft style (close: U or Esc) |
| **O** | Minecraft settings (close: O or “Done”) |
| **F4** / **F4+B** / **F4+G** | Minecraft debug screen / hitboxes / chunk borders (F3 is SL's own debug) |
| `~` → `.mc <command>` | a Minecraft command, e.g. `.mc time set day`; in the admin panel — `mc time set day` |
| `~` → `.kit start` | get the starter kit again |
| `~` → `.enchant <enchantment> [level]` | enchant the SL item in hand: `.enchant sharpness 5`; `.enchant list` — what fits, `.enchant clear` — remove |
| `~` → `.bridge` | help for the bridge commands |
| Ctrl+Shift+F10 | quick exit (or the “Quit” button in Server-specific) |
| Ctrl+Shift+F12 / F11 | turn the overlay off / show the green-screen mask |

In Minecraft windows (crafting table, inventory, chest) drag items with the mouse; close with **E** or **Esc**.

## Mechanics

* **World.** Blocks are real obstacles for SL: you walk on them and can't pass through. Shots and hits break blocks (harder blocks take more hits) with normal Minecraft drops.
* **Mobs.** Minecraft computes damage to them (SL damage ÷ 5); they hit back and the damage arrives in SL. Endermen dodge bullets like arrows. SL armor works as Minecraft armor.
* **Melee like in SL.** The machete and Jailbird hit an area in front: the main target takes full damage, others — half (machete) or full (Jailbird). A Jailbird charge stops at a mob; the Jailbird wears out and explodes on its last charge when broken.
* **Multitools.** A sword + a pickaxe of the same material → an SL machete of that tier: from wood to netherite, damage and digging speed grow (gold digs fastest but hits weakly). A hint shows the tier when you pick it up. You start with a wooden one.
* **SCP-268 (cap)** makes you invisible to Minecraft mobs too: they lose track of you while it lasts.
* **SCP-127** levels up from damage to mobs; kills give bonus XP, ammo and a kill line like for a human. Bosses get their own commentary: Warden — like SCP-939, Ender Dragon — like SCP-106, Wither — like SCP-049 (once 127 has befriended you).
* **Boss health bar** (dragon, wither) is shown at the top of the screen.
* **Your role** in the Minecraft world is chosen in the launcher, “⚙ Settings” tab: Class-D (default), Tutorial, Scientist, Facility Guard, MTF, Chaos Insurgency (their uniform is green — parts may show through). **Micro H.I.D.** damage and range match the game. **Grenades and SCP-018** blow up the Minecraft world too.
* **Water and lava.** You float up by yourself; crouch or walk slowly to dive; swim into the shore to climb out. Water cancels fall damage. Lava burns; fire goes out after death.
* **Ladders and vines.** Look up to climb, down to descend, straight to hang. No fall damage on them.
* **Falling** follows Minecraft rules: damage = height − 3 blocks (hay, honey, beds, slime soften it).
* **Darkness is real.** Hold a lit **lantern** (lights around you) or a **flashlight** (light a few metres ahead).
* **Portals and teleports.** Nether, End, ender pearls, `/tp` all work: the world is built around the arrival point before you are released.
* **Bed** — sleep through the night, and it sets your respawn point any time.
* **Death.** Everything, SL items included, drops as Minecraft items (`.mc gamerule keep_inventory true` to keep them). You respawn at your bed or world spawn.
* **Your SL inventory** is saved in the world folder and comes back next time; each world has its own. Item state (attachments, loaded ammo, Micro H.I.D. charge, Jailbird wear, SCP-127 and multitool tiers) is kept everywhere.
* **Villager trading** — E on a villager opens the usual trading window.
* **Enchanting.** SL items can be enchanted at an enchanting table, with books on an anvil (and with `.enchant`); a grindstone removes enchantments. Enchantments stay on the item everywhere — when dropped, on death, when recharged at a crafting table, between sessions. Consumables can't be enchanted.

  | Item | Enchantments |
  |---|---|
  | Firearms | **Power** (+10% damage per level), **Flame** (sets on fire), **Piercing** (the bullet goes through +1 mob per level), **Infinity** (about every third shot is free). No Punch |
  | Micro H.I.D. | the same + **Unbreaking** (drains slower) and **Mending** (experience → +1% charge per point) |
  | Machete and multitools | **Sharpness, Smite, Bane of Arthropods, Fire Aspect, Knockback, Looting** — like a sword; **Sweeping Edge** — more damage to the other targets in the area; **Efficiency** (+25% digging per level), **Fortune**, **Silk Touch** |
  | Jailbird | melee enchantments + **Unbreaking** (less wear) and **Mending** (experience repairs it) |
  | Armor | all **Protection** types, **Thorns**, **Feather Falling**, **Respiration**, **Depth Strider** (faster swimming up and down) — copied onto the Minecraft armor |

  Enchanted books and other Minecraft items, potions and renamed things keep everything in the SL inventory too — the card name shows their enchantments.
* **Boats, minecarts and horses** can't be ridden: movement in the bridge is driven by SL. Feeding and breeding animals works.
* **The void.** Falling below the world (the End, a hole in bedrock) kills you, like in Minecraft.
* **No hunger** — food is crafted into medkits (see Crafting). Experience is picked up automatically.

## Performance

Two games share one graphics card and the overlay combines their pictures (the overlay itself costs a fraction of a millisecond per frame). Extra frames in one game are taken from the other, so:

* **Run SL in Borderless window mode**, otherwise the overlay is hidden (see above).
* **Cap FPS at 60 in each game** — in the SCP:SL graphics settings and in the Minecraft settings (**O** in game). This usually makes the real result noticeably smoother.
* Turn off VSync in Minecraft and enable Sodium (checkbox in the launcher).
* SL graphics can be low: SL only draws the weapon and UI, Minecraft draws the world.
* For more FPS: `"McRenderScale": 0.75` in `dist/compositor/compositor.json` makes Minecraft render about half the pixels (the world gets slightly softer, crafting windows stay full size).

## Crafting

All recipes are also in the game: **U** opens the SL crafting book in Minecraft style — with real items and hover tooltips. The launcher’s “⚒ Crafting” tab draws them with item icons too (taken from your installed Minecraft).

Every SL item is crafted on a Minecraft crafting table (E on the table). The result immediately becomes a real SCP:SL item in your inventory; ammo is added 80 at a time. Shapes can be mirrored horizontally, and small shapes can go anywhere in the grid. The vanilla iron and diamond chestplates are replaced by SL light and combat armor (in the bridge, Minecraft armor comes from your SL armor anyway). Darkness in Minecraft is real — craft a lantern and hold it lit in your hand (or a flashlight — it shines a narrow beam where you look). Your SL items show up in Minecraft windows too — the Micro H.I.D., Jailbird and Particle Disruptor can be recharged on a crafting table. The drawings are also in the launcher, “⚒ Crafting” tab.

Legend: `#` — Iron ingot, `B` — Iron block, `D` — Diamond, `N` — Netherite ingot, `G` — Gunpowder, `S` — Stick, `R` — Redstone, `T` — Torch, `X` — TNT, `L` — Glowstone dust, `U` — Sugar, `O` — Glass bottle, `C` — Cocoa beans, `E` — Spider eye, `M` — Slimeball, `H` — Leather cap, `P` — Ender pearl, `F` — Flower pot, `I` — Packed ice, `Y` — Blue ice, `W` — Bowl, `K` — Note block, `V` — Eye of ender, `Q` — Glass pane, `A` — Glass, `Z` — Copper ingot, `J` — Coal, `r` — Redstone block, `w` — Sword (any material), `p` — Pickaxe of the same material, `*` — The same item (used), `·` — empty.


### Weapons

```
COM-15    COM-18    COM-45
# # ·     # # #     # # #
Z · ·     # · ·     # # ·
· · ·     · · ·     · · ·
```
```
.44 Revolver    FSP-9           Crossvec
# # #           # # #           # # #
· # ·           # · ·           # # #
· · ·           # · ·           # · ·
```
```
MTF-E11-SR    AK            A7
# R #         # # #         # # #
# # #         # # ·         · # #
· # ·         # · ·         · # ·
```
```
Shotgun    Logicer    FR-MG-0
# # #      # # #      # # #
# # #      # # #      # · #
# · #      # B ·      # # ·
```

### Special weapons

```
SCP-127               Particle Disruptor    Micro H.I.D.
# # D                 # # B                 # # #
# E ·                 # · ·                 D D D
· · ·                 · · ·                 # # #
```
```
Jailbird              Machete (SCP-1509)
D · ·                 · D ·
r · ·                 # # #
# · ·                 · S ·
```
- **Multitool (SL machete): wood, stone, copper, iron, gold, diamond, netherite** — in any order: Sword (any material) + Pickaxe of the same material

### Ammo

```
9x19 ammo (80)       5.56x45 ammo (80)    7.62x39 ammo (80)
G · ·                G · ·                G · ·
# · ·                # · ·                G · ·
· · ·                # · ·                # · ·
```
```
12 gauge ammo (80)    .44 ammo (80)
G G ·                 # G #
# # ·                 · · ·
· · ·                 · · ·
```

### Armor

```
Light Armor     Combat Armor    Heavy Armor
# · #           D · D           D N D
# # #           D D D           D D D
# # #           D D D           D D D
```

### Equipment

```
Radio         Flashlight    Lantern
# · ·         T · ·         J J J
R · ·         # · ·         S S S
# · ·         # · ·         J J J
```
- **Frag Grenade** — in any order: Iron ingot + TNT
- **Flashbang** — in any order: Iron ingot + Glowstone dust + Gunpowder

### SCP

```
SCP-1344 (goggles)
Q V Q
· · ·
· · ·
```
- **SCP-207** — in any order: Sugar + Sugar + Cocoa beans + Glass bottle
- **Anti-SCP-207** — in any order: Sugar + Sugar + Spider eye + Glass bottle
- **SCP-018 (ball)** — in any order: Slimeball + Slimeball + Redstone
- **SCP-268 (cap)** — in any order: Leather cap + Ender pearl
- **SCP-244-A (vase)** — in any order: Flower pot + Packed ice
- **SCP-244-B (vase)** — in any order: Flower pot + Blue ice
- **SCP-1853** — in any order: Glass bottle + Glowstone dust + Sugar
- **SCP-2176 (bulb)** — in any order: Glass + Redstone + Glowstone dust
- **SCP-330 (candy)** — in any order: Bowl + Sugar + Sugar + Sugar
- **SCP-1576 (gramophone)** — in any order: Note block + Ender pearl

### Medical

- **Painkillers** — in any order: Sugar + Sugar + Glass bottle
- **Adrenaline** — in any order: Sugar + Glass bottle + Glistering melon slice
- **SCP-500 (panacea)** — in any order: Golden apple
- **SCP-500 (panacea) ×8** — in any order: Enchanted golden apple

### Recharge

```
Micro H.I.D. — recharge          Jailbird — repair                Particle Disruptor — recharge
r r r                            r r r                            r r r
r * r                            r * r                            r * r
r r r                            r r r                            r r r
```

### Medkits from food

10 hunger points = 1 medkit. Put several identical food items into the grid in any order:

| Food | Hunger | How many | Medkits |
|---|---|---|---|
| Rabbit stew | 10 | 1 | 1 |
| Steak | 8 | 2 | 1 |
| Steak | 8 | 5 | 4 |
| Cooked porkchop | 8 | 2 | 1 |
| Cooked porkchop | 8 | 5 | 4 |
| Pumpkin pie | 8 | 2 | 1 |
| Pumpkin pie | 8 | 5 | 4 |
| Beetroot soup | 6 | 2 | 1 |
| Beetroot soup | 6 | 5 | 3 |
| Cooked chicken | 6 | 2 | 1 |
| Cooked chicken | 6 | 5 | 3 |
| Cooked mutton | 6 | 2 | 1 |
| Cooked mutton | 6 | 5 | 3 |
| Cooked salmon | 6 | 2 | 1 |
| Cooked salmon | 6 | 5 | 3 |
| Golden carrot | 6 | 2 | 1 |
| Golden carrot | 6 | 5 | 3 |
| Honey bottle | 6 | 2 | 1 |
| Honey bottle | 6 | 5 | 3 |
| Mushroom stew | 6 | 2 | 1 |
| Mushroom stew | 6 | 5 | 3 |
| Suspicious stew | 6 | 2 | 1 |
| Suspicious stew | 6 | 5 | 3 |
| Baked potato | 5 | 2 | 1 |
| Bread | 5 | 2 | 1 |
| Cooked cod | 5 | 2 | 1 |
| Cooked rabbit | 5 | 2 | 1 |
| Apple | 4 | 3 | 1 |
| Apple | 4 | 5 | 2 |
| Chorus fruit | 4 | 3 | 1 |
| Chorus fruit | 4 | 5 | 2 |
| Rotten flesh | 4 | 3 | 1 |
| Rotten flesh | 4 | 5 | 2 |
| Raw beef | 3 | 4 | 1 |
| Carrot | 3 | 4 | 1 |
| Raw porkchop | 3 | 4 | 1 |
| Raw rabbit | 3 | 4 | 1 |
| Raw chicken | 2 | 5 | 1 |
| Raw cod | 2 | 5 | 1 |
| Cookie | 2 | 5 | 1 |
| Glow berries | 2 | 5 | 1 |
| Melon slice | 2 | 5 | 1 |
| Raw mutton | 2 | 5 | 1 |
| Poisonous potato | 2 | 5 | 1 |
| Raw salmon | 2 | 5 | 1 |
| Spider eye | 2 | 5 | 1 |
| Sweet berries | 2 | 5 | 1 |
| Beetroot | 1 | 9 | 1 |
| Dried kelp | 1 | 9 | 1 |
| Potato | 1 | 9 | 1 |
| Pufferfish | 1 | 9 | 1 |
| Tropical fish | 1 | 9 | 1 |

## Building from source

You need: .NET SDK 8+, JDK 25, Windows (the overlay and launcher need Windows 10 1903+).

```powershell
# SL plugin (takes the game libraries from SCP:SL Dedicated Server in Steam; other path: -p:SLManaged=...)
dotnet build -c Release src/sl-plugin

# SL item recipes (generates the mod's datapack, language files and the launcher table)
python3 tools/gen_recipes.py src/fabric/src/main/resources src/launcher crafts
# Minecraft mod
cd src/fabric; ./gradlew build; cd ../..          # → src/fabric/build/libs/slbridge-fabric-*.jar

# overlay and launcher
dotnet publish -c Release -r win-x64 --self-contained -p:PublishSingleFile=true src/compositor
dotnet publish -c Release -r win-x64 --self-contained -p:PublishSingleFile=true src/launcher
```

Layout for the launcher: next to `SlBridgeLauncher.exe` there is a `dist/` folder with `SlBridge.dll`, `slbridge-fabric-*.jar`,
`fabric-api-*.jar` ([Modrinth](https://modrinth.com/mod/fabric-api)) and `compositor/SlBridgeCompositor.exe`.

## Safety

* The SCP:SL client and its anticheat are not touched: the overlay only **captures window images** (Windows Graphics Capture)
  and draws on top; clicks pass through it.
* Network: only `127.0.0.1` (the bridge between plugin and mod, port 25599) and the local SL server (7777).
* All binaries in the release are built from this repository — check them on VirusTotal or build them yourself.

## Videos and streams

Feel free to use the bridge in your YouTube videos and streams (or anywhere else). If you want, you can leave the author's channel in the description: [youtube.com/@vamatrikultimate](https://www.youtube.com/@vamatrikultimate) — it's much appreciated.

## Contact

Questions, bugs and ideas — message me on Discord: **vamatrultra**. You can also report bugs in [Issues](../../issues).

## Authors

**Claude x Vamatrik** — idea, testing and direction — Vamatrik; architecture and code — Claude (Anthropic).

## License

[MIT](LICENSE) — free to use, modify and distribute as long as the authors are credited. SCP: Secret Laboratory and Minecraft belong to their owners (Northwood Studios, Mojang); the bridge contains none of their files.
