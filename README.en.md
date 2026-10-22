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
3. **Play** tab — pick or create a world, press “Play”. In SL: Play → Direct connect → Ctrl+V.

If the overlay covers something: **Ctrl+Shift+F12** turns it off/on, **Ctrl+Shift+F11** shows the mask.
**Ctrl+Shift+F10** — quick exit (closes SL, Minecraft with saving, the server and the overlay; same as the “Quit” button in SL: Esc → Settings → Server-specific).

You start with a machete (to break blocks and fight) and one medkit — everything else is crafted. When you die, everything, SL items included, drops on the ground as Minecraft items.

## Controls

* Everything from SCP:SL: walking, sprinting, jumping, weapons, inventory (Tab), admin panel (M).
* Shooting a block breaks it (harder blocks take more hits); shooting a mob damages it.
* Hold **E** on an item to pick it up; **Q** drops a stack.
* **E** (interact) — place a block, doors/levers, food, flint and steel/bucket/eye of ender, crafting table/furnace/chest (Minecraft window).
* **I** — Minecraft inventory with 2×2 crafting. Keys can be rebound in SL: Esc → Settings → Server-specific.
* SL console (`~`): `.mc time set day`; in the admin panel: `mc give @s diamond 5`.
* Darkness is real: hold a lit **lantern** (lights up around you) or a **flashlight** (a narrow beam where you look).

## Crafting

Every SL item is crafted on a Minecraft crafting table (E on the table). The result immediately becomes a real SCP:SL item in your inventory; ammo is added 80 at a time. Shapes can be mirrored horizontally, and small shapes can go anywhere in the grid. The vanilla iron and diamond chestplates are replaced by SL light and combat armor (in the bridge, Minecraft armor comes from your SL armor anyway). Darkness in Minecraft is real — craft a lantern and hold it lit in your hand (or a flashlight — it shines a narrow beam where you look). The drawings are also in the launcher, “⚒ Crafting” tab.

Legend: `#` — Iron ingot, `B` — Iron block, `D` — Diamond, `N` — Netherite ingot, `G` — Gunpowder, `S` — Stick, `R` — Redstone, `T` — Torch, `X` — TNT, `L` — Glowstone dust, `U` — Sugar, `O` — Glass bottle, `C` — Cocoa beans, `E` — Spider eye, `M` — Slimeball, `H` — Leather cap, `P` — Ender pearl, `F` — Flower pot, `I` — Packed ice, `Y` — Blue ice, `W` — Bowl, `K` — Note block, `V` — Eye of ender, `Q` — Glass pane, `A` — Glass, `J` — Coal, `·` — empty.


### Weapons

```
COM-15    COM-18    COM-45
# # ·     # # #     # # #
# · ·     # · ·     # # ·
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
# # #         # # #         # # #
# # #         # # ·         · # #
· # ·         # · ·         · # ·
```
```
Shotgun    Logicer    FR-MG-0
# # #      # # #      # # #
# # #      # # #      # · #
# · #      # # ·      # # ·
```

### Special weapons

```
SCP-127               Particle Disruptor    Micro H.I.D.
# # D                 # # B                 # # #
# · ·                 # · ·                 D D D
· · ·                 · · ·                 # # #
```
```
Jailbird              Machete (SCP-1509)
D · ·                 · · #
# · ·                 · # ·
# · ·                 S · ·
```

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
- **Adrenaline** — in any order: Golden apple
- **SCP-500 (panacea)** — in any order: Enchanted golden apple

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

## Authors

**Claude x Vamatrik** — idea, testing and direction — Vamatrik; architecture and code — Claude (Anthropic).
